# CE 7.7 native ABI fixture

This directory contains a small **Windows x64 C++ fixture**, not a Cheat Engine plugin and not a replacement SDK. It
compiles a minimal contract derived from the CE classic-plugin header, builds a DLL with the three classic plugin
exports, loads that DLL back through `GetProcAddress`, and emits stable `key=value` ABI facts.

## Evidence boundary

The source is deliberately pinned to the upstream revision
[`ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37`](https://github.com/cheat-engine/cheat-engine/tree/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37):
[`Cheat Engine/plugin/cepluginsdk.h`](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/plugin/cepluginsdk.h).
`ce77_plugin_abi_contract.h` transcribes only the measured declaration subset, with source line ranges in its header;
it does not vendor the upstream header or any Cheat Engine binary.

This fixture proves C-header shape, not the behavior of a live CE host. In particular, the popup callback retains the
header's four-byte `BOOL* show` declaration. The planned CE 7.7 live canary is still required before a Pascal-side
one-byte representation can replace it in a runtime contract.

## What it checks

- x64 pointer, `BOOL`, `UINT_PTR` and enum widths;
- `sizeof` and `offsetof` facts for `PluginVersion`, `PLUGINTYPE0_RECORD`, the nine init records,
  `REGISTERMODIFICATIONINFO`, and the safe direct-call prefix of `ExportedFunctions`;
- explicit `__stdcall` callback and classic export signatures at compile time;
- exactly `CEPlugin_GetVersion`, `CEPlugin_InitializePlugin`, and `CEPlugin_DisablePlugin` exported from the fixture
  DLL;
- guard bytes around the version record and its padding, plus calls to all three exports.

It intentionally stops before the pointer-to-pointer hook suffix of `ExportedFunctions` and does not model Delphi
references, Lua, CE object ownership, or any live-process behavior.

## Run it

Use an x64 Visual Studio Developer PowerShell; no Cheat Engine installation is read or required.

```powershell
./tests/native-abi-fixture/build.ps1 -OutputDirectory ./artifacts/native-abi-fixture
```

The script writes only the selected output directory and prints lines such as:

```text
fixture.schema=1
source.upstream_commit=ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37
architecture=win-x64
sizeof.plugin_version=16
offsetof.plugin_type0_record.value_type=40
sizeof.register_modification_info=264
sizeof.exported_functions_prefix=144
calling_convention.classic_callbacks=__stdcall
export.0=CEPlugin_GetVersion
sentinel.plugin_version.outer_guard=passed
```

Normal managed CI does not invoke this script, so it never depends on a local CE installation or a C++ compiler.
The ordinary ABI tests remain pure .NET tests in `tests/CheatEngine.SDK.Abi.Tests`.
