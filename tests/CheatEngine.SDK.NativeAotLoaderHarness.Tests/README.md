# CheatEngine.SDK.NativeAotLoaderHarness.Tests

Unit tests of the NativeAOT loader harness contract, scenario Q41 at level C0.

## Objective

Prove, without publishing anything, that the harness in
[`tests/CheatEngine.SDK.NativeAotLoaderHarness`](../CheatEngine.SDK.NativeAotLoaderHarness/README.md) reads a PE export
directory correctly and refuses every export surface other than the probe's: a classic `CEPlugin_*` plugin entry point,
a missing probe export, or any unexpected extra export.

## Why it exists

The CI `aot` job publishes the probe and runs the harness, but before this project its PE reader and its refusal rules
had no unit test and the job checked exit codes only. A harness that accepted anything would look exactly like a harness
that works. A NativeAOT publish that succeeds is never a Cheat Engine load success; see the
[NativeAOT profile](../../libs/CheatEngine.SDK.Abi/README.md).

## How it works

The harness and the probe are RID-specific NativeAOT projects, so there is no `ProjectReference`: their pure sources
(`PortableExecutableExportReader.cs`, `LibraryProbeContract.cs`, `NativeAotLibraryProbeExportNames.cs`,
`NativeAotLibraryProbeExports.cs`) are compiled in as links. The test bytes are the checked-in Lua protection bridge DLL
(a real PE32+ AMD64 image with four named exports), embedded as a resource and patched **in memory** by
`Support/BridgeImage.cs`: an export name overwritten in place (never longer than the original), the COFF `Machine` field
rewritten, the export data directory zeroed, or the byte array truncated. A patched image is only ever parsed; nothing
maps or loads it.

`LibraryProbeContract.AllowedRuntimeExports` is measured: a local `dotnet publish` of the probe (.NET SDK 10.0.401,
win-x64, Release) exports exactly the two probe names and `DotNetRuntimeDebugHeader`.

## Promise

- The reader returns the bridge's four export names and refuses a non-AMD64 image, an image without an export directory
  and truncated bytes (`PortableExecutableExportReaderTests`).
- The contract refuses a `CEPlugin_*` export, including one read from patched bytes, a missing probe export and any
  unexpected export, and accepts exactly the probe names plus the measured runtime export
  (`LibraryProbeContractTests`).
- The probe source declares exactly the two required entry points and no `CEPlugin_*` name
  (`NativeAotLibraryProbeExportSurfaceTests`).

These are C0 evidence of Q41. The C2 evidence is the CI `aot` job's publish and harness transcript, recorded in the
matrix only after a green run.

## Run the tests

```powershell
dotnet test --project tests/CheatEngine.SDK.NativeAotLoaderHarness.Tests
```
