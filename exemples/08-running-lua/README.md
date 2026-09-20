<div align="center">

# 08 · Running Lua

**Drive the Lua state directly when a generated binding is not enough.**

**Level** `Intermediate` · **Time** `30 min` · **Needs** `Guide 03`

[Examples index](../README.md) · [Previous: Address list](../07-address-list/README.md) · [Next: The main thread](../09-main-thread/README.md)

</div>

---

|                            |                                                                                            |
|----------------------------|--------------------------------------------------------------------------------------------|
| **You build**              | A script runner, a hand written call, a kept function and a callback that carries state    |
| **You learn**              | `LuaFrame`, `TryExecute`, reading results and tables, marshallers, `LuaRef`, `LuaCallback` |
| **You need**               | The bindings from [03 · Calling Cheat Engine](../03-calling-cheat-engine/README.md)        |
| **Cheat Engine functions** | `getOpenedProcessID`, `getNameFromAddress`, `debug_continueFromBreakpoint`                 |

## Objective

Run Lua text, call a function whose name or result shape a generated binding cannot express, and hand a managed
function to Cheat Engine. Every step keeps the Lua stack balanced.

## Why it matters

Lua reports errors with `longjmp`, and the .NET runtime cannot unwind managed frames that way. One unguarded raise can
corrupt the process. The `CheatEngine.SDK.Lua` toolkit keeps every operation that can run Lua code under a protected
call, returns a `LuaStatus` instead of raising, and gives you `LuaFrame` so that no exit path leaves the stack
unbalanced. The native bridge also protects the host-object push path with `lua_pcallk`, so a failure while CE creates
userdata cannot jump across a managed frame. That protection does not make arbitrary raw Lua C calls safe.

## Which tool for which job

| Job                                                                                    | Tool                                  | Guide                                                                 |
|----------------------------------------------------------------------------------------|---------------------------------------|-----------------------------------------------------------------------|
| Export a C# method to Lua                                                              | `[LuaFunction]`                       | [02](../02-lua-functions/README.md)                                   |
| Call a Cheat Engine function with scalar or text arguments and results                 | `[LuaGlobal]`                         | [03](../03-calling-cheat-engine/README.md)                            |
| Run a script, read several results, read a table                                       | `LuaState` and `TryExecute`           | This guide                                                            |
| Call a function whose name is known only at run time, or that takes an enum or a table | `LuaState`, `TryGetGlobal`, `TryCall` | This guide                                                            |
| Work with a Cheat Engine object                                                        | `CEObject` and `Owned<T>`             | [05](../05-aob-scans/README.md) to [07](../07-address-list/README.md) |
| Pass a C# function that carries state to Cheat Engine                                  | `LuaCallback`                         | This guide                                                            |

## How it works

### 1. Get the state and guard the stack

```csharp
var L = LuaRuntime.AcquireState();
using LuaFrame frame = new(L);
```

`LuaRuntime.AcquireState()` asks the attached host for a state for this operation. `LuaFrame` remembers the stack height
and restores it when the block ends, on every exit: an early `return`, an exception or a failed call. The exact CE 7.7
state-per-thread behavior remains an opt-in live probe, so the safe SDK rule is narrower: use the acquired value on the
current operation only, and use the state passed into a Lua callback rather than acquiring another one.

| Rule                                                                       | Why                                                                                   |
|----------------------------------------------------------------------------|---------------------------------------------------------------------------------------|
| Never store a `LuaState`. Acquire it once per operation                    | Its host lifetime and state generation can change during disable or a supported reset |
| Open a `LuaFrame` before you push anything                                 | Your code cannot leave a value behind, whatever path it takes                         |
| A `Try*` member leaves either its results or exactly one error value       | `LuaError.FromStack` reads that value without running Lua code                        |
| Raw members (`PushInteger`, `TypeOf`, `TryReadInteger`) never run Lua code | Only the protected `Try*` members can, so only they can fail                          |
| Inside a callback, use the state Lua passed to you                         | `AcquireState` is for code that Lua did not call                                      |

### 2. Run a script

