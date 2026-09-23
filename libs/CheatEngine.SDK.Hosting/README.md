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
one becomes `?`. A plugin that wants the same name on every machine keeps it ASCII. **Unqualified (2.0):** whether
Cheat Engine 7.7 really reads a non-ASCII name as the process ANSI code page rather than UTF-8 has not been confirmed
by a host-based qualification run — no local Cheat Engine qualification observation is available on this branch. The
ANSI encoding is kept unchanged pending a future host-based qualification run (audit A02-22, A04-14, A20-Q05-3). The
library also builds on [
`CheatEngine.SDK.Lua`](../CheatEngine.SDK.Lua/README.md) and ships inside the `CheatEngine.SDK` package.

| Type                                                                                                    | Role                                                                                                                                                                                 |
|---------------------------------------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `CheatEnginePlugin`, `IPluginFactory` (`CheatEngine.SDK.Hosting.Plugin`)                                | The base class you derive from, and the factory contract the generated entry point implements                                                                                        |
| `PluginHost` (`CheatEngine.SDK.Hosting.Bootstrap`)                                                      | `InitializeManaged<TFactory>`, the three native callbacks, and the lock-free readers `Phase`, `IsInitialized`, `IsEnabled` and `Context`                                             |
| `PluginContext` (`CheatEngine.SDK.Hosting.Context`)                                                     | Immutable facts of one enable: `PluginId`, `Epoch`, `MainThreadId`, `ShutdownToken`, `IsMainThread`, `IsCurrent`, `ReportedExportsSize`, `HasProcessMessages`, `HasCheckSynchronize` |
| `MainThread` (`CheatEngine.SDK.Hosting.Threading`)                                                      | `IsMainThread`, `ProcessMessages()`, `CheckSynchronize(int)` and `Invoke`                                                                                                            |
| `HostLog`, `IHostLogSink`, `HostLogLevel`, `DebugOutputLogSink` (`CheatEngine.SDK.Hosting.Diagnostics`) | The logging seam, plus the opt-in `HostLog.IdentifyOnEnable` load identification diagnostic                                                                                          |

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

## Threading and Lua concurrency contract (ADR-07)

**Unqualified pending a future host-based validation run: no local Cheat Engine qualification is currently
available.** This section states the SDK 2.0 default and its rationale; it is not a claim that Cheat Engine's own
scheduling has been observed.

1. **The 2.0 default admission rule.** `CheatEngine.SDK.Lua.Runtime.LuaRuntime` starts no Lua work on a thread the host
   is not already running Lua on. Concretely: a new `LuaRuntime.AcquireOperation()`-family call is refused with
   `LuaAdmissionStatus.ThreadNotAdmitted` — as an exception from the throwing overloads, as `false` from the `Try*`
   overloads, or as that enum value from `TryAcquireOperationWithOutcome` — **before** the host's Lua state provider
   (`GetLuaState`) ever runs, so refusing a worker never creates a coroutine of the shared heap. This holds unless the
   calling thread is the host's captured main thread, the call is nested inside a Lua operation or callback the host
   already admitted on that thread, or it is the single documented default exception below.
2. **Admission protects this SDK copy's transitions, not the shared heap.** Distinct `lua_State*` pointers Cheat Engine
   hands out per OS thread can be coroutines of one shared Lua universe. Two plugins, or two SDK copies in the same
   process, are never serialized by this SDK (A05-07, A08-05, F04); admission only makes attach, detach and state
   replacement exclusive for this loaded `CheatEngine.SDK.Lua` assembly instance.
3. **Never store a `LuaState`.** `LuaRuntimeOperation` is a `ref struct` and cannot cross `await`
   (`typeof(LuaRuntimeOperation).IsByRefLike` is `true`). A captured `LuaState` outlives its admission and is not a
   substitute for one.
4. **pluginCS.** Cheat Engine holds its plugin critical section on callback paths, so a blocking callback that waits
   for the GUI thread can deadlock it. Adding a C# lock around Lua work is not a fix (A02-12): it adds contention
   without removing the native section CE already holds.
5. **`processMessages` re-enters.** `MainThread.ProcessMessages()` pumps the host's message loop, which can run queued
   work that calls back into the plugin. It is not a synchronization primitive (A16-20): never assume it drains
   exactly what you just queued and nothing else.
6. **CE object lifetime and GUI work stay host-affine.** Creation, destruction and property access on a Cheat Engine
   object happen on the thread the host expects; workers compute on managed copies only. `Task.Run` never authorizes
   driving the GUI. `[MainThreadOnly]` is applied only where the SDK has evidence for it (A16-18, A16-21).
7. **A `[LuaFunction]` invoked by the host on any thread runs under the host's admission for that thread.** The
   generated thunk's nested `AcquireOperation()` call succeeds because `t_operationDepth` is already non-zero on that
   thread, whichever thread CE chose to run the callback on.
8. **The single documented default exception: the worker-side `synchronize` hand-off.** `MainThread.Invoke` from a
   worker performs exactly one Lua global read, one closure and one protected call on the worker's own coroutine,
   through `LuaRuntime.AcquireOperationForMainThreadDispatch()`. This is Cheat Engine's designed cross-thread
   primitive, fixed and SDK-owned — never arbitrary plugin Lua — and the only route a worker has to the GUI thread
   (see [`exemples/09-main-thread`](../../exemples/09-main-thread/README.md)). It stays admitted under the
   conservative default; making it experimental would break the shipped 1.0.0 `MainThread.Invoke` contract. Its heap
   safety is in the Q19 C3/C4 evidence scope, not its admission.
