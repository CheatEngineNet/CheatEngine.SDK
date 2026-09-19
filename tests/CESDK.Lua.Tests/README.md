# CESDK.Lua.Tests

Contract tests for `CESDK.Lua`, the managed layer over the Lua C API, mostly run against a real Lua 5.3 library.

## Objective

Assert the contracts of `CESDK.Lua` instead of only documenting them. The project does not test Cheat Engine.

## Why it exists

`CESDK.Lua` sits between generated code and native Lua. A mistake there unbalances the Lua stack, lets a Lua error
unwind a managed frame or allocates on a hot path. Only a real interpreter shows these faults.

## How it works

| Area                           | Contract asserted                                                                                                                      |
|--------------------------------|----------------------------------------------------------------------------------------------------------------------------------------|
| Stack and protected operations | `LuaFrame` restores the stack top on every exit path, and a raising metamethod or a syntax error becomes a status, never a Lua unwind  |
| Marshallers                    | Values round trip exactly, and a value that does not fit is refused instead of truncated                                               |
| References and runtime         | Every `LuaRuntime.Attach` advances the epoch, and a `LuaRef` from an earlier epoch is never pushed                                     |
| Callbacks                      | Test thunks follow the SDK rule (static, cdecl, catch-all), state travels in the upvalue, and `Detach` neutralizes forgotten callbacks |
| Allocation                     | `AllocationGate` requires exactly zero bytes allocated on the calling thread once a body is warm                                       |
| Call shape                     | `Generated/MemoryBindings.cs` and `StringBindings.cs` hold the call shape of generated bodies, run against Lua stand-ins               |

The suite runs sequentially, because `LuaRuntime` is one ambient binding per process and every `LuaRef` reads its epoch.
`HostDouble` stands in for the host's exported functions, and `RuntimeScope` attaches on creation and detaches on
dispose. The tests reach internals of `CESDK.Lua`, such as `Utf8Scratch` and `LuaRef.Rebind`, through
`InternalsVisibleTo`. The allocation gate counts the calling thread only, which also runs the interpreter and the
callbacks. Native allocations inside Lua stay invisible by design. Tests that need Lua carry `Category=NativeLua`, and
the library lookup is described in [`tests/CESDK.Tests.Shared/README.md`](../CESDK.Tests.Shared/README.md). The
remaining tests run everywhere,
so a default run never ends with zero executed tests.

## Run the tests

```powershell
dotnet test --project tests/CESDK.Lua.Tests
dotnet test --project tests/CESDK.Lua.Tests --filter-trait "Category=NativeLua"
```

## Promise

- The stack is balanced on every path of a frame (`LuaFrameTests`, `ProtectedOperationTests`).
- No Lua error unwinds a managed frame, and no managed exception escapes a thunk (`ProtectedOperationTests`,
  `LuaCallbackTests`).
- A stale reference is detected by its epoch (`LuaRefEpochTests`, `LuaRefTests`).
- `LuaRuntime.Detach` neutralizes every callback the plugin forgot (`LuaCallbackTests`).
- Unlinking a callback takes the registry gate itself, from any position and from any thread
  (`LuaCallbackRegistryTests`).
- Hot paths allocate zero bytes once warm: scalars, protected calls, callbacks and the call shape
  (`ZeroAllocationTests`, `ReadIntegerBindingTests`, `StringBindingTests`).
