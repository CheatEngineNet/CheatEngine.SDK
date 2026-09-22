# CheatEngine.SDK.Lua

`CheatEngine.SDK.Lua` is the managed Lua layer of CheatEngine.SDK: a state view, protected operations, marshallers,
registry references and callbacks over [`CheatEngine.SDK.Lua.Interop`](../CheatEngine.SDK.Lua.Interop/README.md).

## Objective

Give `CheatEngine.SDK.Engine`, `CheatEngine.SDK.Hosting` and generated code one way to work with a Lua state. The layer
knows Lua, not Cheat Engine: it references only `CheatEngine.SDK.Lua.Interop` and `CheatEngine.SDK.Annotations`. It
ships in the `CheatEngine.SDK` package under `lib/net10.0`.

## Why it exists

Cheat Engine exposes its functionality as Lua functions and objects, so CheatEngine.SDK reaches it through a Lua state.
Lua raises errors with `longjmp`, and the .NET runtime cannot unwind managed frames that way, so one unguarded raise can
corrupt the process. This layer keeps every operation that can run Lua code under `lua_pcallk`, keeps the Lua stack
balanced, and keeps the hot paths free of allocations.

## How it works

| Namespace                              | Types                                                                                                                                                                             | Role                                                                         |
|----------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|------------------------------------------------------------------------------|
| `CheatEngine.SDK.Lua.State`            | `LuaState`, `LuaFrame`, `LuaType`                                                                                                                                                 | Borrowed view of a Lua state, stack guard, type tags                         |
| `CheatEngine.SDK.Lua.Runtime`          | `LuaRuntime`, `LuaRuntimeOperation`, `LuaHostBinding`, `LuaStateIdentity`                                                                                                         | The host binding, lifecycle admission, attachment epoch and state generation |
| `CheatEngine.SDK.Lua.Calls`            | `LuaStatus`, `LuaError`, `LuaException`, `LuaComparison`                                                                                                                          | Results of protected operations, opt-in exceptions                           |
| `CheatEngine.SDK.Lua.Marshalling`      | `ILuaMarshaller<T>`, `Int32Marshaller`, `Int64Marshaller`, `SingleMarshaller`, `DoubleMarshaller`, `BooleanMarshaller`, `AddressMarshaller`, `Utf8Marshaller`, `StringMarshaller` | Push and read one managed type each, both by interface and static contract   |
| `CheatEngine.SDK.Lua.References`       | `LuaRef`                                                                                                                                                                          | Registry reference stamped with attachment epoch and state generation        |
| `CheatEngine.SDK.Lua.Callbacks`        | `LuaNativeFunction`, `LuaCallback`, `LuaCallback<TState>`, `LuaThunk`                                                                                                             | Managed functions that Lua can call                                          |
| `CheatEngine.SDK.Lua.CompilerServices` | `LuaGlobalFunctions`, `LuaCallSupport`                                                                                                                                            | Called by generated code, hidden from IntelliSense                           |
| `CheatEngine.SDK.Lua.Registration`     | `LuaRegistrationSet`, `LuaRegistrationLease`, `LuaRegistrationResult`                                                                                                             | Ownership-aware generated-global publication and cleanup outcomes            |

`LuaState` is a pointer-sized `readonly struct` over a borrowed `lua_State*`. Raw members make one or two C calls and
never run Lua code. Protected members (`TryCall`, `TryLoad`, `TryExecute`, `TryGetGlobal`, `TryGetField`, `TryLength`,
`TryNext` and the setters) return a `LuaStatus`, and no unprotected form exists. Raw members that allocate inside Lua,
such as string pushes, can still raise on memory exhaustion or through a failing `__gc` finalizer. Their documentation
says so. `LuaState` is one partial struct in `CheatEngine.SDK.Lua.State`, split by concern over the files of the `State`
folder.

`PushUncheckedFunction` is the one narrowly audited direct closure fast path. It first calls `lua_checkstack(L, 1)` and
throws `InvalidOperationException` without changing the stack when the reservation fails; only then does it call
`lua_pushcclosure(L, thunk, 0)`. In CE's pinned Lua 5.3 source, zero upvalues select the non-allocating light-C-function
branch. No other direct use of `lua_pushcclosure` is permitted, and no code may be inserted between that reservation and
the push.

Protected accessors and comparisons run a small Lua helper under `lua_pcallk`, installed once per state. A Lua function
has no managed frame, so a raise unwinds only Lua frames. On failure every protected member leaves exactly one error
value on the stack, and `LuaError.FromStack` reads it without running Lua code. A relative index is taken before
anything is pushed, and a setter pops its operands even when it fails. The helpers capture `error`, `tostring` and
`next` from Lua's base library, which must be open.

