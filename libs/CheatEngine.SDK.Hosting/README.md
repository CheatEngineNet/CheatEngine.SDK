# CheatEngine.SDK.Hosting

The plugin lifecycle runtime: it answers Cheat Engine's version, enable and disable callbacks and runs `OnEnable` and
`OnDisable` on the plugin class.

## Objective

Run a CheatEngine.SDK plugin inside Cheat Engine. The library fills the init record that Cheat Engine reads and answers
its version, enable and disable callbacks. It binds the Lua API, captures the main thread and logs every failure
instead of raising it.

## Why it exists

Cheat Engine loads a managed plugin through a narrow native contract. The contract is a packed init record, three
`stdcall` callbacks and a table of host functions that lives on the host's stack. An exception that escapes a native
callback ends the process. This library owns that contract once. A plugin author writes `OnEnable` and `OnDisable`, and
a plugin fault becomes a log entry.

Hosting is compiled with `CheatEngine.SDK.Analyzers` as an analyzer-only project reference. This enforces SDK plugin
diagnostics while Hosting builds, but adds no runtime assembly dependency and no package asset to a plugin deployment
folder. Like the other shipping libraries it declares AOT compatibility and verifies that its runtime references carry
the same metadata; the standalone publication probe is
[`tests/CheatEngine.SDK.AotProbe`](../../tests/CheatEngine.SDK.AotProbe/README.md).

## How it works

The entry point that `CheatEngine.SDK.SourceGenerators.EntryPoint` generates calls
`PluginHost.InitializeManaged<TFactory>(args, hostArgument)`, where the generated `TFactory` implements
`IPluginFactory`. Cheat
Engine calls it more than once per load, and every call writes the same values, including the same name pointer. It
refuses a null record and a non-x64 process. The second integer is an opaque raw host argument: CE 7.7 live probing has
not established whether it is a size, version, or another discriminator, so Hosting records it as
`PluginHost.LastInitRecordArgument` without deriving behavior from it. This library writes the 36 packed bytes of
`PluginInitRecord` from [`CheatEngine.SDK.Abi`](../CheatEngine.SDK.Abi/README.md). It copies the plugin name once into
native memory that is
never freed. ASCII is copied exactly. Other characters go through the process ANSI code page, and an unrepresentable
one becomes `?`. A plugin that wants the same name on every machine keeps it ASCII. The library also builds on [
`CheatEngine.SDK.Lua`](../CheatEngine.SDK.Lua/README.md) and ships inside the `CheatEngine.SDK` package.

| Type                                                                                                    | Role                                                                                                                                                                                 |
|---------------------------------------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `CheatEnginePlugin`, `IPluginFactory` (`CheatEngine.SDK.Hosting.Plugin`)                                | The base class you derive from, and the factory contract the generated entry point implements                                                                                        |
| `PluginHost` (`CheatEngine.SDK.Hosting.Bootstrap`)                                                      | `InitializeManaged<TFactory>`, the three native callbacks, and the lock-free readers `Phase`, `IsInitialized`, `IsEnabled` and `Context`                                             |
| `PluginContext` (`CheatEngine.SDK.Hosting.Context`)                                                     | Immutable facts of one enable: `PluginId`, `Epoch`, `MainThreadId`, `ShutdownToken`, `IsMainThread`, `IsCurrent`, `ReportedExportsSize`, `HasProcessMessages`, `HasCheckSynchronize` |
| `MainThread` (`CheatEngine.SDK.Hosting.Threading`)                                                      | `IsMainThread`, `ProcessMessages()`, `CheckSynchronize(int)` and `Invoke`                                                                                                            |
| `HostLog`, `IHostLogSink`, `HostLogLevel`, `DebugOutputLogSink` (`CheatEngine.SDK.Hosting.Diagnostics`) | The logging seam                                                                                                                                                                     |

The lifecycle state machine is `Uninitialized → Registered → Enabling → Enabled → Disabling → Registered`.
`PluginHost.IsEnabled` is true only in stable `Enabled`. `Context` is deliberately available during `Enabling` and
`Disabling`, because `OnEnable` and `OnDisable` need the attached Lua runtime; it is withdrawn after cleanup.

`EnablePlugin` runs these steps in order:

1. Copy the host's exports record. A record smaller than 48 bytes, or without `GetLuaState`, fails.
2. Bind the Lua API to the `lua53-64.dll` already loaded in the process, and check that the state's registry is a table.
3. Construct the plugin on the first enable, before `LuaRuntime` is attached. A constructor call that needs Cheat
   Engine, such as `LuaRuntime.AcquireOperation()` or `CheatEnginePlugin.Context`, throws `InvalidOperationException`,
   because
   `LuaRuntime` attaches after construction.
