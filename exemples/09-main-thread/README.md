<div align="center">

# 09 · The main thread

**Keep every call into Cheat Engine on the thread it belongs to.**

**Level** `Intermediate` · **Time** `25 min` · **Needs** `Guide 03`

[Examples index](../README.md) · [Previous: Running Lua](../08-running-lua/README.md) · [Next: Logging and errors](../10-logging-and-errors/README.md)

</div>

---

|                            |                                                                                                      |
|----------------------------|------------------------------------------------------------------------------------------------------|
| **You build**              | A value monitor that samples on a worker thread, and a long task that keeps the window responsive    |
| **You learn**              | `MainThread.Invoke`, `IsMainThread`, `ProcessMessages`, `CheckSynchronize`, `PluginContext`          |
| **You need**               | The bindings from [03 · Calling Cheat Engine](../03-calling-cheat-engine/README.md)                  |
| **Cheat Engine functions** | `getAddressSafe`, `readInteger`, `print`, and `synchronize`, which `MainThread.Invoke` calls for you |

## Objective

Run background work without breaking Cheat Engine. A worker thread samples a value, reports it through Cheat Engine, and
stops cleanly when the plugin is disabled.

## Why it matters

Cheat Engine's Lua state, its engine objects, its scanners and its windows belong to one thread: the main, or GUI,
thread. A call from any other thread can corrupt them. The SDK gives you one door to that thread, `MainThread`, so that
your worker threads never touch Cheat Engine directly.

## Where your code runs

| Code                                                                        | Thread                |
|-----------------------------------------------------------------------------|-----------------------|
| `OnEnable` and `OnDisable`                                                  | The main thread       |
| A `[LuaFunction]` called from the Lua Engine window or a cheat table script | The main thread       |
| A thread you start: `Thread`, `Task.Run`, a timer callback                  | Never the main thread |

```mermaid
sequenceDiagram
    autonumber
    participant W as Worker thread
    participant I as MainThread.Invoke
    participant M as Cheat Engine main thread
    W->>I: Invoke(work, state)
    I->>I: On the main thread? No
    I->>M: Hand the work to the synchronize global
    M->>M: Run the work
    M-->>I: Work finished
    I-->>W: The result, or the work's exception rethrown here
    Note over W,M: On the main thread, Invoke runs the work inline
```

`MainThread.Invoke` runs your work inline when you are already on the main thread. From another thread it goes through
Cheat Engine's Lua `synchronize` global, waits for the work to finish, and rethrows the work's exception on the caller
with its original stack trace.

## How it works

### 1. Bind what the monitor needs

```csharp
using CESDK.Annotations.Lua;

namespace ValueMonitor;

internal static partial class Ce
{
    [LuaGlobal("getAddressSafe")]
    public static partial bool TryGetAddress(string name, out nuint address);

    [LuaGlobal("print")]
    public static partial void Print(string text);
}
```

### 2. Sample on a worker, report through the main thread

```csharp
using CESDK.Annotations.Lua;
using CESDK.Annotations.Plugin;
using CESDK.Engine.Generated;
using CESDK.Engine.Values;
using CESDK.Hosting.Diagnostics;
using CESDK.Hosting.Plugin;
using CESDK.Hosting.Threading;
using CESDK.Lua.Runtime;

namespace ValueMonitor;

[CheatEnginePlugin("Value Monitor")]
public sealed class ValueMonitorPlugin : CheatEnginePlugin
{
    private CancellationTokenSource? _stop;
    private Thread? _worker;

    protected override void OnEnable()
    {
        var L = LuaRuntime.AcquireState();
        Functions.RegisterLuaFunctions(L).ThrowIfFailed(L);
        Scanner.RegisterLuaFunctions(L).ThrowIfFailed(L);

        _stop = new CancellationTokenSource();
        var token = _stop.Token;
        _worker = new Thread(() => Sample(token)) { IsBackground = true, Name = "Value Monitor" };
        _worker.Start();
    }

    protected override void OnDisable()
    {
        _stop?.Cancel();

        // The worker may be inside MainThread.Invoke, waiting for this very thread. Keep pumping until it ends.
        while (_worker is { } worker && !worker.Join(TimeSpan.Zero)) MainThread.CheckSynchronize(10);

        _stop?.Dispose();
        _stop = null;
        _worker = null;

        var L = LuaRuntime.AcquireState();
        Functions.UnregisterLuaFunctions(L);
        Scanner.UnregisterLuaFunctions(L);
    }

    private static void Sample(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var address = Functions.Watched;
                if (address != 0 && MainThread.Invoke(static a => ReadInt32(a), address) is { } value and < 25)
                    MainThread.Invoke(static v => Ce.Print($"Value Monitor: {v} is below the alarm level."), value);

                token.WaitHandle.WaitOne(TimeSpan.FromSeconds(1));
            }
        }
        catch (InvalidOperationException)
        {
            // The plugin was disabled while a call was in flight: nothing left to monitor.
        }
        catch (Exception exception)
        {
            HostLog.Write(HostLogLevel.Error, "The value monitor stopped.", exception);
        }
    }

    private static int? ReadInt32(nuint address) =>
        MemoryScalars.TryReadInt32(Address.FromUInt64(address), out var value) ? value : null;
}

internal static partial class Functions
{
    private static long s_watched;

    public static nuint Watched => (nuint)Interlocked.Read(ref s_watched);

    [LuaFunction("monitor_watch")]
    public static bool Watch(string symbol)
    {
        if (!Ce.TryGetAddress(symbol, out var address)) return false;

        Interlocked.Exchange(ref s_watched, (long)address);
        return true;
    }

    [LuaFunction("monitor_stop")]
    public static void Stop() => Interlocked.Exchange(ref s_watched, 0);
}
```

