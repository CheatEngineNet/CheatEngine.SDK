<div align="center">

# Recipe · Processes

**Attach to the game, launch it when it is not running, and take a consistent snapshot while it is paused.**

**Level** `Beginner` · **Time** `15 min` · **Needs** `Guide 03`

[Examples index](../../README.md) · [Recipes](../README.md)

</div>

---

|                            |                                                                                                                                                                                              |
|----------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **You build**              | `my_plugin_attach`, `my_plugin_attach_foreground` and `my_plugin_report`, plus a pause scope                                                                                                 |
| **You learn**              | Binding the process functions, reading the module and thread tables, and pausing safely                                                                                                      |
| **You need**               | The `[LuaGlobal]` basics from [03 · Calling Cheat Engine](../../03-calling-cheat-engine/README.md)                                                                                           |
| **Cheat Engine functions** | `openProcess`, `getOpenedProcessID`, `getProcessIDFromProcessName`, `createProcess`, `pause`, `unpause`, `isPaused`, `getForegroundProcess`, `targetIs64Bit`, `getThreadlist`, `enumModules` |

## Objective

Point Cheat Engine at a target process from C#, whether it is already running or must be started, and inspect it: its
id, its bitness, its threads and its modules.

## Why it matters

Every other recipe starts with an opened process. Attaching is also where trainers go wrong: they assume the game is
running, they ignore a failed open, and they leave the game paused when an error interrupts a read. Each of those is a
few lines here.

## How it works

### 1. Bind the process functions

```csharp
using CESDK.Annotations.Lua;

namespace ProcessRecipe;

internal static partial class ProcessCalls
{
    [LuaGlobal("getOpenedProcessID")]
    public static partial int GetOpenedProcessId();

    [LuaGlobal("getProcessIDFromProcessName")]
    public static partial bool TryGetProcessId(string processName, out int processId);

    [LuaGlobal("getForegroundProcess")]
    public static partial int GetForegroundProcessId();

    [LuaGlobal("openProcess")]
    public static partial void OpenProcess(int processId);

    [LuaGlobal("openProcess")]
    public static partial void OpenProcess(string processName);

    [LuaGlobal("createProcess")]
    public static partial void CreateProcess(string path);

    [LuaGlobal("createProcess")]
    public static partial void CreateProcess(string path, string parameters, bool debug, bool breakOnEntryPoint);

    [LuaGlobal("pause")]
    public static partial void Pause();

    [LuaGlobal("unpause")]
    public static partial void Unpause();

    [LuaGlobal("isPaused")]
    public static partial bool IsPaused();

    [LuaGlobal("targetIs64Bit")]
    public static partial bool TargetIs64Bit();
}
```

`openProcess` takes a process id or a process name, so it becomes two overloads that share one binding. Cheat Engine's
own `getOpenedProcessID` answers 0 while nothing is open, which is how the code below detects a failed attach.

### 2. Attach, report and pause

```csharp
using System.Text;
using CESDK.Annotations.Lua;
using CESDK.Engine.Values;
using CESDK.Lua.Runtime;
using CESDK.Lua.State;

namespace ProcessRecipe;

internal readonly record struct ModuleInfo(string Name, Address Base, long Size);

internal readonly struct PausedScope : IDisposable
{
    private readonly bool _wasPaused;

    public PausedScope()
    {
        _wasPaused = ProcessCalls.IsPaused();
        if (!_wasPaused) ProcessCalls.Pause();
    }

    public void Dispose()
    {
        if (!_wasPaused) ProcessCalls.Unpause();
    }
}

internal static partial class Attach
{
    [LuaFunction("my_plugin_attach")]
    public static long AttachOrLaunch(string target)
    {
        var name = Path.GetFileName(target);
        if (TryFindRunning(name, out var running))
        {
            ProcessCalls.OpenProcess(running);
            return ProcessCalls.GetOpenedProcessId();
        }

        if (name == target) return 0;

        ProcessCalls.CreateProcess(target);
        if (ProcessCalls.GetOpenedProcessId() == 0 && TryFindRunning(name, out var started))
            ProcessCalls.OpenProcess(started);
        return ProcessCalls.GetOpenedProcessId();
    }

    [LuaFunction("my_plugin_attach_foreground")]
    public static long AttachForeground()
    {
        ProcessCalls.OpenProcess(ProcessCalls.GetForegroundProcessId());
        return ProcessCalls.GetOpenedProcessId();
    }

    [LuaFunction("my_plugin_report")]
    public static string Report()
    {
        var processId = ProcessCalls.GetOpenedProcessId();
        if (processId == 0) return "No process is open.";

        var threads = ListThreads();
        var modules = ListModules();
        StringBuilder text = new();
        text.Append($"pid {processId}, {(ProcessCalls.TargetIs64Bit() ? "x64" : "x86")}, ")
            .Append($"{threads.Count} threads, {modules.Count} modules");
        foreach (var module in modules.Take(3)) text.Append($"\n  {module.Name} at {module.Base} ({module.Size} bytes)");
        return text.ToString();
    }

    private static bool TryFindRunning(string processName, out int processId) =>
        ProcessCalls.TryGetProcessId(processName, out processId) && processId != 0;

    public static List<long> ListThreads()
    {
        List<long> ids = [];
        var L = LuaRuntime.AcquireState();
        using LuaFrame frame = new(L);
        if (!L.TryGetGlobal("getThreadlist"u8).IsOk || !L.TryCall(0, 1).IsOk || !L.IsTable(-1)) return ids;

        var list = L.AbsoluteIndex(-1);
        var count = L.RawSequenceCount(list);
        for (var i = 0; i < count; i++)
        {
            L.RawGetSequenceItem(list, i);
            if (L.TryReadInteger(-1, out var id)) ids.Add(id);
            L.Pop(1);
        }

        return ids;
    }

    public static List<ModuleInfo> ListModules()
    {
        List<ModuleInfo> modules = [];
        var L = LuaRuntime.AcquireState();
        using LuaFrame frame = new(L);
        if (!L.TryGetGlobal("enumModules"u8).IsOk || !L.TryCall(0, 1).IsOk || !L.IsTable(-1)) return modules;

        var list = L.AbsoluteIndex(-1);
        var count = L.RawSequenceCount(list);
        for (var i = 0; i < count; i++)
        {
            L.RawGetSequenceItem(list, i);
            var entry = L.AbsoluteIndex(-1);
            if (L.IsTable(entry) && TryGetName(L, entry, out var name) && TryGetInteger(L, entry, "Address"u8, out var address))
            {
                TryGetInteger(L, entry, "Size"u8, out var size);
                modules.Add(new ModuleInfo(name, Address.FromInt64(address), size));
            }

            L.Pop(1);
        }

        return modules;
    }

    private static bool TryGetName(LuaState L, int table, out string name)
    {
        string? value = null;
        var ok = L.TryGetField(table, "Name"u8).IsOk && L.TryReadString(-1, out value);
        L.Pop(1);
        name = value ?? string.Empty;
        return ok;
    }

    private static bool TryGetInteger(LuaState L, int table, ReadOnlySpan<byte> key, out long value)
    {
        value = 0;
        var ok = L.TryGetField(table, key).IsOk && L.TryReadInteger(-1, out value);
        L.Pop(1);
        return ok;
    }
}
```