4. Attach `LuaRuntime` with a new epoch and publish the `PluginContext`. The enabling thread becomes the main thread of
   this enable. The phase is `Enabling`, so `IsEnabled` remains false and worker dispatch admission remains closed.
5. Run `OnEnable`. On success, open worker-dispatch admission and move to `Enabled`. On any failure, cancel the
   context's shutdown token, detach in `finally`, withdraw the context, and return to `Registered`.

`DisablePlugin` moves to `Disabling`, closes worker-dispatch admission, and signals `PluginContext.ShutdownToken`
before it invokes plugin cleanup. Work admitted before that boundary is drained first. When disable runs on the
captured GUI thread, Hosting pumps the host's `CheckSynchronize(0)` slot so a worker already waiting in
`synchronize` can finish; cross-thread dispatch is refused if that slot is absent. A disable nested in an admitted Lua
operation or already executing dispatched action is refused before it changes lifecycle state. `OnDisable` then runs
while `LuaRuntime` is still attached. Finally, Hosting closes and drains `LuaRuntime`'s independent operation
admission, then detaches (neutralizing every Lua callback the plugin forgot), withdraws the context, and returns to
`Registered`. If detach fails, the callback returns `FALSE` and the host remains visibly `Disabling` with its runtime
attached rather than reporting a completed shutdown. After a completed shutdown, the next enable reuses
the plugin instance with a new epoch, so a `PluginContext` or Lua reference from an earlier enable is stale.
`PluginContext.IsCurrent` tells a kept context from the live one. While the plugin is disabled, `PluginHost.Context`
returns `null` and `CheatEnginePlugin.Context` throws.

| Situation                                                                             | Cheat Engine receives | Afterward                                                                              |
|---------------------------------------------------------------------------------------|-----------------------|----------------------------------------------------------------------------------------|
| `OnEnable` throws                                                                     | `FALSE`               | Shutdown signalled, runtime detached in `finally`, context withdrawn, exception logged |
| Constructor throws, or the factory returns `null`                                     | `FALSE`               | Plugin disabled, the next enable retries                                               |
| Exports record too small, no `GetLuaState`, no Lua module, or a failed registry check | `FALSE`               | Plugin disabled, reason logged                                                         |
| `OnDisable` throws                                                                    | `TRUE`                | Failure logged; plugin disabled                                                        |
| Enable while enabled, or disable while disabled                                       | `TRUE`                | Nothing changes, warning logged                                                        |
| Lifecycle callback nested in a transition or concurrent with one                      | `FALSE`               | It does not wait; the outer transition decides the state                               |
| Disable delivered on a thread other than the captured main thread                     | `FALSE`               | No cleanup starts; the current enable remains usable                                   |

`MainThread.IsMainThread` is `false` while the plugin is disabled. `ProcessMessages()` and `CheckSynchronize(int)` call
the host's functions and throw `InvalidOperationException` on another thread or when the host left the function out.
`Invoke` runs work inline on the main thread. From another thread it goes through Cheat Engine's Lua `synchronize`
global, waits, and rethrows the work's exception on the caller. Its thunk verifies that the host really invoked it on
the captured main thread and rejects an inline/wrong-thread host implementation. The exact CE 7.7 scheduling behavior
still needs its opt-in live probe. A main thread that disables while a worker already waits in `synchronize` pumps
`CheckSynchronize` as part of the shutdown drain; no fire-and-forget `queue(function, ...)` API is exposed.

## Plugin identity and coexistence

`PluginHost` is static, so its descriptor, runtime context, lifecycle lock and callback admission belong to the loaded
`CheatEngine.SDK.Hosting` assembly instance. They are not automatically scoped to an individual plugin DLL, a Client
service provider, or the process. Within one loaded Hosting assembly instance, the first factory registered by
`InitializeManaged<TFactory>` wins and a different factory is rejected; this is an intentional one-plugin admission
boundary for that assembly instance.

The exact Cheat Engine loader/runtime profile decides whether two plugin DLLs share that Hosting assembly instance or
receive distinct instances. The SDK does not configure an `AssemblyLoadContext`, and neither a .NET load-context type
nor a successful single-plugin test proves the host's behaviour. Consequently, multi-plugin and side-by-side SDK
dependency graphs are **not yet qualified capabilities**. Do not use Client-local DI scopes or locks as evidence that
all CE/Lua participants are serialized.

