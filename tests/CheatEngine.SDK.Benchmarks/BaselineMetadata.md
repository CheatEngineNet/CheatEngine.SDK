# Benchmark baseline metadata

This checked-in metadata is the baseline for **what is measured**, not a cross-machine time budget. BenchmarkDotNet
numbers depend on the CPU, Windows build, .NET runtime, power policy and installed Cheat Engine fixture; PRs must not
fail on a mean-time threshold. Every benchmark uses `MemoryDiagnoser`, so the `Allocated` column accompanies time and
the established unit-test allocation gates remain the correctness authority.

## Baseline schema 1

| Identifier | Workload and comparison | Fixture / required state | Allocation expectation | Status |
|---|---|---|---|---|
| `lua-callback-stateful-v1` | `CallbackBenchmarks.RoundTrip`: one protected Lua loop containing 1,000 chained calls to a registered generated thunk, reported per callback | Pinned CE 7.7 Lua 5.3 DLL; one attached ambient state | Zero after warm-up | Active |
| `lua-utf8-scale-v1` | `Utf8MarshallerBenchmarks.PushReadUtf8`: valid non-ASCII UTF-8 at 16, 64 and 1,024 bytes | Pinned CE 7.7 Lua 5.3 DLL; independent state per parameter | Zero after warm-up | Active |
| `target-scalars-fixture-v1` | `MemoryScalarBenchmarks`: public `TargetMemory` signed 32/64-bit read/write paths | Pinned CE 7.7 Lua 5.3 DLL; Lua table stand-ins, never a live target process | Zero after warm-up | Active |
| `host-scalars-fixture-v1` | `MemoryScalarBenchmarks`: public `HostMemory` signed 32/64-bit read/write paths | Pinned CE 7.7 Lua 5.3 DLL; separate Lua table stand-ins, never the Cheat Engine host process | Zero after warm-up | Active |
| `engineapi-incremental-v1` | `EngineApiIncrementalBenchmarks`: cold two-spec generation, unchanged re-run, and one-spec replacement | In-process Roslyn EngineApi generator; no compilation or MSBuild work | Informational; generator allocations are expected | Active |

`MarshallerBenchmarks`, `GlobalCallBenchmarks` and `ObjectPropertyBenchmarks` predate this schema and remain active
coverage. `PushReadString` is intentionally excluded from the zero-allocation expectation because reading makes a
managed `string`.

## Recording a comparable result

Record the SDK commit, clean/dirty status, .NET SDK/runtime, Windows version, CPU model, power policy, the SHA-256 of
the Lua fixture, exact command and the emitted full JSON report. Store a reviewed result only with the change whose
comparison it explains; never replace an unrelated prior measurement. Use separate artifact paths and
`--noOverwrite`.

```powershell
dotnet run --project tests/CheatEngine.SDK.Benchmarks -c Release --no-build -- `
  --filter "*MemoryScalarBenchmarks*" --job Short --memory --exporters fulljson `
  --artifacts BenchmarkDotNet.Artifacts/target-scalars --noOverwrite
```

First validate a newly added benchmark with `--job Dry`. A clean baseline run uses the BenchmarkDotNet default job;
use `--job Short` only for development feedback. Do not compare results obtained with different jobs as if they were
one series.

## Deliberately deferred workload designs

| Domain | Why no executable benchmark exists yet | Add when the contract is available |
|---|---|---|
| Scalar widths beyond signed 32/64-bit | The public target and host APIs exist, but this baseline deliberately avoids multiplying benchmark cases | One representative signed/unsigned/float/pointer operation per return shape, with CE-return semantics and fixture plus opt-in live validation |
| Byte and string memory I/O | Public span/string APIs exist, but their bulk and encoding caller shapes need a dedicated scenario | Small and page-scale buffers, distinct binary and encoding cases, and explicit batch-order semantics |
| Live Cheat Engine target-memory calls | Normal CI must not attach to or mutate a process | Opt-in CE 7.7 x64 test against an authorized disposable target, recorded separately from fixture numbers |
