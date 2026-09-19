# CESDK.Lua

`CESDK.Lua` is the managed Lua layer of CESDK: a state view, protected operations, marshallers, registry references and
callbacks over [`CESDK.Lua.Interop`](../CESDK.Lua.Interop/README.md).

## Objective

Give `CESDK.Engine`, `CESDK.Hosting` and generated code one way to work with a Lua state. The layer knows Lua, not Cheat
Engine: it references only `CESDK.Lua.Interop` and `CESDK.Annotations`. It ships in the `CESDK` package under
`lib/net10.0`.

## Why it exists

Cheat Engine exposes its functionality as Lua functions and objects, so CESDK reaches it through a Lua state. Lua raises
errors with `longjmp`, and the .NET runtime cannot unwind managed frames that way, so one unguarded raise can corrupt
the process. This layer keeps every operation that can run Lua code under `lua_pcallk`, keeps the Lua stack balanced,
and keeps the hot paths free of allocations.

## How it works

| Namespace                    | Types                                                                                                                                                                             | Role                                                     |
|------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|----------------------------------------------------------|
| `CESDK.Lua.State`            | `LuaState`, `LuaFrame`, `LuaType`                                                                                                                                                 | Borrowed view of a Lua state, stack guard, type tags     |
| `CESDK.Lua.Runtime`          | `LuaRuntime`, `LuaHostBinding`                                                                                                                                                    | The host binding: attach, detach, acquire a state, epoch |
| `CESDK.Lua.Calls`            | `LuaStatus`, `LuaError`, `LuaException`, `LuaComparison`                                                                                                                          | Results of protected operations, opt-in exceptions       |
| `CESDK.Lua.Marshalling`      | `ILuaMarshaller<T>`, `Int32Marshaller`, `Int64Marshaller`, `SingleMarshaller`, `DoubleMarshaller`, `BooleanMarshaller`, `AddressMarshaller`, `Utf8Marshaller`, `StringMarshaller` | Push and read one managed type each                      |
| `CESDK.Lua.References`       | `LuaRef`                                                                                                                                                                          | Registry reference stamped with an epoch                 |
| `CESDK.Lua.Callbacks`        | `LuaNativeFunction`, `LuaCallback`, `LuaCallback<TState>`, `LuaThunk`                                                                                                             | Managed functions that Lua can call                      |
| `CESDK.Lua.CompilerServices` | `LuaGlobalFunctions`, `LuaCallSupport`                                                                                                                                            | Called by generated code, hidden from IntelliSense       |

`LuaState` is a pointer-sized `readonly struct` over a borrowed `lua_State*`. Raw members make one or two C calls and
never run Lua code. Protected members (`TryCall`, `TryLoad`, `TryExecute`, `TryGetGlobal`, `TryGetField`, `TryLength`,
`TryNext` and the setters) return a `LuaStatus`, and no unprotected form exists. Raw members that allocate inside Lua,
such as string pushes, can still raise on memory exhaustion or through a failing `__gc` finalizer. Their documentation
says so. `LuaState` is one partial struct in `CESDK.Lua.State`, split by concern over the files of the `State` folder.

Protected accessors and comparisons run a small Lua helper under `lua_pcallk`, installed once per state. A Lua function
has no managed frame, so a raise unwinds only Lua frames. On failure every protected member leaves exactly one error
value on the stack, and `LuaError.FromStack` reads it without running Lua code. A relative index is taken before
anything is pushed, and a setter pops its operands even when it fails. The helpers capture `error`, `tostring` and
`next` from Lua's base library, which must be open.

A managed callback is a static `[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]` method that takes the state
as `nint` and catches every exception. It reports failure with `LuaThunk.Fail`, which pushes a sentinel and a message. A
Lua wrapper turns that into `error(message, 2)`, where unwinding is safe. `LuaCallback.TryCreate` attaches a state
object that the thunk reads with `LuaThunk.TryGetState`. Lookup acquires a strong managed reference under the same gate
as release. SDK references use a private registry table and never participate in the host registry free list. Any Lua
operation that may allocate is called through `cesdk-lua-bridge.dll`, so a Lua `longjmp` cannot cross a managed frame.