The opt-in [two-plugin live fixture](../../tests/CheatEngine.SDK.LivePlugin.Coexistence/README.md) logs the exact
plugin, Hosting-assembly and runtime `AssemblyLoadContext` identities for a controlled host run. It is an observation
protocol, not a CI test or a portability promise. It must be run and recorded before a supported coexistence profile is
claimed. The related qualification scenarios Q09 and Q10 (coexistence), Q19 (first calls from two workers) and Q30
(target switch) remain not executed.

The current supported route is the managed, framework-dependent plugin route. The standalone Native AOT probe checks
library publication constraints; it does not establish that Cheat Engine can load, disable, unload or remove a Native
AOT plugin. Microsoft documents that Native AOT libraries cannot be unloaded through `FreeLibrary`/`dlclose`; a native
plugin mode needs an explicit resident-core/adapter design and separate exact-host qualification. See
[Native AOT libraries](https://learn.microsoft.com/dotnet/core/deploying/native-aot/libraries).

`HostLog` receives every failure that a callback turns into `FALSE` or 0. The default sink writes to
`OutputDebugStringW`, so a debugger attached to Cheat Engine or DebugView shows the entries. Set `HostLog.Sink` to route
entries elsewhere and `HostLog.MinimumLevel` (default `Information`) to filter. `Trace` adds every lifecycle call.

```csharp
using System;
using CheatEngine.SDK.Annotations.Plugin;
using CheatEngine.SDK.Hosting.Diagnostics;
using CheatEngine.SDK.Hosting.Plugin;
using CheatEngine.SDK.Hosting.Threading;

namespace MyTrainer;

[CheatEnginePlugin("My Trainer")]
public sealed class TrainerPlugin : CheatEnginePlugin
{
    protected override void OnEnable() =>
        HostLog.Write(HostLogLevel.Information, $"Enabled as plugin {Context.PluginId}.");

    protected override void OnDisable() { }

    // Runs inline on the main thread, otherwise calls the host synchronize global and waits.
    public static void RunOnMainThread(Action work) => MainThread.Invoke<Action>(static run => run(), work);
}
```

## Promise

The tests in `tests/CheatEngine.SDK.Hosting.Tests` call the callbacks through the function pointers that they read back
from a simulated host record.

1. No exception reaches Cheat Engine: `InitializeManaged` and the three callbacks catch everything, return 0 or `FALSE`
   and log it. A throwing log sink never escapes `HostLog` (`InitializeManagedTests`, `EnablePluginTests`,
   `DisablePluginTests`, `HostLogTests`).
2. The bootstrap writes exactly 36 bytes, at an aligned and an odd address. Its second raw host argument is preserved
   for diagnostics without being interpreted, and a second factory type is rejected (`InitializeManagedTests`).
3. The plugin is constructed once, after the Lua API is bound and before `LuaRuntime` attaches, and a failed
   construction retries on the next enable. A failed `OnEnable` leaves the runtime detached and the context withdrawn
   (`EnablePluginTests`).
4. `OnDisable` closes admission, signals shutdown, drains already admitted GUI work, runs while attached, closes and
   drains ordinary Lua operations, and still detaches/neutralizes callbacks after a throwing `OnDisable`. A later enable
   reuses the instance with a new epoch (`DisablePluginTests`).
5. Nested and concurrent lifecycle callbacks are refused without waiting, and the outer transition stands
   (`ReentrancyTests`).
6. `ProcessMessages`, `CheckSynchronize` and `Invoke` throw before the plugin is enabled, the two pumps refuse worker
   threads, and a wrong-thread `synchronize` thunk is rejected (`MainThreadTests`).
7. `Invoke` reports a missing or failing `synchronize` and rethrows inline work exceptions on the caller
   (`MainThreadTests`).
8. The native boundary uses no delegate marshalling, structure marshalling or reflection activation:
   `eng/BannedSymbols.txt` makes each a build error.

## Run the tests

```powershell
dotnet test --project tests/CheatEngine.SDK.Hosting.Tests
```

Tests tagged `Category=NativeLua` run against the Lua DLL of Cheat Engine 7.7 kept in
[`native/cheat-engine`](../../native/cheat-engine/README.md), so nothing has to be installed. See
the [test project README](../../tests/CheatEngine.SDK.Hosting.Tests/README.md). To run a plugin inside Cheat Engine,
follow [Run it in Cheat Engine](../../tests/CheatEngine.SDK.LivePlugin/README.md#run-it-in-cheat-engine).
