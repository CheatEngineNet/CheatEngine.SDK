# CESDK.Hosting

The plugin lifecycle runtime: it answers Cheat Engine's version, enable and disable callbacks and runs `OnEnable` and
`OnDisable` on the plugin class.

## Objective

Run a CESDK plugin inside Cheat Engine. The library fills the init record that Cheat Engine reads and answers its
version, enable and disable callbacks. It binds the Lua API, captures the main thread and logs every failure instead of
raising it.

## Why it exists

Cheat Engine loads a managed plugin through a narrow native contract. The contract is a packed init record, three
`stdcall` callbacks and a table of host functions that lives on the host's stack. An exception that escapes a native
callback ends the process. This library owns that contract once. A plugin author writes `OnEnable` and `OnDisable`, and
a plugin fault becomes a log entry.

## How it works

The entry point that `CESDK.SourceGenerators.EntryPoint` generates calls
`PluginHost.InitializeManaged<TFactory>(args, size)`, where the generated `TFactory` implements `IPluginFactory`. Cheat
Engine calls it more than once per load, and every call writes the same values, including the same name pointer. It
refuses a null record and a non-x64 process. The official managed template mirrors the init record unpacked, which is 40
bytes on x64. This library writes only the 36 packed bytes of `PluginInitRecord` from [
`CESDK.Abi`](../CESDK.Abi/README.md). It copies the plugin name once into native memory that is never freed. ASCII is
copied exactly. Other characters go through the process ANSI code page, and an unrepresentable one becomes `?`. A plugin
that wants the same name on every machine keeps it ASCII. The library also builds on [
`CESDK.Lua`](../CESDK.Lua/README.md) and ships inside the `CESDK` package.

| Type                                                                                          | Role                                                                                                                                                                |
|-----------------------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `CheatEnginePlugin`, `IPluginFactory` (`CESDK.Hosting.Plugin`)                                | The base class you derive from, and the factory contract the generated entry point implements                                                                       |
| `PluginHost` (`CESDK.Hosting.Bootstrap`)                                                      | `InitializeManaged<TFactory>`, the three native callbacks, and the readers `IsInitialized`, `IsEnabled` and `Context`, which are lock-free and safe from any thread |
| `PluginContext` (`CESDK.Hosting.Context`)                                                     | Immutable facts of one enable: `PluginId`, `Epoch`, `MainThreadId`, `IsMainThread`, `IsCurrent`, `ReportedExportsSize`, `HasProcessMessages`, `HasCheckSynchronize` |
| `MainThread` (`CESDK.Hosting.Threading`)                                                      | `IsMainThread`, `ProcessMessages()`, `CheckSynchronize(int)` and `Invoke`                                                                                           |
| `HostLog`, `IHostLogSink`, `HostLogLevel`, `DebugOutputLogSink` (`CESDK.Hosting.Diagnostics`) | The logging seam                                                                                                                                                    |

`EnablePlugin` runs these steps in order:

1. Copy the host's exports record. A record smaller than 48 bytes, or without `GetLuaState`, fails.
2. Bind the Lua API to the `lua53-64.dll` already loaded in the process, and check that the state's registry is a table.
3. Construct the plugin on the first enable, before `LuaRuntime` is attached. A constructor call that needs Cheat
   Engine, such as `LuaRuntime.AcquireState` or `CheatEnginePlugin.Context`, throws `InvalidOperationException`, because
   `LuaRuntime` attaches after construction.
4. Attach `LuaRuntime` with a new epoch and publish the `PluginContext`. The enabling thread becomes the main thread of
   this enable.
5. Run `OnEnable`.

`DisablePlugin` runs `OnDisable` while `LuaRuntime` is still attached. Then the runtime detaches, which neutralizes
every Lua callback the plugin forgot to release, and the context is withdrawn. The next enable reuses the plugin
instance with a new epoch, so a `PluginContext` or Lua reference from an earlier enable is stale.
`PluginContext.IsCurrent` tells a kept context from the live one. While the plugin is disabled, `PluginHost.Context`
returns `null` and `CheatEnginePlugin.Context` throws.

