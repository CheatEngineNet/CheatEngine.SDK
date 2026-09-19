# CheatEngine.SDK.Engine.Tests

Tests for `CheatEngine.SDK.Engine`: object handles, ownership, value conventions and enums, driven by a fake Cheat
Engine object model on a real Lua 5.3 library.

## Objective

Prove that `CEObject`, `Owned<T>`, `Address`, `IndexBase`, `LuaSequence` and the Cheat Engine enums behave as
documented. Cover every failure path without a running Cheat Engine.

## Why it exists

Engine code runs inside Cheat Engine against an object model the SDK cannot inspect. The userdata decoder
`CEObject.TryRead`, the access primitives and their allocation budget must be provable off-host.

## How it works

Tests tagged `Category=NativeLua` run against the Lua DLL of Cheat Engine 7.7 kept in
[`native/cheat-engine`](../../native/cheat-engine/README.md), so nothing has to be installed. The other tests need no
native code. See [
`tests/CheatEngine.SDK.Tests.Shared/README.md`](../CheatEngine.SDK.Tests.Shared/README.md).

| Piece                         | Role                                                                                                                                                                                                                       |
|-------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `Support/FakeHost.cs`         | Stands in for `GetLuaState` and `LuaPushClassInstance`, not for Lua. A Lua model supplies the classes `Object`, `Probe` and `Stubborn`, getters and setters that can raise, zero-based `obj[i]` and `destroy` bookkeeping. |
| `Support/HostScope.cs`        | Attaches `LuaRuntime` to a fixture state for one test and detaches on dispose. Tests attach the runtime only through it, and tests that need it unattached call `LuaRuntime.Detach()` first.                               |
| `Support/DebugAssertScope.cs` | Turns a failed `Debug.Assert` into an exception, so the Debug-only main-thread guard of `Owned<T>` is testable. That test skips in Release.                                                                                |
| `Support/EngineTest.cs`       | `RequireNativeLua()` skips without a Lua library. `RunOnWorker` runs work on a fresh thread and returns what it threw.                                                                                                     |
| `AssemblyInfo.cs`             | Runs tests sequentially, because `LuaRuntime` and the fake host are process-wide.                                                                                                                                          |

Each fake object is a Lua table found by its pointer, so every push of one pointer finds the same state. Pointers are
synthetic and never dereferenced. The double follows the assumed userdata layout, so the suite cannot prove that Cheat
Engine's own `LuaPushClassInstance` uses it.

## Promise

- A raising getter, setter or method reaches the caller as a status with one error value, never as an exception. Tests
  assert `L.Top` after each primitive.
- Hot paths allocate exactly zero bytes after warm-up, measured by `Support/AllocationGate.cs`: typed get, set and call,
  stack-level primitives, `Address` and enum marshalling, enum name lookup.
- A typed operation acquires the state once and pushes the object once (`HostCallCountTests`).
- `Owned<T>` destroys the object exactly once. After the plugin is disabled, `Dispose` skips the destroy call and leaves
  the object alive.
- Enum values and Cheat Engine names are pinned by literals.

## Run the tests

```powershell
dotnet test --project tests/CheatEngine.SDK.Engine.Tests
dotnet test --project tests/CheatEngine.SDK.Engine.Tests --filter-trait "Category=NativeLua"
```
