# CE 7.7 native ABI fixture

This directory contains a small **Windows x64 C++ fixture**, not a Cheat Engine plugin and not a replacement SDK. It
compiles a deliberately local transcription of a minimal CE classic-plugin-header subset, builds a DLL with the three
classic plugin exports, loads that DLL back through `GetProcAddress`, and emits 104 stable `key=value` ABI facts.

## Evidence boundary

The source is deliberately pinned to the upstream revision
[
`ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37`](https://github.com/cheat-engine/cheat-engine/tree/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37):
[
`Cheat Engine/plugin/cepluginsdk.h`](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/plugin/cepluginsdk.h).
`ce77_plugin_abi_contract.h` transcribes only the measured declaration subset, with source line ranges in its header;
it does not vendor the upstream header or any Cheat Engine binary.

This fixture proves the MSVC x64 shape of the checked-in transcription, not the behavior of a live CE host and not the
contents of an installed `cepluginsdk.h`. In particular, the popup callback retains the header's four-byte `BOOL* show`
declaration. The planned CE 7.7 live canary is still required before a Pascal-side one-byte representation can replace
it in a runtime contract.

## What it checks

- x64 pointer, `BOOL`, `UINT_PTR` and enum widths;
- `sizeof`, `offsetof`, and `alignof` facts for `PluginVersion`, `PLUGINTYPE0_RECORD`, the nine init records,
  `REGISTERMODIFICATIONINFO`, and the physically contiguous prefix of `ExportedFunctions`;
- a CI-only comparison of those emitted layout facts with the `sizeof`, address-of offset, and alignment measurements
  from the compiled managed ABI records;
- explicit `__stdcall` callback and classic export signatures at compile time;
- exactly `CEPlugin_GetVersion`, `CEPlugin_InitializePlugin`, and `CEPlugin_DisablePlugin` exported from the fixture
  DLL;
- guard bytes around the version record and its padding, plus calls to all three exports;
- a synthetic table topology with a direct address, borrowed process-id and process-handle cells, a null `FixMem`
  slot, an intentionally uninvoked conflicting `GetAddressFromPointer` address, and an excluded hookable suffix.

It intentionally stops before the pointer-to-pointer hook suffix of `ExportedFunctions` and does not model Delphi
references, Lua, CE object ownership, buffer capacities, or any live-process behavior. It never invokes a synthetic
`GetAddressFromPointer` pointer merely to infer its conflicting return width.

## Run it

The script imports an x64 Visual Studio C++ developer environment when necessary. It needs the installed Visual Studio
x64 C++ build tools, but reads no Cheat Engine installation.

```powershell
./tests/native-abi-fixture/build.ps1 -OutputDirectory ./artifacts/native-abi-fixture
```

The script writes only the selected output directory and prints lines such as:

```text
fixture.schema=2
source.upstream_commit=ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37
source.contract=transcribed-pinned-header-subset
architecture=win-x64
sizeof.plugin_version=16
alignof.plugin_version=8
offsetof.plugin_type0_record.ValueType=40
sizeof.register_modification_info=264
sizeof.exported_functions_prefix=144
calling_convention.classic_callbacks=__stdcall
export.0=CEPlugin_GetVersion
sentinel.plugin_version.outer_guard=passed
```

The script writes `ce77-native-abi-facts.txt` and validates every emitted key and value with
`Validate-Facts.ps1`. CI invokes this fixture in the native job and publishes the facts as the
`classic-abi-fixture-facts` artifact; the Debug build-test job passes that path to the compiled managed ABI test with
`CE77_NATIVE_ABI_REQUIRED=true`; that required mode fails the comparison gate if the path is absent. Ordinary
ABI tests remain pure .NET tests in `tests/CheatEngine.SDK.Abi.Tests`: their local opt-out does not require a C++
compiler or the facts file.
