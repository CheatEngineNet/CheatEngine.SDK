# CheatEngine.SDK.LiveProbe

`CheatEngine.SDK.LiveProbe` is a manually loaded **evidence harness**, not a unit-test project, sample plugin, package
asset, or normal CI input. It records CE 7.7 behaviours that fixture tests cannot establish: the raw managed bootstrap
argument, the disputed packed-record tail, `synchronize`, per-thread Lua states and registry sharing, external
`resetLuaState`, CE userdata, callback cleanup on plugin disable, and the Checkpoint B hooks of the qualification
runner.

## Objective

Give a local qualification run and a human operator one
plugin that exposes, through Lua-console commands, the facts the exact-host (C3) qualification scenarios record: plugin
id and epoch (Q05), the reported exports-table size (Q03), the raw second bootstrap integer (Q04), a managed exception
inside a Lua callback (Q14), lifecycle faults on demand (Q06, Q08), a message pump during a callback (Q07), the
non-ASCII plugin name (Q05.a) and where the SDK assemblies were loaded from (Q40).

## Why it exists

Only a real Cheat Engine host can show these behaviours, and C1/C2 success is never host evidence. The harness keeps
every probe opt-in and fail-closed, so
loading it by mistake observes nothing and changes nothing. The solution compiles it, so a compile break is caught by
CI;
CI never loads or runs it.

## Safety boundary

The probe is deliberately inert unless all of these are true (the one exception is the two status commands below, which
only report process-local facts the probe already holds and act on nothing):

1. The process is x64 and its main executable is exactly CE `7.7.0.10621` x64, SHA-256
   `9727076DA50924E4A097B49A02155E4B34759269C3017FF31375364B8826EB4D`.
2. `CE_SDK_LIVE_PROBE_ACKNOWLEDGEMENT` exactly equals `I_AUTHORIZE_CE77_LIVE_PROBES_ON_A_DISPOSABLE_TARGET`.
3. `CE_SDK_LIVE_PROBE_AUTHORIZATION_FILE` names an unexpired JSON manifest whose schema is `ce77-live-probe-v1`, repeats
   that acknowledgement and host hash, identifies a live target PID, marks it `disposable: true`, and gives the SHA-256
   of that target's currently running image.
4. After enable, CE's `getOpenedProcessID()` is exactly that manifest PID.

The harness never opens or selects a process, changes target memory, injects, starts a debugger, or executes arbitrary
target code. The one intentionally risky observation is an operator-authorized four-byte canary immediately after the
SDK's conservative packed 36-byte bootstrap record. It runs only during bootstrap after conditions 1–3 pass. It is
isolated to the CE process, not the target, but must still be run only with a disposable test setup. A canary value that
survives **does not prove allocation capacity** on its own; preserve all raw observations for review.

The harness does not write a result file: it logs raw values through `HostLog`/`OutputDebugString` and returns them to
the Lua caller. The qualification runner's Lua driver records the returned values in a redacted event log; a manual
operator keeps the transcript outside the repository. Do not place installed CE binaries, target binaries, manifests
containing sensitive paths, or captured process memory in source control.

## How it works

