# CheatEngine.SDK.Hosting.Tests

Tests for `CheatEngine.SDK.Hosting` against a simulated Cheat Engine host, with a real Lua 5.3 state where the
lifecycle needs one.

## Objective

Prove that bootstrap, enable, disable, main-thread dispatch and logging follow the plugin protocol. Prove that a plugin
exception becomes a failure result plus a log entry.

## Why it exists

`CheatEngine.SDK.Hosting` sits on the native boundary. An escaping exception or a write past the init record crashes
the Cheat Engine process, so those paths must be provable off-host.

## How it works

Every callback call crosses the boundary Cheat Engine crosses: the callbacks are read back from the init record as typed
`delegate* unmanaged[Stdcall]` fields. The bootstrap is the managed `PluginHost.InitializeManaged<TFactory>` that the
generated entry point calls. Tests tagged `Category=NativeLua` run against the Lua DLL of Cheat Engine 7.7 kept in
[`native/cheat-engine`](../../native/cheat-engine/README.md), so nothing has to be installed. See [
`tests/CheatEngine.SDK.Tests.Shared/README.md`](../CheatEngine.SDK.Tests.Shared/README.md).

| Piece                        | Role                                                                                                                                                                                                  |
|------------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `Support/HostSimulator.cs`   | A 36-byte init record buffer at an aligned or an odd address, followed by guard bytes that catch a write past the record. Calls the bootstrap, then the three lifecycle callbacks through the record. |
| `Support/FakeExports.cs`     | `[UnmanagedCallersOnly]` `stdcall` doubles for the exports record. `GetLuaState` hands out the fixture state.                                                                                         |
| `Support/RecordingPlugin.cs` | Records what it observes, throws where a test says, can hold `OnEnable` at a deterministic point, and makes a nested lifecycle call from inside `OnEnable` or `OnDisable`.                            |

Every host test starts with `HostingTest.Reset()`, which returns the host, the runtime, the doubles and the log to their
initial state. Tests run sequentially because `PluginHost`, `LuaRuntime` and the doubles are process-wide. Declare the
`NativeLuaState` before its `HostSimulator`, because disposal runs in reverse order. The simulator detaches `LuaRuntime`
and releases forgotten Lua callbacks while the state must still be open.

The `synchronize` stand-in is a Lua function that runs the work where it is called. Dispatch tests now prove that this
wrong-thread behavior is rejected; they do not claim a thread hop. The end-to-end check inside Cheat Engine is [
`tests/CheatEngine.SDK.LivePlugin`](../CheatEngine.SDK.LivePlugin/README.md#run-it-in-cheat-engine).

## Promise

- The bootstrap writes exactly 36 bytes, at an aligned and an odd address. Its second raw host integer is recorded but
  never interpreted as a size or version. A refused bootstrap writes nothing.
- An exception from a factory name getter makes the bootstrap return 0. An exception from a plugin constructor or
  `OnEnable` makes the callback return `FALSE`. `OnDisable` failures are logged, cleanup completes, and its callback
  returns `TRUE` so Cheat Engine records the resulting disabled state. None escapes.
- A nested or competing `EnablePlugin`/`DisablePlugin` call returns `FALSE` without waiting, and the outer transition
  stands. `IsEnabled` is false in `Enabling` and `Disabling`, although lifecycle callbacks retain a context.
- `DisablePlugin` closes worker-dispatch admission, signals `PluginContext.ShutdownToken`, pumps
  `CheckSynchronize` when needed to drain admitted work, closes and drains ordinary Lua operations, then detaches
  `LuaRuntime`, withdraws `PluginContext` and releases Lua callbacks the plugin forgot.
- `MainThread.ProcessMessages`, `CheckSynchronize` and `Invoke` throw `InvalidOperationException` while no plugin is
  enabled. A worker `Invoke` rejects a `synchronize` callback that runs it on the wrong managed thread.
- Without a Lua module, or with a module that lacks one Lua export, the enable fails before any plugin code runs and
  before the host is asked for its Lua state, and the log names what is missing (`EnablePluginTests`,
  `PartialLuaModuleEnableTests`).
- The 2.0 conservative admission default refuses a worker before the Lua state provider ever runs; the worker-side
  `synchronize` hand-off inside `MainThread.Invoke` is the single documented default exception and keeps working
  unopted-in (`MainThreadTests`).
- An external Lua-state reset is logged once as `LuaStateReplacedExternally:` and does not survive into the next
  enable; there is no public reset API (`ExternalResetLifecycleTests`).
- A throwing or re-entrant `HostLog` sink is contained during every native callback, and a second-factory rejection is
  logged outside the registration lock (`HostLogContainmentTests`).
- The opt-in `CheatEngineSdkIdentification` diagnostic is silent unless requested, bounded, path-free and built without
  calling Lua or constructing the plugin (`LoadIdentificationTests`).
- The native hostfxr host emulator's A/B coexistence facts are consumed as C2 evidence only, redacted of every
  absolute path, and never presented as Cheat Engine's own behaviour (`NativeHostEmulatorTests`).
- The bootstrap's second raw argument never changes the 36-byte record write, for any value including `int.MinValue`
  and `int.MaxValue` (`InitializeManagedTests`); repeated enabling never accumulates a Lua-module loader reference
  (`LuaModuleLocatorTests`); and `DisablePlugin` pumps a worker genuinely blocked inside the host's real Lua
  `synchronize` call, not only a dispatch override, before it detaches (`DisablePluginTests`).

## Run the tests

```powershell
dotnet test --project tests/CheatEngine.SDK.Hosting.Tests
dotnet test --project tests/CheatEngine.SDK.Hosting.Tests --filter-trait "Category=NativeLua"
```