Try it from the Lua Engine window:

```lua
print(monitor_watch("game.exe+1F4A0"))   -- true, and the worker starts sampling that address
monitor_stop()                            -- the worker keeps running and reads nothing
```

Three details make this safe.

- **`monitor_watch` runs on the main thread**, so it may call `getAddressSafe` directly. It hands the worker a plain
  number through `Interlocked`, never a Cheat Engine object.
- **Every read goes through `Invoke`.** `MemoryScalars.TryReadInt32` runs Lua, so the worker calls it as work for the
  main thread and gets the `int?` back as the result.
- **The worker catches everything.** An exception that escapes a thread you started ends the process. The
  `InvalidOperationException` branch is the normal ending: `Invoke` throws it once the plugin is disabled.

> [!WARNING]
> Never block the main thread on a worker that calls `Invoke`. The worker waits for the main thread, the main thread
> waits for the worker, and both wait forever. When the main thread must wait, as in `OnDisable` above, it pumps
> `MainThread.CheckSynchronize(timeout)` in a loop so that queued calls still run.

### 3. Keep the window alive during a long task

```csharp
using CESDK.Annotations.Lua;
using CESDK.Engine.Generated;
using CESDK.Engine.Values;
using CESDK.Hosting.Bootstrap;
using CESDK.Hosting.Threading;

namespace ValueMonitor;

internal static partial class Scanner
{
    [LuaFunction("my_plugin_count_matches")]
    public static long CountMatches(long start, long length, int target)
    {
        var context = PluginHost.Context;
        long matches = 0;

        for (long offset = 0; offset < length; offset += 4)
        {
            if (MemoryScalars.TryReadInt32(Address.FromInt64(start + offset), out var value) && value == target)
                matches++;

            if ((offset & 0xFFF) != 0) continue;

            MainThread.ProcessMessages();
            if (context is null || !context.IsCurrent) break;
        }

        return matches;
    }
}
```

This function counts the 32-bit values equal to `target` in a range. It runs on the main thread, so a long loop would
freeze Cheat Engine's window. Every 1,024 reads it calls `MainThread.ProcessMessages()`, which lets Cheat Engine handle
its pending window messages.

`ProcessMessages` is re-entrant: message handlers run inside the call. The user can therefore untick the plugin in the
middle of the loop. The loop keeps the `PluginContext` it started with and stops as soon as `IsCurrent` turns `false`,
which is how it notices a disable or a re-enable.

### 4. A helper that reads like a block

```csharp
using CESDK.Hosting.Threading;

namespace ValueMonitor;

internal static class Sync
{
    public static void Run(Action work) => MainThread.Invoke<Action>(static run => run(), work);

    public static T Run<T>(Func<T> work) => MainThread.Invoke<Func<T>, T>(static run => run(), work);
}
```

`MainThread.Invoke` takes a state argument so that the lambda can be `static` and capture nothing. When a closure is
simpler than the state, this helper passes the delegate itself as the state:

```csharp
Sync.Run(() => Ce.Print("hello from any thread"));
var moduleBase = Sync.Run(() => Ce.TryGetAddress("game.exe", out var address) ? address : 0);
```

## The rules

| Rule                                                               | Why                                                            | Where you saw it                                |
|--------------------------------------------------------------------|----------------------------------------------------------------|-------------------------------------------------|
| Cheat Engine state, objects and scanners belong to the main thread | None of them is thread safe                                    | `Invoke` around `ReadInt32`                     |
| Reach the main thread only through `MainThread.Invoke`             | It is the one supported hop, inline when you are already there | Steps 2 and 4                                   |
| Keep a check and the action it guards in the same `Invoke`         | Another thread can change Cheat Engine between two hops        | One `ReadInt32` call, one hop                   |
| Catch every exception on a thread you start                        | An escaped exception ends the process                          | `Sample`                                        |
| Pump when the main thread waits or works long                      | It keeps queued calls and window messages moving               | `OnDisable` and `CountMatches`                  |
| Stop your threads in `OnDisable`                                   | After it returns, `Invoke` and the Lua state are gone          | `_stop.Cancel()` and `Join`                     |
| Dispose an `Owned<T>` on the main thread                           | `Dispose` calls the object's `destroy()`                       | See [05 · AOB scans](../05-aob-scans/README.md) |

## Facts about the thread and the plugin

`PluginContext` holds the immutable facts of one enable and is safe to read from any thread. Get it from
`CheatEnginePlugin.Context` between the start of `OnEnable` and the end of `OnDisable`, or from `PluginHost.Context`,
which returns `null` while the plugin is disabled.

| Member                                      | Meaning                                                    |
|---------------------------------------------|------------------------------------------------------------|
| `PluginId`                                  | The id Cheat Engine assigned in the enable callback        |
| `Epoch`                                     | The runtime epoch of this enable. Every enable advances it |
| `MainThreadId`                              | The managed thread id of the main thread                   |
| `IsMainThread`                              | Whether the calling thread is the main thread              |
| `IsCurrent`                                 | Whether this is still the context of the current enable    |
| `HasProcessMessages`, `HasCheckSynchronize` | Whether the host supplied the two message loop slots       |

`MainThread.IsMainThread` reads the same fact and is `false` while the plugin is disabled. Keep no `PluginContext` and
no Lua reference across a disable: the next enable publishes a new context and a new epoch, and `IsCurrent` tells the
old one from the live one.

Four attributes in `CESDK.Annotations` document these rules on the SDK's own members: `[RunsOnMainThread]` for a body
that runs on the main thread and restricts no caller, `[MainThreadOnly]` for a member that callers must reach from the
main thread, `[RequiresPluginEnabled]` for an API that works only between enable and disable, and `[CEOwned]` for an
object you must not dispose. They are metadata only, so the compiler does not enforce them. Mark your own APIs the same
way.

## What you see when you call too early

`MainThread.Invoke`, `ProcessMessages` and `CheckSynchronize` need an enabled plugin. Calling `my_plugin_count_matches`
while the plugin is disabled raises an ordinary Lua error:

```text
System.InvalidOperationException: The plugin is not enabled: Cheat Engine has not called EnablePlugin, or has called DisablePlugin since.
```

`ProcessMessages` and `CheckSynchronize` also refuse a thread that is not the main thread, with a message that tells you
to use `MainThread.Invoke` to get there. They do not pump a queue that is not theirs. They throw as well when the host
left the function out of its record.

## Promise

- `Invoke` runs inline on the main thread. From another thread it waits for the work, then returns its result or
  rethrows its exception on the caller.
- `ProcessMessages` and `CheckSynchronize` run only on the main thread and throw `InvalidOperationException` elsewhere.
- `MainThread.IsMainThread` is `false`, and `Invoke` throws, whenever the plugin is disabled.
- No exception from your plugin reaches Cheat Engine. `OnEnable` failures are logged and reported as failed calls;
  `OnDisable` failures are logged, then cleanup completes and Cheat Engine records the disabled state.

## Before you move on

- [ ] Every call into Cheat Engine from a thread you started goes through `MainThread.Invoke`.
- [ ] Every thread you start ends before `OnDisable` returns, and its loop catches every exception.
- [ ] A main thread that waits for a worker pumps `CheckSynchronize`.

---

<div align="center">

[Examples index](../README.md) · **Next:** [10 · Logging and errors](../10-logging-and-errors/README.md)

</div>