```csharp
using System.Diagnostics.CodeAnalysis;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace ScriptLab;

internal sealed record Snapshot(int ProcessId, string Status, long[] Numbers);

internal static class ScriptRunner
{
    public static bool TryRun(ReadOnlySpan<byte> script, [NotNullWhen(false)] out string? error)
    {
        var L = LuaRuntime.AcquireState();
        using LuaFrame frame = new(L);

        var status = L.TryExecute(script, 0, "=trainer.lua"u8);
        error = status.IsOk ? null : LuaError.FromStack(L, status).Message;
        return status.IsOk;
    }

    public static bool TryReadSnapshot([NotNullWhen(true)] out Snapshot? snapshot)
    {
        snapshot = null;
        var L = LuaRuntime.AcquireState();
        using LuaFrame frame = new(L);

        if (!L.TryExecute("return getOpenedProcessID(), 'ready', { 10, 20, 30 }"u8, 3).IsOk) return false;

        var first = L.AbsoluteIndex(-3);
        if (!Int32Marshaller.TryRead(L, first, out var processId)) return false;
        if (!L.TryReadString(first + 1, out var status)) return false;

        var table = first + 2;
        if (!L.IsTable(table)) return false;

        var numbers = new long[L.RawSequenceCount(table)];
        for (var i = 0; i < numbers.Length; i++)
        {
            L.RawGetSequenceItem(table, i);
            if (!L.TryReadInteger(-1, out numbers[i])) return false;
            L.Pop(1);
        }

        snapshot = new Snapshot(processId, status, numbers);
        return true;
    }

    public static bool IsReady()
    {
        var L = LuaRuntime.AcquireState();
        using LuaFrame frame = new(L);

        return L.TryExecute("return 'ready'"u8, 1).IsOk
               && L.TryReadUtf8(-1, out var text)
               && text.SequenceEqual("ready"u8);
    }
}
```

`TryExecute(source, resultCount, chunkName)` loads the text and runs it under a protected call. The second argument is
how many results you want on the stack. The optional chunk name starts with `=` to appear verbatim in messages, so a
script that fails reports `trainer.lua:1:` instead of a copy of its own source.

| Call                                               | Returns                                                            |
|----------------------------------------------------|--------------------------------------------------------------------|
| `TryRun("x = 1 + 1"u8, out error)`                 | `true`, `error` is `null`                                          |
| `TryRun("return +"u8, out error)`                  | `false`, `trainer.lua:1: unexpected symbol near '+'`               |
| `TryRun("error('boom')"u8, out error)`             | `false`, `trainer.lua:1: boom`                                     |
| `TryRun("local t = nil; return t.x"u8, out error)` | `false`, `trainer.lua:1: attempt to index a nil value (local 't')` |

### 3. Read several results, including a table

`TryReadSnapshot` runs a script that returns three values and turns them into one typed record, with each result read
as the type you expect.

```text
The stack after TryExecute(..., 3)

  index   relative   value
    3        -1      { 10, 20, 30 }
    2        -2      "ready"
    1        -3      4242            first = L.AbsoluteIndex(-3)
```

- A relative index such as `-3` moves every time you push, so convert it once with `AbsoluteIndex`.
- `RawSequenceCount`, `RawGetSequenceItem` and `TryGetSequenceItem` are zero based, like every public index in
  CheatEngine.SDK. The single `+ 1` lives in `IndexBase.ToLuaKey`.
- The raw form skips metamethods and suits a plain table. Use `TryGetSequenceItem` for a Cheat Engine object that
  behaves like an array, because it runs under a protected call.
- `TryReadUtf8` hands you the bytes Lua already holds, so `IsReady` compares text with no allocation. The span is valid
  only while the value stays on the stack. `TryReadString` copies into a new `string`.

### 4. Call a function by hand

```csharp
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace ScriptLab;

internal static class Calls
{
    public static int GetOpenedProcessId()
    {
        var L = LuaRuntime.AcquireState();
        using LuaFrame frame = new(L);

        L.TryGetGlobal("getOpenedProcessID"u8).ThrowIfFailed(L);
        if (!L.IsFunction(-1)) throw new InvalidOperationException("getOpenedProcessID is unavailable.");

        L.TryCall(0, 1).ThrowIfFailed(L);
        if (!Int32Marshaller.TryRead(L, -1, out var processId))
            throw new InvalidOperationException("getOpenedProcessID did not return an integer.");

        return processId;
    }

    public static string? DescribeAddress(nuint address)
    {
        var L = LuaRuntime.AcquireState();
        using LuaFrame frame = new(L);

        if (!L.TryGetGlobal("getNameFromAddress"u8).IsOk || !L.IsFunction(-1)) return null;

        AddressMarshaller.Push(L, address);
        BooleanMarshaller.Push(L, true);
        BooleanMarshaller.Push(L, true);
        if (!L.TryCall(3, 1).IsOk) return null;

        return StringMarshaller.TryRead(L, -1, out var name) ? name : null;
    }

    public static bool TryContinue(ContinueMethod method)
    {
        var L = LuaRuntime.AcquireState();
        using LuaFrame frame = new(L);

        if (!L.TryGetGlobal("debug_continueFromBreakpoint"u8).IsOk || !L.IsFunction(-1)) return false;

        EnumMarshaller<ContinueMethod>.Push(L, method);
        return L.TryCall(1, 0).IsOk;
    }
}
```

