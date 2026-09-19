<div align="center">

# Recipe · DBVM status and physical memory

**Ask whether DBVM is available without ever failing loudly, then read physical memory and watch writes when it is.**

**Level** `Advanced` · **Time** `30 min` · **Needs** `Guide 08`

[Examples index](../../README.md) · [Recipes](../README.md)

</div>

---

|                            |                                                                                                                                                                                                      |
|----------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **You build**              | A status probe, a physical memory reader and a physical write watcher that all degrade gracefully                                                                                                    |
| **You learn**              | Try forms as a design rule, reading a Lua byte table into a `Span<byte>`, cleaning up a watch in `OnDisable`                                                                                         |
| **You need**               | The Lua toolkit from [08 · Running Lua](../../08-running-lua/README.md): `LuaState`, `LuaFrame` and the sequence helpers                                                                             |
| **Cheat Engine functions** | `dbk_initialized`, `dbvm_initialized`, `dbvm_initialize`, `dbvm_getMemory`, `dbk_getPhysicalAddress`, `dbvm_readPhysicalMemory`, `dbvm_watch_writes`, `dbvm_watch_retrievelog`, `dbvm_watch_disable` |

> [!CAUTION]
> DBVM operations are optional. They need the Cheat Engine driver, they need DBVM to be running, and the machine can
> refuse them: the driver may fail to load, for example when Windows was not started with support for unsigned drivers.
> Every binding in this recipe is a Try form, so a plugin that runs on a machine without DBVM answers "not available"
> and keeps working.

## Objective

Report the state of the driver and of DBVM in one line, read a few physical bytes, and count the writes DBVM sees at a
virtual address of the game. None of it may throw, and none of it may load a driver on its own.

## Why it matters

DBVM sits below the operating system, so a mistake there costs more than a mistake in a normal process. The safe shape
is a plugin that asks first, changes nothing until the user says so, and treats "not available" as an ordinary answer.
Try forms make that the default: a missing function, a raised error or a `nil` result becomes `false`.

## How it works

### 1. Bind the functions

```csharp
using CESDK.Annotations.Lua;
using CESDK.Lua.Calls;

namespace DbvmProbe;

internal static partial class DbvmCalls
{
    [LuaGlobal("dbk_initialized")]
    public static partial bool TryDriverLoaded(out bool loaded);

    [LuaGlobal("dbvm_initialized")]
    public static partial bool TryDbvmLoaded(out bool loaded);

    [LuaGlobal("dbvm_getMemory")]
    public static partial bool TryGetMemory(out long freeBytes, out long fullPages);

    [LuaGlobal("dbk_getPhysicalAddress")]
    public static partial bool TryGetPhysicalAddress(nuint virtualAddress, out nuint physicalAddress);

    [LuaGlobal("dbvm_watch_writes")]
    public static partial bool TryWatchWrites(nuint physicalAddress, int size, int options, out int id);

    [LuaGlobal("dbvm_initialize")]
    internal static partial void InitializeRaw(bool offloadOs);

    [LuaGlobal("dbvm_watch_disable")]
    internal static partial void DisableWatchRaw(int id);

    public static bool TryInitialize()
    {
        try
        {
            InitializeRaw(false);
        }
        catch (LuaException)
        {
            return false;
        }

        return TryDbvmLoaded(out var loaded) && loaded;
    }

    public static bool TryDisableWatch(int id)
    {
        try
        {
            DisableWatchRaw(id);
            return true;
        }
        catch (LuaException)
        {
            return false;
        }
    }
}
```

`dbk_initialized` and `dbvm_initialized` only report. They never load anything, which is exactly why the probe uses
them and not `dbk_initialize`. `dbvm_initialize(false)` asks Cheat Engine to prepare its DBVM functions without moving
the operating system onto DBVM, and the wrapper confirms the result with `dbvm_initialized`. `dbvm_initialize` and
`dbvm_watch_disable` are bound as `void` because the recipe does not use their results, so their wrappers catch
`LuaException` to keep the "never throws" promise.

### 2. Read the state, read the bytes, watch the writes