| File                                                                                | Content                                                                                                                                                                                                             |
|-------------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `CE77LiveProbeBootstrap.cs`                                                         | The hand-written `CESDK.CESDK.CEPluginInitialize(nint, int)`. It records the second integer raw, then forwards to `PluginHost.InitializeManaged`. With `LIVEPROBE_NON_ASCII_NAME` it selects the non-ASCII factory. |
| `ProbePluginFactory.cs`, `ProbePluginFactoryNonAscii.cs`                            | The factories. `Create` first evaluates the fault switch. The non-ASCII factory exists only in the `-p:LiveProbeNonAsciiName=true` build.                                                                           |
| `Ce77LiveProbePlugin.cs`                                                            | `OnEnable` registers the console commands, re-checks the gates and applies the `OnEnable` fault; `OnDisable` unregisters them and applies the `OnDisable` fault.                                                    |
| `ProbeConsole.cs`, `ProbeHostGlobals.cs`                                            | The `[LuaFunction]` console commands below and the one `[LuaGlobal("getOpenedProcessID")]` binding.                                                                                                                 |
| `LiveProbeAuthorization.cs`, `AuthorizationDecision.cs`                             | The fail-closed gate: exact host hash and version, operator acknowledgement, unexpired manifest, live hash-verified disposable target.                                                                              |
| `LiveProbeState.cs`                                                                 | Process-local observations and the command implementations. Every command that acts re-evaluates the gate first.                                                                                                    |
| `LiveProbeFaultInjection.cs`, `LiveProbeFaultStage.cs`, `LiveProbeFaultDecision.cs` | The fault switch (see below). Lua-free, compile-linked into the tests.                                                                                                                                              |
| `LiveProbeHostFacts.cs`, `LiveProbeStatusSnapshot.cs`, `LiveProbeStatusReport.cs`   | The status record: `PluginHost` facts, gates, fault decisions and observations serialized as `ce77-live-probe-status-v1` JSON. Lua-free, compile-linked into the tests.                                             |
| `HostProfileObservation.cs`                                                         | The `ce77-live-host-profile-v1` identity record of host, Lua module, bridge, plugin and target files.                                                                                                               |

### Console commands

Every command returns a self-contained string. Commands that act (all except the two status commands) re-evaluate the
authorization manifest, the target image and CE's opened PID immediately before acting, and return
`Live probe denied: <reason>` otherwise.

| Command                                                                              | Observation                                                                                                                                                                                                                     | Used by                      |
|--------------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|------------------------------|
| `ce77_live_probe_status()`                                                           | Human-readable: bootstrap calls, the raw second integer (`opaqueSecondInt`, never labelled size or version), `PluginHost.LastInitRecordArgument`, phase, plugin id, epoch, reported exports size, gates, prior observations.    | operator                     |
| `ce77_live_probe_status_json()`                                                      | The same facts as one `ce77-live-probe-status-v1` JSON object, plus assembly locations, MVIDs, the Hosting load context and every fault-switch decision. Not gated: it reads process-local facts only.                          | Q03, Q04, Q05, Q06, Q08, Q40 |
| `ce77_live_probe_host_profile()`                                                     | One `ce77-live-host-profile-v1` JSON identity record for the authorized CE host, loaded Lua module, loaded bridge, plugin and disposable target.                                                                                | Q40                          |
| `ce77_live_probe_throw_managed_exception()`                                          | Throws `InvalidOperationException("CE 7.7 live probe deliberate managed exception (Q14).")` inside the generated thunk; call it under `pcall` and record the Lua error.                                                         | Q14                          |
| `ce77_live_probe_pump_messages(seconds)`                                             | Pumps CE's messages for 1–60 seconds from admitted main-thread work and returns a `ce77-live-probe-pump-v1` JSON record of the lifecycle phases seen. The operator unticks the plugin meanwhile. An observation, not a promise. | Q07                          |
| `ce77_live_probe_begin_synchronize()` then `ce77_live_probe_synchronize_status()`    | Worker, thunk and nested-invoke managed thread IDs; return round-trip and propagated exception.                                                                                                                                 | operator                     |
| `ce77_live_probe_begin_lua_threads()` then `ce77_live_probe_lua_threads_status()`    | GUI and worker `lua_State*` identities and a private raw-registry marker read by the worker. Do not execute other Lua for one second.                                                                                           | Q19                          |
| `ce77_live_probe_snapshot_before_reset()` / `ce77_live_probe_snapshot_after_reset()` | State pointer, SDK epoch and reference slot before and after an operator-run `resetLuaState()`; whether the old SDK reference still pushed. The harness never calls the reset.                                                  | Q17, Q18                     |
| `ce77_live_probe_userdata()`                                                         | `type(getMainForm())` and `tostring(getMainForm())`, without retaining the userdata or invoking the host-object pusher.                                                                                                         | operator                     |
| `ce77_live_probe_prepare_callback_shutdown()`                                        | Installs a counter callback and leaves it registered in `OnDisable`, so `LuaRuntime.Detach` must neutralize it. Call `pcall(ce77_live_probe_callback_shutdown)` before and after disabling.                                     | Q15                          |

