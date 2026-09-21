# CheatEngine.SDK.Lua.Tests

Contract tests for `CheatEngine.SDK.Lua`, the managed layer over the Lua C API, mostly run against a real Lua 5.3
library.

## Objective

Assert the contracts of `CheatEngine.SDK.Lua` instead of only documenting them. The project does not test Cheat Engine.

## Why it exists

`CheatEngine.SDK.Lua` sits between generated code and native Lua. A mistake there unbalances the Lua stack, lets a Lua
error unwind a managed frame or allocates on a hot path. Only a real interpreter shows these faults.

## How it works

| Area                           | Contract asserted                                                                                                                                                |
|--------------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Stack and protected operations | `LuaFrame` restores the stack top on every exit path; raising metamethods, syntax errors and host-object pusher exits become statuses, never a Lua unwind        |
| Marshallers                    | Values round trip exactly, and a value that does not fit is refused instead of truncated                                                                         |
| References and runtime         | Every `LuaRuntime.Attach` advances the epoch, and a `LuaRef` from an earlier epoch is never pushed                                                               |
| Callbacks                      | Test thunks follow the SDK rule (static, cdecl, catch-all), state travels in the upvalue, and `Detach` drains admitted invocations before neutralizing callbacks |
| Allocation                     | `AllocationGate` requires exactly zero bytes allocated on the calling thread once a body is warm                                                                 |
| Call shape                     | `Generated/MemoryBindings.cs` and `StringBindings.cs` hold the call shape of generated bodies, run against Lua stand-ins                                         |

The suite runs sequentially, because `LuaRuntime` is one ambient binding per process and every `LuaRef` reads its epoch.
`HostDouble` stands in for the host's exported functions, and `RuntimeScope` attaches on creation and detaches on
dispose. The tests reach internals of `CheatEngine.SDK.Lua`, such as `Utf8Scratch` and `LuaRef.Rebind`, through
`InternalsVisibleTo`. The allocation gate counts the calling thread only, which also runs the interpreter and the
callbacks. Native allocations inside Lua stay invisible by design. Tests that need Lua carry `Category=NativeLua`, and
the library lookup is described in
[`tests/CheatEngine.SDK.Tests.Shared/README.md`](../CheatEngine.SDK.Tests.Shared/README.md). The
remaining tests run everywhere,
so a default run never ends with zero executed tests.

The SDK-012 fixture maps state providers per managed worker. It proves the rejected first acquisition, then uses a
rooted coroutine whose pointer differs from the main state while the global and private registry remain shared. The
fixture also proves reset-generation invalidation of a shared reference and callback. It is a deterministic lifecycle
test and does not qualify concurrent execution in a live Cheat Engine process.

## Run the tests

```powershell
dotnet test --project tests/CheatEngine.SDK.Lua.Tests
dotnet test --project tests/CheatEngine.SDK.Lua.Tests --filter-trait "Category=NativeLua"
```

## Promise

- The stack is balanced on every path of a frame (`LuaFrameTests`, `ProtectedOperationTests`).
- No Lua error unwinds a managed frame, including a host-object pusher's non-local exit, and no managed exception
  escapes a thunk (`ProtectedOperationTests`, `LuaCallbackTests`, `NativeFailureProcessTests`).
- A stale reference is detected by its epoch (`LuaRefEpochTests`, `LuaRefTests`).
- `LuaRuntime.Detach` neutralizes every callback the plugin forgot and waits for an admitted callback while rejecting a
  later callback invocation (`LuaCallbackTests`, `CallbackLifetimeConcurrencyTests`).
- Unlinking a callback takes the registry gate itself, from any position and from any thread
  (`LuaCallbackRegistryTests`).
- Hot paths allocate zero bytes once warm: scalars, protected calls, callbacks, the generated call shape and the
  benchmark's 1,024-byte non-ASCII UTF-8 payload (`ZeroAllocationTests`, `ReadIntegerBindingTests`,
  `StringBindingTests`).
