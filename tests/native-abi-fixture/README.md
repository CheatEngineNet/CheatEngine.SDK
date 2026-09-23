# CE 7.7 native ABI fixture

This directory contains a small **Windows x64 C++ fixture**, not a Cheat Engine plugin and not a replacement SDK. It
compiles a deliberately local transcription of the classic- and managed-plugin records, builds a DLL with the three
classic plugin exports, loads that DLL back through `GetProcAddress`, and emits 318 stable `key=value` ABI facts
(`fixture.schema=3`).

## Evidence boundary

The sources are deliberately pinned to the upstream revision
[`ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37`](https://github.com/cheat-engine/cheat-engine/tree/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37).
`ce77_plugin_abi_contract.h` transcribes only the measured declarations, with the C# field names of
`CheatEngine.SDK.Abi`. It records names, types, line ranges and SHA-256 only; it does not vendor any upstream file and
no Cheat Engine binary or installation is read.

| Source (under `Cheat Engine/`) | SHA-256 | Transcribed declarations |
|---|---|---|
| `plugin/cepluginsdk.h` | `b6500df1e94d7bb011b38e173b2603197b7a1f304496d751ede82e57e36e532f` | lines 15-160, 163-180 and 271-456: `PluginVersion`, `PLUGINTYPE0_RECORD`, the nine init records, `REGISTERMODIFICATIONINFO`, the 144-byte physical prefix of `ExportedFunctions`, the three exports |
| `plugin.pas` (host authority) | `358f51a39ad14d00ecba3c9137f440152d4ab85f1d2498068fa81fca906d09db` | `TPluginDotNetInitResult` (packed record, lines 29-36) as `managed_plugin_init_record`; `TExportedFunctionsDotNetV1` (lines 38-45) as `managed_exported_functions`; `TPlugin0_SelectedRecord` (lines 726-735) as `host_plugin0_selected_record` |
| `plugin/cepluginsdk.pas` (kit mirror, negative oracle) | `cda5269f441120e5a3bff2f87e289cd71de9158ca2a619c7d0a734eb98ee6052` | `TPlugin0_SelectedRecord` (lines 161-170, `address: dword`) as `pascal_dword_mirror_selected_record`; `TSelectedRecord` (lines 147-156, `ispointer: boolean`) as `pascal_boolean_mirror_selected_record`; natural alignment deduced from `{$MODE Delphi}` (line 3) |

This fixture proves the MSVC x64 shape of the checked-in transcription, not the behavior of a live CE host and not the
contents of an installed file. The two Pascal mirrors are transcribed only so that the managed oracle can prove the
SDK record differs from them (`SelectedRecordOracleTests`); they are known-wrong for x64. The popup callback retains
the header's four-byte `BOOL* show` declaration; a CE 7.7 live canary is still required before a Pascal-side one-byte
representation can replace it in a runtime contract.

## What it checks

- x64 pointer, `BOOL`, `UINT_PTR` and enum widths;
- `sizeof` and `alignof` of every transcribed record, and `offsetof` and field width (`fieldsize.<record>.<field>`) of
  **every** transcribed field;
- the packed managed bootstrap record: 36 bytes with byte alignment, and a by-value copy into a buffer at an odd address
  leaves every guard byte intact (`sentinel.managed_plugin_init_record.tail_guard=passed`); a negative self-test copies
  the naturally aligned 40-byte mirror the same way and the probe fails unless that overrun is detected;
- a CI-only comparison of those facts with the managed measurements (`sizeof`, alignment, and every field's offset and
  width) in `tests/CheatEngine.SDK.Abi.Tests/Fixture/NativeAbiFixtureManagedComparisonTests.cs`, and of the host and
  mirror selection records with the SDK record in `tests/CheatEngine.SDK.Abi.Tests/Native/SelectedRecordOracleTests.cs`;
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
x64 C++ build tools, but reads no Cheat Engine installation. A relative `-OutputDirectory` is resolved against the
PowerShell location.

```powershell
./tests/native-abi-fixture/build.ps1 -OutputDirectory ./artifacts/native-abi-fixture
```

The script writes only the selected output directory and prints lines such as:

```text
fixture.schema=3
source.upstream_commit=ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37
source.contract=transcribed-pinned-header-and-host-pascal-subset
architecture=win-x64
sizeof.plugin_version=16
alignof.plugin_version=8
offsetof.plugin_type0_record.ValueType=40
fieldsize.plugin_type0_record.IsPointer=4
sizeof.managed_plugin_init_record=36
alignof.managed_plugin_init_record=1
fieldsize.pascal_boolean_mirror_selected_record.IsPointer=1
calling_convention.classic_callbacks=__stdcall
export.0=CEPlugin_GetVersion
sentinel.managed_plugin_init_record.tail_guard=passed
```

The script writes `ce77-native-abi-facts.txt` and validates every emitted key and value with `Validate-Facts.ps1`, an
exact-set validator: a missing, extra or different fact fails. CI invokes this fixture in the native job and publishes
the facts as the `classic-abi-fixture-facts` artifact; the Debug build-test job passes that path to the compiled managed
ABI tests with `CE77_NATIVE_ABI_REQUIRED=true`; that required mode fails the comparison gate if the path is absent.
Ordinary ABI tests remain pure .NET tests in `tests/CheatEngine.SDK.Abi.Tests`: their local opt-out does not require a
C++ compiler or the facts file.

Changing an ABI record means changing `ce77_plugin_abi_contract.h`, `ce77_native_abi_probe.cpp`, `Validate-Facts.ps1`
and the managed expectations (`tests/CheatEngine.SDK.Abi.Tests/Support/FieldLayoutExpectations.cs`) in the same commit.