| Situation                                                                                                                                    | Cheat Engine receives | Afterward                                             |
|----------------------------------------------------------------------------------------------------------------------------------------------|-----------------------|-------------------------------------------------------|
| `OnEnable` throws                                                                                                                            | `FALSE`               | Runtime detached, context withdrawn, exception logged |
| Constructor throws, or the factory returns `null`                                                                                            | `FALSE`               | Plugin disabled, the next enable retries              |
| Exports record too small, no `GetLuaState`, no Lua module, or a failed registry check                                                        | `FALSE`               | Plugin disabled, reason logged                        |
| `OnDisable` throws                                                                                                                           | `TRUE`                | Failure logged; plugin disabled                        |
| Enable while enabled, or disable while disabled                                                                                              | `TRUE`                | Nothing changes, warning logged                       |
| Lifecycle callback nested in `OnEnable` or `OnDisable` on the same thread, for example a plugin dialog handled while `ProcessMessages` pumps | `FALSE`               | The outer transition decides the state                |

`MainThread.IsMainThread` is `false` while the plugin is disabled. `ProcessMessages()` and `CheckSynchronize(int)` call
the host's functions and throw `InvalidOperationException` on another thread or when the host left the function out.
`Invoke` runs work inline on the main thread. From another thread it goes through Cheat Engine's Lua `synchronize`
global, waits, and rethrows the work's exception on the caller. A main thread that blocks on a worker which calls
`Invoke` deadlocks unless it pumps `CheckSynchronize`.

`HostLog` receives every failure that a callback turns into `FALSE` or 0. The default sink writes to
`OutputDebugStringW`, so a debugger attached to Cheat Engine or DebugView shows the entries. Set `HostLog.Sink` to route
entries elsewhere and `HostLog.MinimumLevel` (default `Information`) to filter. `Trace` adds every lifecycle call.

```csharp
using System;
using CESDK.Annotations.Plugin;
using CESDK.Hosting.Diagnostics;
using CESDK.Hosting.Plugin;
using CESDK.Hosting.Threading;

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

The tests in `tests/CESDK.Hosting.Tests` call the callbacks through the function pointers that they read back from a
simulated host record.

1. No exception reaches Cheat Engine: `InitializeManaged` and the three callbacks catch everything, return 0 or `FALSE`
   and log it. A throwing log sink never escapes `HostLog` (`InitializeManagedTests`, `EnablePluginTests`,
   `DisablePluginTests`, `HostLogTests`).
2. The bootstrap writes exactly 36 bytes, at an aligned and an odd address. A positive `size` below 36 writes nothing,
   and a second factory type is rejected (`InitializeManagedTests`).
3. The plugin is constructed once, after the Lua API is bound and before `LuaRuntime` attaches, and a failed
   construction retries on the next enable. A failed `OnEnable` leaves the runtime detached and the context withdrawn
   (`EnablePluginTests`).
4. `OnDisable` runs while attached, a throwing `OnDisable` still disables the plugin, and a forgotten Lua callback is
   neutralized. A later enable reuses the instance with a new epoch (`DisablePluginTests`).
5. A nested lifecycle callback is refused and the outer transition stands (`ReentrancyTests`).
6. `ProcessMessages`, `CheckSynchronize` and `Invoke` throw before the plugin is enabled, and the two pumps refuse
   worker threads (`MainThreadTests`).
7. `Invoke` reports a missing or failing `synchronize` and rethrows the work's exception on the caller
   (`MainThreadTests`).
8. The native boundary uses no delegate marshalling, structure marshalling or reflection activation:
   `eng/BannedSymbols.txt` makes each a build error.

## Run the tests

```powershell
dotnet test --project tests/CESDK.Hosting.Tests
```

Tests tagged `Category=NativeLua` run against the Lua DLL of Cheat Engine 7.7 kept in
[`native/cheat-engine`](../../native/cheat-engine/README.md), so nothing has to be installed. See
the [test project README](../../tests/CESDK.Hosting.Tests/README.md). To run a plugin inside Cheat Engine,
follow [Run it in Cheat Engine](../../tests/CESDK.LivePlugin/README.md#run-it-in-cheat-engine).