`LuaRuntime.Attach` advances an epoch. A `LuaRef` from an earlier epoch is stale: it is never pushed, and its slot is
forgotten. `LuaRuntime.Detach` neutralizes every live callback while the state is still reachable. Nothing has a
finalizer, because a Lua state belongs to one thread.

Never store a `LuaState`. Call `LuaRuntime.AcquireState` once per operation, and inside a callback use the state Lua
passed. Text is UTF-8: `"..."u8` literals are the primary form, and `ReadOnlySpan<char>` overloads and
`StringMarshaller` transcode. `AddressMarshaller` accepts numbers only, and an address above `long.MaxValue` travels as
a negative Lua integer.

### The generated call shape

The `LuaBindings` generator emits this shape for
`[LuaGlobal("readInteger")] public static partial bool TryReadInt32(nuint address, bool signed, out int value);`, with
the public helper passing `signed: true`. Generated code uses other local names and `global::` qualification.
Hand-written code can guard the stack with
`using LuaFrame frame = new(L);` instead of the `top` and `SetTop` pair.

```csharp
using CESDK.Lua.CompilerServices;
using CESDK.Lua.Marshalling;
using CESDK.Lua.References;
using CESDK.Lua.Runtime;

static class MemoryReads
{
    private static readonly LuaRef s_readInteger = new();

    public static bool TryReadInt32(nuint address, out int value)
    {
        var L = LuaRuntime.AcquireState();
        var top = L.Top;
        if (!LuaGlobalFunctions.TryPush(L, s_readInteger, "readInteger"u8))
            return LuaCallSupport.Fail(L, top, out value);
        AddressMarshaller.Push(L, address);
        BooleanMarshaller.Push(L, true); // CE readInteger defaults to unsigned.
        if (!L.TryCall(2, 1).IsOk) return LuaCallSupport.Fail(L, top, out value);
        var ok = Int32Marshaller.TryRead(L, -1, out value);
        L.SetTop(top);
        return ok;
    }
}
```

Generated bodies restore the stack in `finally`, including a managed exception from a marshaller. A body also handles:
the global is unresolved, the call raised, or the result is `nil` or of the
wrong type. A `Try*` form returns `false` through `LuaCallSupport.Fail`. A throwing form calls `ThrowUnresolvedGlobal`,
`Throw` or `ThrowUnexpectedResult`, which restore the stack and throw `LuaException`.

### String results

A generated body pops its results before it returns, so a `ReadOnlySpan<byte>` read from a result would dangle: spans
are argument-only. A string result is copied into a caller `Span<byte>` with `TryCopyUtf8`, or decoded into a `string`
with `StringMarshaller`, which allocates.

## Promise

- Protected members send every raise other than memory exhaustion through `lua_pcallk` (`ProtectedOperationTests`). An
  exception in a thunk becomes a Lua error (`A_managed_exception_inside_a_thunk_never_escapes_and_becomes_a_lua_error`).
- The stack stays balanced. A `Try*` member leaves its results or one error value, and `LuaFrame` restores the top on
  early return, exception and failed call (`LuaFrameTests`).
- Hot paths allocate nothing: scalar pushes and reads, protected calls, callbacks and the generated call shape
  (`ZeroAllocationTests`, `ReadIntegerBindingTests`, `StringBindingTests`).
- Stale references are detected. A `LuaRef` from an earlier epoch is never pushed
  (`References_are_invalidated_by_detach_and_reattach`).
- Callbacks are released deterministically. `Release` and `Detach` neutralize the closure before its state is freed
  (`LuaCallbackTests`).
- Scripts cannot break the helpers, even by redefining `error` and `tostring`
  (`Helpers_survive_a_script_that_redefines_error_and_tostring`).
- A detached runtime fails cleanly: `AcquireState` throws instead of touching a stale state
  (`AcquireState_throws_while_detached`).

## Run the tests

```powershell
dotnet test --project tests/CESDK.Lua.Tests
dotnet test --project tests/CESDK.Lua.Tests --filter-trait "Category=NativeLua"
```

Tests tagged `Category=NativeLua` run against the Lua DLL of Cheat Engine 7.7 kept in
[`native/cheat-engine`](../../native/cheat-engine/README.md), so nothing has to be installed. See
[`tests/CESDK.Lua.Tests`](../../tests/CESDK.Lua.Tests/README.md).