9. **The experimental worker-thread opt-in.** `LuaRuntime.AdmitWorkerThreads()`, marked
   `[Experimental("CESDK5001")]` (see [`analyzers/docs/CESDK5001.md`](../../analyzers/docs/CESDK5001.md)), admits
   worker-thread `AcquireOperation()`-family calls that are not already nested in admitted work, until the next
   `LuaRuntime.Attach`/`Detach`. It does not serialize the shared heap and does not by itself make concurrent Lua
   calls from two threads safe. It stays gated until Q19 passes at **both** C3 (exact host, one plugin) **and** C4
   (exact host, two SDK copies) — no local qualification run is currently available for either level.
10. **Not qualified (A22-14).** The following remain open regardless of the conservative default:
    - Q19 at C4 (two simultaneous SDK copies, both opted in);
    - the worker `synchronize` hand-off's heap safety under real concurrent Lua traffic;
    - multi-plugin concurrency (see "Plugin identity and coexistence" above);
    - detection of an external Lua-state reset under live host conditions beyond the C1/C2 evidence in
      [`CheatEngine.SDK.Lua`](../CheatEngine.SDK.Lua/README.md#lua-state-replacement-20-decision) (Q17/Q18);
    - Q10.

`LuaRuntime` reports two one-shot diagnostics through `HostLog`, each at most once per attachment: a stable
`LuaWorkerThreadRefused:` warning naming the refused managed thread id, and a stable `LuaStateReplacedExternally:`
error when an external reset is detected (see the Lua README). Neither category token depends on Cheat Engine's UI
language.

`HostLog` receives every failure that a callback turns into `FALSE` or 0. The default sink writes to
`OutputDebugStringW`, so a debugger attached to Cheat Engine or DebugView shows the entries. Set `HostLog.Sink` to route
entries elsewhere and `HostLog.MinimumLevel` (default `Information`) to filter. `Trace` adds every lifecycle call.

A sink must not block (`HostLog.Write` can run from inside a native callback). It may throw: `HostLog` swallows a
sink exception so a logging failure never escapes to native code. It must not re-enter `HostLog.Write` from its own
`Write`, directly or through a path that logs: a re-entrant write on the same thread is dropped and counted instead
of recursing towards an uncatchable `StackOverflowException` (A24-21, SRC02-06). It must not re-enter a lifecycle
callback (`EnablePlugin`/`DisablePlugin`) or acquire a Lua operation while a lifecycle transition owns admission:
both are refused immediately, without waiting for the sink.

## Opt-in load identification

Set `HostLog.IdentifyOnEnable = true`, or the environment variable `CHEATENGINE_SDK_IDENTIFY_ON_ENABLE=1`, before an
enable attempt (a `[ModuleInitializer]` method applies it to the very first one). Every enable attempt that reaches
`EnablePlugin` then writes at most one `Information` entry, **before** the exports record is copied, so it appears
even when the Lua bind or plugin construction later fails:

```
CheatEngineSdkIdentification: sdk.version=…; sdk.commit=…; sdk.consistent=…; hosting.mvid=…; hosting.alc=…; plugin.id=…; plugin.assembly=…; host.argument=…; exports.size=…; bridge.fingerprint=…; bridge.sha256=…; lua.module=…; lua.sha256=…; ce.file=…; ce.fileVersion=…; runtime=…; arch=…
```

Fixed key order, each value at most 128 characters (the fixed-shape `bridge.fingerprint`, `<64 hex>:<64 hex>`, is the
one 129-character exception; it is validated by its own pattern instead of the general bound), the whole entry at
most 1024. `bridge.fingerprint`,
`bridge.sha256` and `lua.sha256` read `unavailable` when the bridge or the Lua module cannot be located or hashed;
`sdk.commit` reads `unknown` when no 40-hex commit can be parsed from the informational version. Nothing here is a
directory path, a drive root or a user name: `lua.module` and `ce.file` are file names only, and `hosting.alc` keeps
only the load context's kind and file token, never the isolated component's absolute path. Building the line calls no
Lua API and constructs no plugin; only the finished line is written to `HostLog`, once.

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
9. `MainThread.Invoke` from a worker keeps working, unopted-in, under the 2.0 conservative default; a worker calling
   `LuaRuntime.AcquireOperation()` directly is refused before the state provider runs (`MainThreadTests`).
10. An external Lua-state reset is logged once as `LuaStateReplacedExternally:` and does not survive into the next
    enable (`ExternalResetLifecycleTests`).
11. A throwing or re-entrant `HostLog` sink is contained during every native callback, and a second-factory
    rejection is logged outside the registration lock (`HostLogContainmentTests`).
12. The `CheatEngineSdkIdentification` diagnostic is silent unless opted in (programmatically or through the
    environment seam), is emitted at most once per enable attempt before the exports record is copied (so a later bind
    or construction failure does not suppress it), keeps a fixed key order within its 1024-character bound, never
    contains a directory separator, a drive root or the current user name, and calls no Lua API and constructs no
    plugin while it builds (`LoadIdentificationTests`).

## Run the tests

```powershell
dotnet test --project tests/CheatEngine.SDK.Hosting.Tests
```

Tests tagged `Category=NativeLua` run against the Lua DLL of Cheat Engine 7.7 kept in
[`native/cheat-engine`](../../native/cheat-engine/README.md), so nothing has to be installed. See
the [test project README](../../tests/CheatEngine.SDK.Hosting.Tests/README.md). To run a plugin inside Cheat Engine,
follow [Run it in Cheat Engine](../../tests/CheatEngine.SDK.LivePlugin/README.md#run-it-in-cheat-engine).
