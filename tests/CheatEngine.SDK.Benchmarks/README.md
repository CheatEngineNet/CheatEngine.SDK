# CheatEngine.SDK.Benchmarks

BenchmarkDotNet console application that measures the calls a plugin makes into Lua, on a real Lua 5.3 state.

## Objective

Report the time and the managed allocations of the SDK's hot paths. They are marshaller push and read, a generated
global call, a generated callback, generated target-memory scalar calls, the EngineApi incremental-generator pipeline
and a `CheatEngine.SDK.Engine` property get. The `Allocated` column is the headline, not the mean.

## Why it exists

The SDK is built so that these paths allocate nothing on the managed side, except reading a `string`. This project shows
the same paths in a benchmark table, with time next to allocation. A change to the call machinery can then be judged on
both.

## How it works

| Class                            | Category                                      | Measures                                                                                                         |
|----------------------------------|-----------------------------------------------|------------------------------------------------------------------------------------------------------------------|
| `MarshallerBenchmarks`           | `Transition`, `Strings`                       | Push and read of the `Int32`, `Int64`, `Single`, `Double`, `Boolean`, `Address`, `Utf8` and `String` marshallers |
| `GlobalCallBenchmarks`           | `GlobalCall`                                  | A protected call of a Lua global with two arguments and one result                                               |
| `CallbackBenchmarks`             | `Callbacks`                                   | A Lua loop that calls a registered `[LuaFunction]` thunk, reported per call                                      |
| `Utf8MarshallerBenchmarks`       | `Transition`, `Utf8`                          | Push and borrowed-span read of valid non-ASCII UTF-8 at 16, 64 and 1,024 bytes                                   |
| `ObjectPropertyBenchmarks`       | `ObjectAccess`                                | `CEObject.TryGetProperty<Int32Marshaller, int>` on a fake host object                                            |
| `MemoryScalarBenchmarks`         | `EngineApi`, `TargetMemory`, `Fixture`        | Public `TargetMemory` and `HostMemory` signed 32/64-bit calls against isolated Lua table stand-ins               |
| `EngineApiIncrementalBenchmarks` | `SourceGenerator`, `Incremental`, `EngineApi` | Cold, cached and one-spec-edit EngineApi generator workloads; excludes compiler/MSBuild time                     |

`BenchGlobals` and `BenchFunctions` declare a real `[LuaGlobal]` and `[LuaFunction]`. The project references the
shipping `LuaBindings` generator as an analyzer, so the measured bodies are what a plugin gets and cannot drift from it.

`Support/FakeHostRuntime.cs` attaches `LuaRuntime` to one `NativeLuaState`. For the property benchmark it adds a
host-object pusher and a metatable that answers every property read with 42. The state pointer lives in one static
field, so one state attaches per process, and BenchmarkDotNet runs each benchmark in its own process. Every benchmark
restores the Lua stack top it started with, or the SDK call it measures does. `CallbackBenchmarks` uses one fixed loop
count, because `OperationsPerInvoke` must be a compile-time constant.

`NativeLuaState` and `NativeLuaLibrary` come from
[`tests/CheatEngine.SDK.Tests.Shared`](../CheatEngine.SDK.Tests.Shared/README.md). Each class closes
its Lua state in `[GlobalCleanup]`, and detaches the runtime where it attached one, because BenchmarkDotNet does not
call `Dispose`. The project references `CheatEngine.SDK.Annotations`, `CheatEngine.SDK.Lua.Interop`,
`CheatEngine.SDK.Lua` and `CheatEngine.SDK.Engine` directly, never `src/CheatEngine.SDK`. The package project embeds
its libraries with `PrivateAssets=all`, so their types do not
flow through a project reference. The build is x64 only, because it binds to a 64-bit Lua DLL through function pointers.
Every class carries `[MemoryDiagnoser(false)]`. It deliberately declares no timing job: the BenchmarkDotNet default job
is the reproducible baseline job, while an invocation may opt into `--job Short` for development feedback or `--job Dry`
to validate a new scenario. This keeps `Dry` genuinely dry instead of combining it with a class-level job. Allocation
values are evidence; deterministic allocation gates remain the correctness authority.

`MemoryScalarBenchmarks` is intentionally a fixture benchmark. It measures the SDK wrapper's state acquisition,
cached-global push, target-or-host address conversion, protected call, scalar conversion and stack restoration; the Lua
tables do not measure Cheat Engine's process-memory implementation. `EngineApiIncrementalBenchmarks` deliberately
invokes just the
incremental generator against two curated in-memory specs. It measures no compiler, MSBuild or filesystem work. The
versioned scenario identities, allocation expectations, result-recording recipe and API designs that are still deferred
live in [BaselineMetadata.md](BaselineMetadata.md).

## Promise

- It is not a test project and never packs: `eng/Tests.props` turns only `*.Tests` projects into test projects, and the
  root `Directory.Build.props` sets `IsPackable` to `false`.
- A missing or unusable Lua DLL stops setup with a reason instead of a native crash: every `[GlobalSetup]` calls
  `NativeLuaLibrary.ThrowIfUnavailable()`, and `NativeLuaProbeTests` in `CheatEngine.SDK.Lua.Interop.Tests` covers the
  lookup.
- Zero allocation does not depend on this project: `ZeroAllocationTests` in `CheatEngine.SDK.Lua.Tests` and
  `CheatEngine.SDK.Engine.Tests` assert the same paths. `PushReadString` is the one benchmark expected to allocate,
  because reading returns a new managed string. BenchmarkDotNet allocation values are evidence, not a replacement for
  those deterministic gates.
- Timing is intentionally not a CI threshold. Historical results are compared only when their recorded machine, .NET,
  fixture hash and BenchmarkDotNet job agree; see [BaselineMetadata.md](BaselineMetadata.md).

## Run it

Run in Release, because BenchmarkDotNet refuses a non-optimized build. The Lua DLL of Cheat Engine 7.7 kept in
[`native/cheat-engine`](../../native/cheat-engine/README.md) is used, or the DLL named by
`CHEATENGINE_SDK_LUA53_PATH` when it is set (no fallback then). Results land in
`BenchmarkDotNet.Artifacts/` under the working directory, which is git-ignored. To select one class, pass
`--filter "*MarshallerBenchmarks*"` instead of `--anyCategories`.

```powershell
dotnet run --project tests/CheatEngine.SDK.Benchmarks -c Release -- --anyCategories GlobalCall Callbacks --job Short

# Compile and execute a newly added scenario once, without collecting a meaningful timing result.
dotnet run --project tests/CheatEngine.SDK.Benchmarks -c Release -- --filter "*MemoryScalarBenchmarks*" --job Dry --noOverwrite
```
