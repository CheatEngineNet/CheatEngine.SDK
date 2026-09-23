# CheatEngine.SDK.NativeAotLoaderHarness

A managed executable that inspects, and on explicit request maps, the NativeAOT library probe (scenario Q41). It is not
a test runner for Cheat Engine plugins and never starts or attaches to Cheat Engine.

## Objective

Prove that a NativeAOT publish of the [library probe](../CheatEngine.SDK.NativeAotLibraryProbe/README.md) has exactly
the export surface it is expected to have, and that such a file can be mapped and queried by name without any code of
it being called.

## Why it exists

A NativeAOT plugin DLL is not a supported CheatEngine.SDK profile: Cheat Engine unloads plugins with `FreeLibrary`,
which .NET does not support for NativeAOT libraries (see the
[NativeAOT plugin profile](../../libs/CheatEngine.SDK.Abi/README.md#nativeaot-plugin-profile-f02)). The CI `aot` job
still publishes the probe, so that a publish regression or an unexpected export is seen. Before this harness checked
the surface exactly, it would also have accepted a DLL with a classic `CEPlugin_*` entry point.

## How it works

The harness has exactly two modes:

- `--analyze <probe.dll>` reads the PE bytes with `PEReader`. It verifies the PE32+ AMD64 image and the export names
  without mapping the DLL.
- `--load --acknowledge-process-resident-load` has no file-path argument. It derives the fixed fixture file name from
  its own published directory, locks the file against replacement, then maps it in the short-lived harness process. It
  queries the two names only; it neither invokes an export nor frees the module.

Both modes first require the exact export surface of `LibraryProbeContract`: no `CEPlugin_` export (a classic Cheat
Engine plugin entry point), both probe exports, and nothing else except the NativeAOT runtime's own
`DotNetRuntimeDebugHeader`, the one extra export a local `dotnet publish` of the probe produced. Any drift exits
non-zero
with `harness.error=` naming every violation; a conforming file prints `export.runtime.*`, `export.unexpected=none` and
`contract=passed`. The load mode therefore refuses any file that has a `CEPlugin_` export, and it cannot be pointed at
an arbitrary path or a production plugin: Windows loader initialization can run while a DLL is mapped, even when no
export is invoked.

`library.unload=not-attempted` is an intentional result. NativeAOT shared libraries do not support `FreeLibrary` or
`dlclose` unloading, so process termination is the boundary of this observation.

## Promise

- The contract and the PE reader are unit-tested without publishing anything ([
  `tests/CheatEngine.SDK.NativeAotLoaderHarness.Tests`](../CheatEngine.SDK.NativeAotLoaderHarness.Tests/README.md),
  Q41 at level C0): a `CEPlugin_*` export, a missing probe export and any unexpected export are refused.
- The CI `aot` job's publish and harness transcript are the C2 evidence of Q41, recorded only after a green run.

## Run the tests

Publish both components into the same new output directory, then run the published harness:

```powershell
dotnet publish tests/CheatEngine.SDK.NativeAotLibraryProbe/CheatEngine.SDK.NativeAotLibraryProbe.csproj -c Release -o artifacts/nativeaot-loader-profile
dotnet publish tests/CheatEngine.SDK.NativeAotLoaderHarness/CheatEngine.SDK.NativeAotLoaderHarness.csproj -c Release -o artifacts/nativeaot-loader-profile

artifacts/nativeaot-loader-profile/CheatEngine.SDK.NativeAotLoaderHarness.exe --analyze artifacts/nativeaot-loader-profile/CheatEngine.SDK.NativeAotLibraryProbe.dll
artifacts/nativeaot-loader-profile/CheatEngine.SDK.NativeAotLoaderHarness.exe --load --acknowledge-process-resident-load
dotnet test --project tests/CheatEngine.SDK.NativeAotLoaderHarness.Tests
```
