# CheatEngine.SDK.LiveProbe.Tests

Deterministic unit tests for the manually loaded CE 7.7 [LiveProbe](../CheatEngine.SDK.LiveProbe/README.md) evidence
harness.

## Objective

Prove the fail-closed behaviour of the harness without a Cheat Engine host: the authorization gate is re-evaluated
before every acting command, the Checkpoint B hooks are inert without it, the fault switch selects exactly one stage,
and the status records report raw host facts without interpreting them.

## Why it exists

The harness is only ever run inside Cheat Engine, where a regression would surface as a wrong or missing qualification
record. These tests catch it in CI instead. They never start Cheat Engine, load a CE host, select a target, or produce
a qualification artifact: a green run here is C1 evidence about the harness, never host evidence.

## How it works

The project compiles the Lua-free sources of the harness directly (`Compile Include` links in the project file) and
supplies a local stub for the generated `ProbeHostGlobals.GetOpenedProcessId` Lua global, in the same `LiveProbe`
namespace, so it neither loads the plugin nor runs its Lua source generator. Tests inject the authorization decision,
CE's opened PID, the fault-switch file reader and the `PluginHost` facts. `AssemblyInfo.cs` disables parallelization
because the harness keeps process-wide static state.

| Test class                       | Covers                                                                                                                      |
|----------------------------------|-----------------------------------------------------------------------------------------------------------------------------|
| `LiveProbeStateTests`            | Fresh authorization and target-PID checks before host-profile capture and every protected command.                          |
| `LiveProbeStatusTests`           | Text and JSON status (plugin id, epoch, exports size, raw second bootstrap integer), the exception hook and the pump hook. |
| `LiveProbeFaultInjectionTests`   | The `liveprobe.fault.json` switch: never read without authorization, exact stage selection, ignored and reported failures. |
| `HostProfileObservationTests`    | Typed outcomes for missing, locked, vanishing or protected identity files.                                                  |

## Promise

- A capture or protected command after the manifest expired, the target image changed or CE selected another PID
  returns a fresh denial and never runs the probe (`LiveProbeStateTests`).
- The status reports the exports size and the raw second bootstrap integer without naming it a size, length or
  version
  (`LiveProbeStatusTests.Status_reports_the_exports_size_and_the_raw_second_bootstrap_integer_without_interpretation`).
- The exception and pump hooks do nothing without authorization, and the pump refuses an out-of-range duration before
  any host call (`LiveProbeStatusTests`).
- The fault switch is ignored without authorization, selects exactly the requested stage, and treats an absent file as
  no fault (`LiveProbeFaultInjectionTests`).
- Identity-file failures are reported as typed outcomes (`HostProfileObservationTests`).

## Run the tests

```powershell
dotnet test --project tests/CheatEngine.SDK.LiveProbe.Tests/CheatEngine.SDK.LiveProbe.Tests.csproj
```