### Fault switch (Q06, Q08)

A file named `liveprobe.fault.json` next to `CheatEngine.SDK.LiveProbe.dll` selects one lifecycle stage that throws:

```json
{ "schema": "ce77-live-probe-fault-v1", "throwIn": "OnEnable" }
```

`throwIn` is `None`, `FactoryCreate` (the factory throws before constructing the plugin; the enable fails and the next
enable tries again), `OnEnable` (after the console commands are registered) or `OnDisable` (after they are
unregistered). The switch is read once per enable, without Lua, and only when the authorization gate allows: without
the manifest the file is never opened. An unknown schema, an unknown stage or malformed JSON is ignored and reported.
Every
decision is logged and appears under `faultInjection` in `ce77_live_probe_status_json()`, with the list of stages that
actually threw. The runner writes and deletes the file in the bundle folder; it never lives in the repository.

### Non-ASCII name build (Q05.a)

`dotnet build tests/CheatEngine.SDK.LiveProbe/CheatEngine.SDK.LiveProbe.csproj -c Release -p:LiveProbeNonAsciiName=true`
defines `LIVEPROBE_NON_ASCII_NAME`, and the bootstrap then registers the name `CheatEngine.SDK Live Probe é 日本` (one
Latin-1 character and two characters outside code page 1252). It settles, by observation, whether Cheat Engine 7.7
decodes the name the SDK converts to the process ANSI code page ([
`AnsiNameBuffer`](../../libs/CheatEngine.SDK.Hosting/Bootstrap/AnsiNameBuffer.cs)). The default build keeps the ASCII
name `CheatEngine.SDK CE 7.7 Live Probe`. The switch writes to the same output folder, so rebuild without it afterwards.

## Build and load

A qualification run builds the harness from the exact CI package into a clean folder and drives it. For a manual
session:

```powershell
dotnet build tests/CheatEngine.SDK.LiveProbe/CheatEngine.SDK.LiveProbe.csproj -c Release
```

Keep the complete `artifacts/bin/CheatEngine.SDK.LiveProbe/release/` folder together when loading
`CheatEngine.SDK.LiveProbe.dll` in CE's plugin settings. It needs the SDK assemblies, `.deps.json`,
`.runtimeconfig.json` and `cheatengine-sdk-lua-bridge.dll` next to the plugin. Record the supported host and runtime
policy alongside the transcript; never edit an installed CE to run this harness.

Create a short-lived authorization file on a secure local volume. Substitute only the hash and PID of the disposable
program that the operator has deliberately launched and attached in CE:

```json
{
  "schema": "ce77-live-probe-v1",
  "acknowledgement": "I_AUTHORIZE_CE77_LIVE_PROBES_ON_A_DISPOSABLE_TARGET",
  "hostSha256": "9727076DA50924E4A097B49A02155E4B34759269C3017FF31375364B8826EB4D",
  "targetProcessId": 12345,
  "targetSha256": "UPPERCASE_SHA256_OF_THE_LIVE_DISPOSABLE_TARGET_IMAGE",
  "disposable": true,
  "expiresUtc": "2026-09-20T12:30:00Z"
}
```

Set both environment variables in the same process tree that starts CE. Check `ce77_live_probe_status()` immediately
after enabling. If it reports any denied gate, stop: none of the action commands should be used and no result is
evidence. Disable the plugin, close CE normally, delete the short-lived authorization manifest, and terminate only the
disposable target through its normal cleanup route. Do not force-unload assemblies or use CE's process-killing actions.