The two tables come back from Cheat Engine as Lua sequences, so the code reads them with the zero based helpers of
`CESDK.Engine.Values`. A protected accessor such as `TryGetField` always leaves exactly one value on the stack, the
field or the error, so each read pops once. `LuaFrame` restores the stack even on an early return.

### 3. Try it

```lua
print(my_plugin_attach("game.exe"))          -- 4242, attached to the running game
print(my_plugin_attach(gamePath))            -- gamePath holds the full path to game.exe: it starts the game when it is not running
print(my_plugin_attach_foreground())         -- attach to the window in front of you
print(my_plugin_report())
```

```text
pid 4242, x64, 3 threads, 2 modules
  game.exe at 0000000140000000 (2064384 bytes)
  engine.dll at 00007FF812340000 (5242880 bytes)
```

The ids, modules and sizes depend on your target. `my_plugin_attach` returns `0` when the process is not running and the
argument is a bare name.

## Pause without leaving the game frozen

`PausedScope` remembers whether the target was already paused, pauses it when it was not, and resumes it only in that
case. Put it in a `using` around a group of reads that must agree with each other:

```csharp
using CESDK.Engine.Generated;
using CESDK.Engine.Values;

namespace ProcessRecipe;

internal static class Snapshot
{
    public static (int Health, int Mana)? Take(Address health, Address mana)
    {
        using var paused = new PausedScope();
        return MemoryScalars.TryReadInt32(health, out var h) && MemoryScalars.TryReadInt32(mana, out var m) ? (h, m) : null;
    }
}
```

Both values come from the same instant, and the game resumes whether the reads succeed, fail or throw.

## Good to know

- **Check the id, not the call.** `openProcess` reports nothing. Read `getOpenedProcessID()` afterward: 0 means nothing
  is open.
- **Launching is a separate step.** `createProcess(path, parameters, debug, breakOnEntryPoint)` starts a program. The
  recipe reads the opened id afterward and opens the new process by name when Cheat Engine did not.
- **Do not pause across a slow operation.** Pause only around reads and writes. A paused game cannot answer, and a
  script that waits for it will wait forever.
- **`getThreadlist()` needs Cheat Engine 7.7.** It returns the thread ids as an indexed table, with the main thread
  usually first.
- **Module entries** carry `Name`, `Address`, `Size`, `Is64Bit` and `PathToFile`. Cheat Engine 7.6 and later fill
  `Size`.
- **A Lua `nil` is a normal answer.** `TryGetProcessId` returns `false` for an unknown name, so an absent game is an
  `if`, not an exception.

## Promise

- A Try form never throws for an unknown process name. It returns `false`.
- A throwing form reports a failed Cheat Engine call in a `LuaException` and leaves the Lua stack as it found it.
- `PausedScope` resumes the target on every exit path, and leaves it paused when it was paused before.
- Each Lua function you export catches its own exceptions, so a failed attach is a Lua error and never reaches Cheat
  Engine.

## Before you move on

- [ ] `my_plugin_attach("game.exe")` returns the process id, and `0` when the game is not running.
- [ ] `my_plugin_report()` lists threads and modules.
- [ ] A read that fails inside `using var paused = new PausedScope();` still resumes the game.

---

<div align="center">

[Examples index](../../README.md) · [Recipes](../README.md)

</div>
