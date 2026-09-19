# CESDK.Hosting.Tests

Tests for `CESDK.Hosting` against a simulated Cheat Engine host, with a real Lua 5.3 state where the lifecycle needs
one.

## Objective

Prove that bootstrap, enable, disable, main-thread dispatch and logging follow the plugin protocol. Prove that a plugin
exception becomes a failure result plus a log entry.

## Why it exists

`CESDK.Hosting` sits on the native boundary. An escaping exception or a write past the init record crashes the Cheat
Engine process, so those paths must be provable off-host.

## How it works

Every callback call crosses the boundary Cheat Engine crosses: the callbacks are read back from the init record as typed
`delegate* unmanaged[Stdcall]` fields. The bootstrap is the managed `PluginHost.InitializeManaged<TFactory>` that the
generated entry point calls. Tests tagged `Category=NativeLua` run against the Lua DLL of Cheat Engine 7.7 kept in
[`native/cheat-engine`](../../native/cheat-engine/README.md), so nothing has to be installed. See [
`tests/CESDK.Tests.Shared/README.md`](../CESDK.Tests.Shared/README.md).

| Piece                        | Role                                                                                                                                                                                                  |
|------------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `Support/HostSimulator.cs`   | A 36-byte init record buffer at an aligned or an odd address, followed by guard bytes that catch a write past the record. Calls the bootstrap, then the three lifecycle callbacks through the record. |
| `Support/FakeExports.cs`     | `[UnmanagedCallersOnly]` `stdcall` doubles for the exports record. `GetLuaState` hands out the fixture state.                                                                                         |
| `Support/RecordingPlugin.cs` | Records what it observes, throws where a test says, and makes a nested lifecycle call from inside `OnEnable` or `OnDisable`.                                                                          |

Every host test starts with `HostingTest.Reset()`, which returns the host, the runtime, the doubles and the log to their
initial state. Tests run sequentially because `PluginHost`, `LuaRuntime` and the doubles are process-wide. Declare the
`NativeLuaState` before its `HostSimulator`, because disposal runs in reverse order. The simulator detaches `LuaRuntime`
and releases forgotten Lua callbacks while the state must still be open.

The `synchronize` stand-in is a Lua function that runs the work where it is called. Dispatch tests check the mechanics
and claim no thread hop. The end-to-end check inside Cheat Engine is [
`tests/CESDK.LivePlugin`](../CESDK.LivePlugin/README.md#run-it-in-cheat-engine).

## Promise

- The bootstrap writes exactly 36 bytes, at an aligned and an odd address. A refused bootstrap writes nothing.
- An exception from a factory name getter makes the bootstrap return 0. An exception from a plugin constructor or
  `OnEnable` makes the callback return `FALSE`. `OnDisable` failures are logged, cleanup completes, and its callback
  returns `TRUE` so Cheat Engine records the resulting disabled state. None escapes.
- A nested `EnablePlugin` or `DisablePlugin` call from inside `OnEnable` or `OnDisable` returns `FALSE`, and the outer
  transition stands.
- `DisablePlugin` detaches `LuaRuntime`, withdraws `PluginContext` and releases Lua callbacks the plugin forgot.
- `MainThread.ProcessMessages`, `CheckSynchronize` and `Invoke` throw `InvalidOperationException` while no plugin is
  enabled. `Invoke` from a worker returns the result or rethrows on the caller.

## Run the tests

```powershell
dotnet test --project tests/CESDK.Hosting.Tests
dotnet test --project tests/CESDK.Hosting.Tests --filter-trait "Category=NativeLua"
```