`GetOpenedProcessId` shows the pattern in full. It is always the same: push the function, push the arguments,
`TryCall(argumentCount, resultCount)`, read the results. This one has four exits, and each one is visible in the code:

| Exit                    | What happened                                              | What you see                                      |
|-------------------------|------------------------------------------------------------|---------------------------------------------------|
| `TryGetGlobal` fails    | Reading the global raised, for example through a metatable | `LuaException` with the Lua message               |
| `IsFunction` is `false` | The global is `nil` or another value                       | `InvalidOperationException` from your check       |
| `TryCall` fails         | The function raised                                        | `LuaException`, such as `?:2: no process is open` |
| The read fails          | The function returned another kind                         | `InvalidOperationException` from your check       |

> [!TIP]
> For `getOpenedProcessID`, a `[LuaGlobal]` binding is shorter and does the same job. Reach for the hand written form
> when the name is only known at run time, when an argument is an enum or a table, or when a result is a table.

The marshallers are the typed way to push and read one value. Each one is a `struct` with static `Push` and `TryRead`.

| Marshaller                             | C# type                                                           | Lua value                                                                      |
|----------------------------------------|-------------------------------------------------------------------|--------------------------------------------------------------------------------|
| `Int32Marshaller`, `Int64Marshaller`   | `int`, `long`                                                     | integer                                                                        |
| `SingleMarshaller`, `DoubleMarshaller` | `float`, `double`                                                 | number                                                                         |
| `BooleanMarshaller`                    | `bool`                                                            | boolean                                                                        |
| `StringMarshaller`                     | `string`                                                          | string, copied and decoded                                                     |
| `Utf8Marshaller`                       | `ReadOnlySpan<byte>`                                              | string, not copied                                                             |
| `AddressMarshaller`                    | `nuint`                                                           | integer, so an address above `long.MaxValue` travels as a negative Lua integer |
| `EnumMarshaller<TEnum>`                | `VariableType`, `ContinueMethod` and the other Cheat Engine enums | integer, the number Cheat Engine's `defines.lua` uses                          |

`TryContinue` shows why the enum marshaller exists: a generated binding cannot take an enum, and
`ContinueMethod.StepOver` reaches Lua as `2`.

### 5. Keep a function for later

```csharp
using System.Diagnostics.CodeAnalysis;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace ScriptLab;

internal sealed class KeptFunction : IDisposable
{
    private readonly LuaRef _reference;

    private KeptFunction(LuaRef reference) => _reference = reference;

    public bool IsCurrent => _reference.IsCurrent;

    public static bool TryCapture(ReadOnlySpan<byte> globalName, [NotNullWhen(true)] out KeptFunction? kept)
    {
        kept = null;
        var L = LuaRuntime.AcquireState();
        using LuaFrame frame = new(L);

        if (!L.TryGetGlobal(globalName).IsOk || !L.IsFunction(-1)) return false;

        kept = new KeptFunction(L.CreateRef());
        return true;
    }

    public bool TryCall(long argument, out long result)
    {
        result = 0;
        var L = LuaRuntime.AcquireState();
        using LuaFrame frame = new(L);

        if (!L.TryPushRef(_reference)) return false;

        L.PushInteger(argument);
        return L.TryCall(1, 1).IsOk && L.TryReadInteger(-1, out result);
    }

    public void Dispose() => _reference.Dispose();
}
```

`CreateRef` pops the value on top and stores it in the Lua registry. `TryPushRef` pushes it back. The reference keeps
the function it captured: if a script later assigns a new function to the same global name, the reference still calls
the old one.

A `LuaRef` carries a complete `LuaStateIdentity`: `(attachEpoch, stateGeneration)`. A disable followed by a new enable
changes the attachment epoch; an SDK-controlled state replacement changes the generation within the same attachment.
Either mismatch makes the reference stale: `IsCurrent` is `false`, `TryPushRef` returns `false`, and `Release` does not
touch a registry slot that may now name another value. Capture references in `OnEnable`, dispose them in `OnDisable`,
and re-resolve a stale reference instead of rebinding it by hand.

The state-generation preparation path is deliberately internal until the SDK owns the CE `resetLuaState` call from end
to end. Calling `resetLuaState` directly outside that path is unsupported: the SDK will not guess whether the old
registry, userdata, or callback closures are still valid. The intended order is to close admission, neutralize callbacks
and caches for the old identity, then replace/rebind the host state and advance `stateGeneration`.

### 6. Give Cheat Engine a function that carries state