## Promise

- The harness is compiled by CI through `CheatEngine.SDK.slnx` as an x64 dynamic-loading plugin that is not a test
  module and never packs
  (`QualificationHarnessShapeTests.LiveProbe_is_in_the_solution_as_an_x64_dynamic_loading_plugin_that_never_packs`).
- A fresh authorization is required before every acting command, and a changed manifest, target image or CE target
  PID is refused (`LiveProbeStateTests`).
- The raw second bootstrap integer and the reported exports size are reported without interpretation
  (`LiveProbeStatusTests.Status_reports_the_exports_size_and_the_raw_second_bootstrap_integer_without_interpretation`).
- Without authorization the exception and pump hooks are inert, and the pump refuses a duration outside 1–60 seconds
  before any host call (`LiveProbeStatusTests`).
- The two status commands are deliberately not gated: they serialize process-local facts (bootstrap record, `PluginHost`
  state, gate results, fault decisions, assembly identities), touch no target and make no Lua call of their own; an
  absent context is
  reported as absent, never as zero values
  (`LiveProbeStatusTests.Status_without_an_enabled_context_reports_none_instead_of_zero_values`,
  `LiveProbeStatusTests.Status_json_reports_the_fault_switch_decision_and_the_assembly_identities`).
- The fault switch is never read without authorization, selects exactly the requested stage, and ignores and reports an
  absent, unreadable or unknown switch (`LiveProbeFaultInjectionTests`).
- Missing, locked or vanishing identity files are reported as typed outcomes, never as a crash
  (`HostProfileObservationTests`), and the bridge identity is the module the process loaded, never a file guessed next
  to the application
  (`HostProfileObservationTests.Host_profile_records_the_loaded_bridge_module_and_never_a_file_next_to_the_application`).
- No live test is invoked by `dotnet test`, normal CI, Release validation or packaging: the solution only compiles it,
  and no workflow references the runner
  (`LocalQualificationRunnerTests.No_workflow_references_the_local_qualification_runner`).

## Run the tests

```powershell
dotnet test --project tests/CheatEngine.SDK.LiveProbe.Tests/CheatEngine.SDK.LiveProbe.Tests.csproj
```

The tests compile the Lua-free sources of this harness directly (see
[`CheatEngine.SDK.LiveProbe.Tests`](../CheatEngine.SDK.LiveProbe.Tests/README.md)); a host run is the qualification
runner's job.

## Scope and limitations

- Every protected command re-evaluates the gate immediately before acting. This is a current-state check, not proof
  that CE did not select another target between observations; PID reuse by an identical executable is not
  distinguishable without an operator-supplied incarnation value, which the `ce77-live-probe-v1` manifest does not
  contain.
- The plugin does not implement the classic native plugin Type-6 popup callback. That callback belongs to the classic
  ABI and needs a separately compiled, header-pinned native probe after the CE 7.7 header/Pascal divergence has been
  resolved.
- The tail canary is a bounded experimental write, not a new ABI rule. The SDK production path continues to write only
  the packed 36-byte record.
- `synchronize` facts become evidence only when raw thread IDs, exception text, return value and re-entrancy outcome are
  captured from the pinned host. `inMainThread()` capture and disable-while-worker drain remain future controlled
  probes; this harness does not claim either result.
- This version observes an external `resetLuaState()` only. It must not be mistaken for the planned SDK-controlled
  reset/generation contract.
- The worker-and-registry observation is opt-in only. A distinct worker Lua pointer may be a coroutine sharing the main
  virtual machine, heap and registry, so it is not evidence of independent heaps or safe concurrent execution.
- The pump hook reports what happened while the operator acted; it does not promise how Cheat Engine delivers a
  disable during a callback.

Record every result and its evidence in the pull request or release notes that claim it.