A managed callback is a static `[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]` method that takes the state
as `nint` and catches every exception. It reports failure with `LuaThunk.Fail`, which pushes a sentinel and a message. A
Lua wrapper turns that into `error(message, 2)`, where unwinding is safe. `LuaCallback.TryCreate` attaches a state
object that the thunk reads with `LuaThunk.TryGetState`. Lookup acquires a strong managed reference under the same gate
as release. Its SDK-owned dispatch closure holds a `LuaRuntimeOperation` for each stateful invocation: an already
admitted callback can finish during teardown, while a later callback returns a catchable `"the Lua runtime is stopping"`
error without entering plugin code. Active dispatched work prevents clean disable: a lifecycle transition from that
nesting is refused before taking the shutdown gate. SDK references use a private registry table and never participate in
the host registry free list. Any Lua operation that may allocate is called through `cheatengine-sdk-lua-bridge.dll`, so
a Lua `longjmp` cannot cross a managed frame.

`LuaRuntime` identifies a persistent Lua resource with `(attachEpoch, stateGeneration)`. `Attach` advances only the
attachment epoch. A supported reset begins with the internal host-owned `BeginStateReset` transition: it closes
admission, drains every `LuaRuntimeOperation`, neutralizes rooted callbacks while the old state is still reachable, then
advances only the state generation. Its stack-only transition remains open until the host has actually replaced the
state. `LuaRef`s and generated global caches become stale unless both components match. A stale slot is forgotten rather
than pushed or released against a replacement registry. `Detach` follows the same close-and-drain rule before it
neutralizes every live callback. A lifecycle transition cannot start from an admitted Lua operation or dispatched work
because shutdown would wait for that caller to return: the transition is refused before waiting for another transition's
lock. A failed `Detach` leaves the lifecycle in `Disabling` for diagnosis instead of reporting false completion. Nothing
has a finalizer, because a Lua state belongs to one thread.

`LuaRegistrationSet.Register` publishes a fixed, nonempty descriptor list only after it captures the current
`LuaStateIdentity` and preflights every effective global. The generated `TryRegisterLuaFunctions` method uses that
transaction and returns a `LuaRegistrationResult`; callers inspect it and own its non-null `Lease`. Its default policy
rejects collisions without publication;
`ReplaceExisting` is opt-in and remembers the prior value. Release compares the exact rooted installed closure with
`RawEquals` before changing a global, so it never clears a later replacement. It attempts independent cleanup entries
after a protected failure and reports all named outcomes. A lease from an old attachment or reset generation is stale
and performs no Lua or registry operation. This is intentionally not a promise to reverse arbitrary `__index` or
`__newindex` metamethod side effects.

The universe identity intentionally omits the state pointer. In the pinned Cheat Engine source, a worker obtains a
coroutine through lua_newthread and roots it in the main registry. That coroutine has a separate stack pointer, but it
shares the main virtual machine, heap and registry. A different worker pointer therefore does not establish an
independent Lua heap or a safe concurrent-execution policy.

Cheat Engine's `resetLuaState` must not be called outside the SDK-owned reset protocol. An external, unnotified reset is
unsupported: the SDK cannot safely infer whether the old registry, callbacks, CE userdata or thread-local state still
exist, so it deliberately does not attempt best-effort cleanup against a potentially replacement state. The
deterministic
fixture tests below prove the managed invalidation ordering only; CE 7.7 reset/thread/userdata behavior remains subject
to the opt-in live probe.

SDK-012 exercises this ordering with a deterministic native fixture: a worker first has no provider state, then receives
a rooted coroutine with a pointer distinct from the main state; it can read the shared global and private-registry
reference, and a reset invalidates that reference and its callback before later worker execution. This fixture proves
the SDK lifecycle contract, not live Cheat Engine multi-threaded execution.

Never store a `LuaState`. Start normal work with `using var operation = LuaRuntime.AcquireOperation();` and use
`operation.State` until that synchronous scope ends. This admission is what lets attach, detach and reset reject new
work and drain existing work before the host changes state. `AcquireState` is an advanced legacy escape hatch only for
code externally serialized with the host lifecycle. Inside a callback use the state Lua passed. Text is UTF-8:
`"..."u8` literals are the primary form, and `ReadOnlySpan<char>` overloads and
`StringMarshaller` transcode. `AddressMarshaller` accepts numbers only, and an address above `long.MaxValue` travels as
a negative Lua integer.

### The generated call shape

