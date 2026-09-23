# CheatEngine.SDK.NativeAotLibraryProbe

An inert `win-x64` NativeAOT shared library, the fixture of the NativeAOT loader harness (scenario Q41).

## Objective

Give the [loader harness](../CheatEngine.SDK.NativeAotLoaderHarness/README.md) a real NativeAOT shared library whose
export surface is known exactly: two deliberately non-Cheat-Engine names and nothing that looks like a plugin.

## Why it exists

A NativeAOT publish that succeeds proves nothing about Cheat Engine: Cheat Engine unloads plugins with `FreeLibrary`,
which .NET does not support for NativeAOT libraries, so a NativeAOT plugin DLL is not a supported profile (see the
[NativeAOT plugin profile](../../libs/CheatEngine.SDK.Abi/README.md#nativeaot-plugin-profile-f02)). What can be
checked is what such a publish exports. This probe is the controlled input of that check. It is not a plugin, has no
`CEPlugin_*` export and does not reference the SDK shipping graph.

## How it works

`NativeAotLibraryProbeExports` declares two `[UnmanagedCallersOnly]` entry points, named by
`NativeAotLibraryProbeExportNames.Required`: `CheatEngineSdkNativeAotProbe_LoadOnly` and
`CheatEngineSdkNativeAotProbe_NameQuery`. A NativeAOT publish exports only the `UnmanagedCallersOnly` methods of the
published assembly, plus the runtime's own `DotNetRuntimeDebugHeader`. The harness may map the published file and query
the two names; it never calls them and never frees the module.

## Promise

- The source declares exactly the two required entry points and no `CEPlugin_*` name
  (`NativeAotLibraryProbeExportSurfaceTests` in
  [`tests/CheatEngine.SDK.NativeAotLoaderHarness.Tests`](../CheatEngine.SDK.NativeAotLoaderHarness.Tests/README.md)).
- A local publish (.NET SDK 10.0.401, win-x64, Release) exports exactly those two names and `DotNetRuntimeDebugHeader`;
  the harness refuses any other surface.

## Run the tests

The probe has no tests of its own. Publish it only for the library-analysis gate:

```powershell
dotnet publish tests/CheatEngine.SDK.NativeAotLibraryProbe/CheatEngine.SDK.NativeAotLibraryProbe.csproj -c Release -o artifacts/nativeaot-library-probe
dotnet test --project tests/CheatEngine.SDK.NativeAotLoaderHarness.Tests
```