```csharp
using CESDK.Annotations.Lua;
using CESDK.Annotations.Plugin;
using CESDK.Engine.Values;
using CESDK.Hosting.Plugin;
using CESDK.Lua.Marshalling;
using CESDK.Lua.Runtime;
using CESDK.Lua.State;

namespace DbvmProbe;

[CheatEnginePlugin("DBVM Probe")]
public sealed class DbvmProbePlugin : CheatEnginePlugin
{
    protected override void OnEnable()
    {
        var status = Probe.RegisterLuaFunctions(LuaRuntime.AcquireState());
        if (!status.IsOk) throw new InvalidOperationException($"Lua registration failed: {status}.");
    }

    protected override void OnDisable()
    {
        Probe.Stop();
        Probe.UnregisterLuaFunctions(LuaRuntime.AcquireState());
    }
}

internal static partial class Probe
{
    private static int s_watchId = -1;

    [LuaFunction("my_plugin_dbvm_status")]
    public static string Status()
    {
        var driver = DbvmCalls.TryDriverLoaded(out var loaded) && loaded ? "yes" : "no";
        if (!DbvmCalls.TryDbvmLoaded(out var running) || !running) return $"driver {driver}, dbvm no";

        return DbvmCalls.TryGetMemory(out var free, out var pages)
            ? $"driver {driver}, dbvm yes, {free} bytes free in {pages} full page(s)"
            : $"driver {driver}, dbvm yes";
    }

    [LuaFunction("my_plugin_dbvm_start")]
    public static bool Start() => DbvmCalls.TryInitialize();

    [LuaFunction("my_plugin_read_physical")]
    public static string? ReadPhysical(nuint physicalAddress, int count)
    {
        if (count is < 1 or > 4096) return null;

        Span<byte> buffer = stackalloc byte[count];
        return TryReadPhysical(physicalAddress, buffer) ? Convert.ToHexString(buffer) : null;
    }

    [LuaFunction("my_plugin_watch_writes")]
    public static long WatchWrites(nuint virtualAddress, int size)
    {
        if (Volatile.Read(ref s_watchId) >= 0) return -1;
        if (!DbvmCalls.TryGetPhysicalAddress(virtualAddress, out var physical)
            || !DbvmCalls.TryWatchWrites(physical, size, 0, out var id)) return -1;

        Volatile.Write(ref s_watchId, id);
        return id;
    }

    [LuaFunction("my_plugin_watch_hits")]
    public static long WatchHits(LuaState state)
    {
        var id = Volatile.Read(ref s_watchId);
        if (id < 0) return -1;

        using LuaFrame frame = new(state);
        if (!state.TryGetGlobal("dbvm_watch_retrievelog"u8).IsOk) return -1;

        Int32Marshaller.Push(state, id);
        return state.TryCall(1, 1).IsOk && state.IsTable(-1) ? state.RawSequenceCount(-1) : -1;
    }

    [LuaFunction("my_plugin_watch_stop")]
    public static bool Stop()
    {
        var id = Interlocked.Exchange(ref s_watchId, -1);
        return id < 0 || DbvmCalls.TryDisableWatch(id);
    }

    private static bool TryReadPhysical(nuint physicalAddress, Span<byte> destination)
    {
        var state = LuaRuntime.AcquireState();
        using LuaFrame frame = new(state);
        if (!state.TryGetGlobal("dbvm_readPhysicalMemory"u8).IsOk) return false;

        AddressMarshaller.Push(state, physicalAddress);
        Int32Marshaller.Push(state, destination.Length);
        if (!state.TryCall(2, 1).IsOk || !state.IsTable(-1) || state.RawSequenceCount(-1) < destination.Length)
            return false;

        for (var i = 0; i < destination.Length; i++)
        {
            state.RawGetSequenceItem(-1, i);
            var ok = state.TryReadInteger(-1, out var value);
            state.Pop(1);
            if (!ok || value is < 0 or > 255) return false;

            destination[i] = (byte)value;
        }

        return true;
    }
}
```

Cheat Engine returns a byte table, and a Lua table counts from one. `RawGetSequenceItem(-1, i)` takes a zero based index
and does the `+ 1` for you, so the loop reads like ordinary C#. The reader copies into a buffer on the stack and never
allocates, and it refuses a table that is shorter than the request.

### 3. Use it from Lua

```lua
print(my_plugin_dbvm_status())                       -- driver no, dbvm no
print(my_plugin_dbvm_start())                        -- true when DBVM answers afterward
print(my_plugin_read_physical(0x1000, 8))            -- 16 hex digits, or nil when unavailable
local id = my_plugin_watch_writes(getAddress("game.exe+1A2B3C"), 4)
print(id, my_plugin_watch_hits())                    -- the watch id and the number of logged events
print(my_plugin_watch_stop())                        -- true
```

## Good to know

| Topic              | Detail                                                                                                                                                      |
|--------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Never loads        | The probe uses `dbk_initialized` and `dbvm_initialized`. Loading the driver is `dbk_initialize`, which you call only when the user asks                     |
| Watch options      | The third argument of `dbvm_watch_writes` is a bit field. This recipe passes 0, the plain watch. Bit 5 grows the log instead of discarding entries          |
| Reads and executes | `dbvm_watch_reads` and `dbvm_watch_executes` take the same arguments, so a binding is one more declaration                                                  |
| Log entries        | `dbvm_watch_retrievelog` returns one table per event with the context at that moment. Count them with `RawSequenceCount`, or read fields with `TryGetField` |
| System clock       | `dbvm_speedhack_setSpeed` changes how fast the timestamp counter runs for the whole system, not for one process. Bind it only when that is what you want    |
| Cleanup            | `OnDisable` stops the watch, so DBVM does not keep logging for a plugin that is gone                                                                        |

## Promise

- Every function that talks to Cheat Engine is a Try form or sits inside a wrapper that catches `LuaException`.
- A missing driver, a missing DBVM and a refused call all read as "not available", never as an exception.
- Copying the bytes out of the Lua table allocates nothing. Only the hex string of `my_plugin_read_physical` does.
- The Lua stack returns to its previous height on every path.
- The generated thunk catches every exception, and `LuaFrame` restores the stack on every path.

## Before you move on

- [ ] `my_plugin_dbvm_status()` answers on a machine that has no driver, without an error.
- [ ] `my_plugin_read_physical(address, 8)` returns sixteen hex digits, or `nil` when DBVM is not running.
- [ ] Unticking the plugin while a watch is active disables the watch.

---

<div align="center">

[Examples index](../../README.md) · [Recipes](../README.md)

</div>