```csharp
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CheatEngine.SDK.Annotations.Plugin;
using CheatEngine.SDK.Hosting.Plugin;
using CheatEngine.SDK.Lua.Callbacks;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace ScriptLab;

internal sealed class HitCounter
{
    public required string Name { get; init; }

    public int Hits { get; set; }
}

internal static unsafe class HitThunks
{
    public static LuaNativeFunction Report => new(&ReportThunk);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ReportThunk(nint handle)
    {
        try
        {
            LuaState L = new(handle);
            if (!LuaThunk.TryGetState(L, out HitCounter? counter))
                return LuaThunk.Fail(L, "the callback has been released"u8);

            counter.Hits++;
            StringMarshaller.Push(L, $"{counter.Name}: {counter.Hits} hit(s)");
            return 1;
        }
        catch (Exception exception)
        {
            return LuaThunk.Fail(new LuaState(handle), exception);
        }
    }
}

[CheatEnginePlugin("Script Lab")]
public sealed class ScriptLabPlugin : CheatEnginePlugin
{
    private LuaCallback<HitCounter>? _hits;

    protected override void OnEnable()
    {
        var L = LuaRuntime.AcquireState();
        LuaCallback.TryCreate(L, HitThunks.Report, new HitCounter { Name = "player" }, out _hits)
            .ThrowIfFailed(L);
        _hits!.TryRegister(L, "my_plugin_hits"u8).ThrowIfFailed(L);
    }

    protected override void OnDisable()
    {
        var L = LuaRuntime.AcquireState();
        using (LuaFrame frame = new(L))
        {
            L.PushNil();
            L.TrySetGlobal("my_plugin_hits"u8);
        }

        _hits?.Release(L);
        _hits = null;
    }
}
```

A `[LuaFunction]` is a static method and has no instance state. A `LuaCallback` pairs a thunk with one state object of
yours, so two callbacks that share a thunk keep separate counters. The state must be a class (`where TState : class`).

```lua
print(my_plugin_hits())   -- player: 1 hit(s)
print(my_plugin_hits())   -- player: 2 hit(s)
```

The thunk is the one piece you write by hand, and it follows four rules:

1. It is an `[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]` static method that takes the state as `nint`.
2. The whole body is one `try` whose `catch` reports the failure. `CESDK1004` warns when an exception can escape.
3. It reports failure with `LuaThunk.Fail(...)`, which Lua turns into an ordinary error at the call site.
4. It reads its state with `LuaThunk.TryGetState`, which returns `false` once the callback is released.

`TryRegister` assigns the function to a global. To hand it to a Cheat Engine function that wants a callback, such as the
`OnScanDone` property of a scan object, push it with `TryPush(L)` and pass it as the value or argument. A callback also
captures the complete Lua state identity. Disable and a supported state reset neutralize callback closures before their
managed state is released; an old closure is never deliberately rebound to a new registry universe.

> [!IMPORTANT]
> Release every callback in `OnDisable`, on the thread that created it. A script that kept the function and calls it
> after the release gets `the callback has been released` from the thunk, never a crash. A callback you forget is
> neutralized when the runtime detaches.

## What each layer reports

| Layer                                                                  | On failure                                                                 |
|------------------------------------------------------------------------|----------------------------------------------------------------------------|
| `Try*` members of `LuaState`                                           | A `LuaStatus` other than `LuaStatus.Ok`, with one error value on the stack |
| `LuaError.FromStack(L, status)`                                        | A `LuaError` with the `Status` and the `Message`                           |
| `status.ThrowIfFailed(L)` and `LuaException.ThrowFromStack(L, status)` | A `LuaException` whose `Status` and `Message` come from that error         |
| Marshaller `TryRead` and `TryReadInteger`                              | `false`, and the stack is unchanged                                        |
| A thunk                                                                | `LuaThunk.Fail(...)`, and Lua raises the message at the call site          |

`LuaStatus` names the codes `Ok`, `RuntimeError`, `SyntaxError`, `MemoryError`, `MessageHandlerError`,
`GcMetamethodError`, `FileError` and `Yield`. Compare it with `status.IsOk` in normal code.

## Promise

- Protected members send every raise other than memory exhaustion through `lua_pcallk`, so a raise never unwinds a
  managed frame.
- The stack stays balanced: a `Try*` member leaves its results or one error value, and `LuaFrame` restores the height on
  every exit.
- Scalar pushes and reads, protected calls and callbacks allocate nothing once warm.
- A `LuaRef` from an earlier attachment or state generation is never pushed.
- `Release`, supported reset preparation, and `Detach` neutralize a callback before its managed state is freed.

## Before you move on

- [ ] Every method that acquires a state opens a `LuaFrame` on the next line.
- [ ] Every `TryExecute` and `TryCall` result is checked, and the message is read with `LuaError.FromStack`.
- [ ] Each `LuaRef` and each `LuaCallback` you create in `OnEnable` is released in `OnDisable`, and none is retained
  across a changed `(attachEpoch, stateGeneration)` identity.

---

<div align="center">

[Examples index](../README.md) · **Next:** [09 · The main thread](../09-main-thread/README.md)

</div>