The `LuaBindings` generator emits this shape for
`[LuaGlobal("readInteger")] public static partial bool TryReadInt32(nuint address, bool signed, out int value);`, with
the public helper passing `signed: true`. Generated code uses other local names and `global::` qualification.
Hand-written code can guard the stack with
`using LuaFrame frame = new(L);` instead of the `top` and `SetTop` pair.

```csharp
using CheatEngine.SDK.Lua.CompilerServices;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;

static class MemoryReads
{
    private static readonly LuaRef s_readInteger = new();

    public static bool TryReadInt32(nuint address, out int value)
    {
        using var operation = LuaRuntime.AcquireOperation();
        var L = operation.State;
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
the global is unresolved, protected global lookup itself failed, the call raised, or the result is `nil` or of the wrong
type. A `Try*` form returns `false` through `LuaCallSupport.Fail`. A throwing form calls `ThrowUnresolvedGlobal`,
`Throw`
or `ThrowUnexpectedResult`, which restore the stack and throw `LuaException`.

### String results

A generated body pops its results before it returns, so a `ReadOnlySpan<byte>` read from a result would dangle: spans
are argument-only. A string result is copied into a caller `Span<byte>` with `TryCopyUtf8`, or decoded into a `string`
with `StringMarshaller`, which allocates.

### Custom marshalling

Generated bindings select a concrete marshaller for every value by type: scalar types use an SDK marshaller directly,
and an explicit `[LuaMarshaller(typeof(TMarshaller))]` on a parameter or return value overrides that for one binding.
The attribute names a type implementing `ILuaMarshaller<T>` for the annotated value type; the generator validates that
the named type implements the interface and directly emits static calls to the marshaller's `Push` and `TryRead`
members.

Built-in scalar marshallers push and read with no allocation: the pattern of `TMarshaller.Push(L, value)` and
`TMarshaller.TryRead(L, index, out value)` is as efficient as an SDK marshaller. Custom marshallers for structs,
wrappers or sum types keep the same binding protocol. No registry, runtime reflection, delegate or instance marshaller
is used by generated code.

Use `[LuaMarshaller]` for a type specific to one wrapper or for a type that does not justify an SDK-wide default. The
SDK scalar marshallers remain static members of their named types (`Int32Marshaller`, `AddressMarshaller`) so generic
table-reading or callback code can use them with `where TMarshaller : ILuaMarshaller<T>`. An explicit selection on a
binding parameter or result is evaluated only once per method symbol at generation time.

## Promise

- Protected members send every raise other than memory exhaustion through `lua_pcallk` (`ProtectedOperationTests`). An
  exception in a thunk becomes a Lua error (`A_managed_exception_inside_a_thunk_never_escapes_and_becomes_a_lua_error`).
- The stack stays balanced. A `Try*` member leaves its results or one error value, and `LuaFrame` restores the top on
  early return, exception and failed call (`LuaFrameTests`).
- Hot paths allocate nothing: scalar pushes and reads, protected calls, callbacks and the generated call shape
  (`ZeroAllocationTests`, `ReadIntegerBindingTests`, `StringBindingTests`).
- Stale references are detected. A `LuaRef` from an earlier attachment epoch or state generation is never pushed or
  released into the current registry (`References_are_invalidated_by_detach_and_reattach`,
  `A_reference_from_the_pre_reset_state_never_releases_a_current_generation_slot`).
- Callbacks are released deterministically. `Release`, `Detach` and the exclusive SDK reset transition neutralize the
  closure before its state is freed; lifecycle transitions wait for admitted callback creation and invocation before
  draining the registry (`LuaCallbackTests`, `CallbackLifetimeConcurrencyTests`).
- Scripts cannot break the helpers, even by redefining `error` and `tostring`
  (`Helpers_survive_a_script_that_redefines_error_and_tostring`).
- A detached or transitioning runtime fails cleanly: `AcquireOperation` refuses to hand out a state, and the transition
  waits for admitted work before it invalidates the old Lua universe (`LuaRuntimeTests`).

## Run the tests

```powershell
dotnet test --project tests/CheatEngine.SDK.Lua.Tests
dotnet test --project tests/CheatEngine.SDK.Lua.Tests --filter-trait "Category=NativeLua"
```

Tests tagged `Category=NativeLua` run against the Lua DLL of Cheat Engine 7.7 kept in
[`native/cheat-engine`](../../native/cheat-engine/README.md), so nothing has to be installed. See
[`tests/CheatEngine.SDK.Lua.Tests`](../../tests/CheatEngine.SDK.Lua.Tests/README.md).
