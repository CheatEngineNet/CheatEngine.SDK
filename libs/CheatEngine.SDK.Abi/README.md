# CheatEngine.SDK.Abi

Blittable C# mapping of the binary interface between Cheat Engine 7.7 and a plugin.

## Objective

Describe the records that Cheat Engine and a plugin exchange, byte for byte, so `CheatEngine.SDK.Hosting` can read and
write them through pointers. The assembly holds layouts, constants and two ABI booleans. It references no other
CheatEngine.SDK assembly. Filling the records and reacting to them belongs to `CheatEngine.SDK.Hosting`.

## Why it exists

Cheat Engine reads and writes this memory directly. A wrong size or offset corrupts the Cheat Engine process, and no
other test in the solution would notice. Three details break a naive mapping.

- The init record is 36 bytes without tail padding. A mirror with natural alignment is 40 bytes, so `PluginInitRecord`
  is packed and a write touches exactly 36 bytes.
- The interface has two boolean widths, and any non-zero pattern counts as true. `Bool32` and `Bool8` compare
  truthiness, never raw bits.
- With runtime marshalling disabled, the compiler accepts `bool`, `char` and wrong calling conventions in function
  pointers. A test gate rejects them.

## How it works

Sizes are x64. On the managed path, Cheat Engine calls `CESDK.CESDK.CEPluginInitialize(IntPtr args, int size)`, and
`args` addresses a `PluginInitRecord`. Never rely on the value of `size`. On the classic path, a native DLL exports
three `CEPlugin_*` functions.

| Type                               | Namespace                     | Content                                                                                                     | Path    |
|------------------------------------|-------------------------------|-------------------------------------------------------------------------------------------------------------|---------|
| `PluginInitRecord`                 | `CheatEngine.SDK.Abi.Managed` | 36 bytes, packed: name, three lifecycle callbacks, version                                                  | Managed |
| `ManagedExportedFunctions`         | `CheatEngine.SDK.Abi.Managed` | 48 bytes: size, `GetLuaState`, `LuaRegister`, `LuaPushClassInstance`, `ProcessMessages`, `CheckSynchronize` | Managed |
| `ManagedEntryPoint`                | `CheatEngine.SDK.Abi.Managed` | Constants: `CESDK.CESDK`, `CEPluginInitialize`, results 1 and 0                                             | Managed |
| `PluginVersion`                    | `CheatEngine.SDK.Abi.Native`  | 16 bytes: version, plugin name                                                                              | Both    |
| `Bool32`                           | `CheatEngine.SDK.Abi`         | 4 bytes: Win32 `BOOL`, lifecycle results                                                                    | Both    |
| `Bool8`                            | `CheatEngine.SDK.Abi`         | 1 byte: Pascal `boolean`, the `CheckSynchronize` result                                                     | Managed |
| `AbiConstants`                     | `CheatEngine.SDK.Abi`         | `SdkVersion` = 6                                                                                            | Both    |
| `AbiArchitecture`                  | `CheatEngine.SDK.Abi`         | `IsSupported` and `ThrowIfUnsupported()`, x64 only                                                          | Both    |
| `PluginType`, `AutoAssemblerPhase` | `CheatEngine.SDK.Abi.Native`  | 4-byte enums                                                                                                | Classic |
| `*PluginInit` records              | `CheatEngine.SDK.Abi.Native`  | One registration record per `PluginType`                                                                    | Classic |
| `PluginType0Record`                | `CheatEngine.SDK.Abi.Native`  | Internal, 48-byte x64 C-header selection-record mirror                                                      | Classic |
| `RegisterModificationInfo`         | `CheatEngine.SDK.Abi.Native`  | Internal, dangerous 264-byte x64 register-change request                                                    | Classic |
| `ExportedFunctionsPrefix`          | `CheatEngine.SDK.Abi.Native`  | Internal, 144-byte x64 physical C-header prefix; stops before hook-bearing pointers                         | Classic |
| `NativeExportNames`                | `CheatEngine.SDK.Abi.Native`  | `CEPlugin_GetVersion`, `CEPlugin_InitializePlugin`, `CEPlugin_DisablePlugin`                                | Classic |

`CheatEngine.SDK.Hosting` loads plugins through the managed path. The classic records are layout definitions, checked by
the same size and offset tests. This assembly maps only the internal, physically contiguous C-header prefix of the
classic exported-functions table. It intentionally excludes the following pointer-indirect, hook-bearing suffix and
Delphi-object slots.

Some slots are `void*` on purpose. `LuaRegister` stays untyped so nobody calls it by accident. The type-0 and type-6
click callbacks, plugin types 3 and 4, the type-6 popup callback, `FixMem`, and `GetAddressFromPointer` are also
untyped. The historical C and Pascal declarations either conflict or establish that the slot can be null; preserving a
physical slot is not permission to call it.

## CE 7.7 evidence boundary

The installed x64 host baseline is Cheat Engine `7.7.0.10621`, executable SHA-256
`9727076DA50924E4A097B49A02155E4B34759269C3017FF31375364B8826EB4D`. The classic declarations used here come from
the installed `cepluginsdk.h` (SHA-256 `9C0E31BB753D782CE20710D19828F4E97B4371C8733ABD0C5C6F7F485306FB28`) and
`cepluginsdk.pas` (SHA-256 `CDA5269F441120E5A3BFF2F87E289CD71DE9158CA2A619C7D0A734EB98EE6052`), compared with the
official [pinned
`cepluginsdk.h`](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/plugin/cepluginsdk.h)
and [pinned
`plugin.pas`](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/plugin.pas).

These installed-file hashes were recorded in a source index that was retired on 2026-09-22 and is not restored; they
remain declarations (`DeclaredRepo`) until an exact-host measurement re-measures them. The independently
compiled fixture is deliberately more limited: it compiles a checked-in transcription of the pinned upstream C-header
subset under MSVC x64, validates 104 facts, and compares its `sizeof`, `offsetof`, alignment, export, and topology facts
with a versioned expectation. The Debug CI test run also passes that facts file into a compiled managed test, which
measures
the matching managed record sizes, offsets, and alignments directly. It is therefore a `compiled-transcription-fixture`
proof, not proof that a live CE host
loads a slot, uses a given Pascal boolean width, or provides a non-null table entry. The records stay internal until a
dedicated owning facade and exact-host canary exist.

The type-6 popup callback is a specifically unresolved contract. The historical C header declares `BOOL* show`, while
the pinned Pascal host implementation passes `PBool`. Its effective write width is `Unknown` until the required CE 7.7
x64 live canary verifies it, so the callback slot is an opaque pointer rather than a callable managed signature. Do not
write a one-byte flag, and do not invoke this callback slot from a production plugin, before that proof exists.

The `CheatEngine.SDK` package embeds the assembly and its XML documentation under `lib/net10.0`. It is not a package of
its own.

## Two tables, two routes

Cheat Engine hands a plugin one of two exported-function tables, depending on how the plugin was loaded:

| Route             | Entry point                                        | Table the plugin receives                                                                 |
|-------------------|----------------------------------------------------|-------------------------------------------------------------------------------------------|
| Managed (hostfxr) | `CESDK.CESDK.CEPluginInitialize(IntPtr, int)`      | `ManagedExportedFunctions`: 48 bytes, six fields, filled by Cheat Engine's managed loader |
| Classic native    | `CEPlugin_GetVersion`, `CEPlugin_InitializePlugin` | `TExportedFunctions5` of `plugin.pas`: 159 slots, 1272 bytes on x64                       |

The tables are distinct: the managed table is not a prefix of the classic one, and nothing converts one into the
other. A CheatEngine.SDK plugin loads through the managed route only, so it has **no route to the classic table**: the
classic records in this assembly (the internal 144-byte `ExportedFunctionsPrefix`, the classic dispatcher) are
layouts and source-contract code, never a way for a managed plugin to reach a classic slot.
`AbiRouteSeparationTests.Managed_exports_are_never_converted_to_the_classic_prefix` and
`AbiRouteSeparationTests.Only_the_classic_dispatcher_and_readers_consume_the_prefix`
(`tests/CheatEngine.SDK.Repository.Tests/Abi/`) enforce the separation in `libs/**`. The 159-slot classic table itself
— its authority, mirrors and divergences from the C and Pascal kits — is committed test data, never a top-level
`docs/` page: `tests/CheatEngine.SDK.Repository.Tests/Abi/TestData/classic-slot-registry.json`, read only by
`ClassicSlotRegistryDocumentTests` and `ClassicSlotRegistryPrefixTests`.

## The type-0 selection record oracle

The oracle for `PluginType0Record` is the host type actually passed: `TPlugin0_SelectedRecord` of the pinned
`plugin.pas` (lines 726-735, `address: ptrUint; ispointer: BOOL`), which the C header `PLUGINTYPE0_RECORD` agrees
with. The Pascal kit unit `cepluginsdk.pas` has two known-wrong mirrors. All three are 48 bytes, so only a per-field
check separates them (`SelectedRecordOracleTests`, registry divergence D09):

| Field                | Host type (oracle) | `cepluginsdk.pas` dword mirror (lines 161-170) | `cepluginsdk.pas` `TSelectedRecord` (lines 147-156) |
|----------------------|--------------------|------------------------------------------------|-----------------------------------------------------|
| `interpretedaddress` | 0, 8 bytes         | 0, 8 bytes                                     | 0, 8 bytes                                          |
| `address`            | 8, 8 bytes         | 8, **4 bytes**                                 | 8, 8 bytes                                          |
| `ispointer`          | 16, 4 bytes        | **12**, 4 bytes                                | 16, **1 byte**                                      |
| `countoffsets`       | 20, 4 bytes        | **16**, 4 bytes                                | 20, 4 bytes                                         |
| `offsets`            | 24, 8 bytes        | 24, 8 bytes                                    | 24, 8 bytes                                         |
| `description`        | 32, 8 bytes        | 32, 8 bytes                                    | 32, 8 bytes                                         |
| `valuetype`          | 40, 1 byte         | 40, 1 byte                                     | 40, 1 byte                                          |
| `size`               | 41, 1 byte         | 41, 1 byte                                     | 41, 1 byte                                          |

Which record the 7.7.0.10621 binary passes is `NotObserved`: no managed route reaches a type-0 registration, so the
type-0 callback stays `void*`.

## NativeAOT plugin profile (F02)

This section publishes the restrictions of audit finding F02 before any support claim: **a NativeAOT plugin DLL is
not a supported CheatEngine.SDK profile.** Unloading a Native AOT library with `FreeLibrary` or `dlclose` is not
supported by .NET (https://learn.microsoft.com/dotnet/core/deploying/native-aot/libraries, audit source EXT-01), and
Cheat Engine removes a plugin with `FreeLibrary`.

| Profile                                                                                                        | Status in CheatEngine.SDK 2.0                                          | Evidence and boundary                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
|----------------------------------------------------------------------------------------------------------------|------------------------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Historical CLR route: `MSCorEE.dll` hosting with a string entry point, as in the historical public C# template | Documentary only, **not supported**                                    | Described by the pinned public source (`ce-public-src-ec45d5f`), which is never qualifiable. The SDK does not downgrade to that bootstrap.                                                                                                                                                                                                                                                                                                                                       |
| **Managed hostfxr route**                                                                                      | **The only qualifiable profile**: `ce-7.7.0.10621-x64-managed-hostfxr` | Cheat Engine 7.7.0.10621 x64 (`cheatengine-x86_64.exe` SHA-256 `9727076da50924e4a097b49a02155e4b34759269c3017ff31375364b8826eb4d`), with a `ce.runtimeconfig.json` recorded as a local modification (`LocalModified`), never an installer baseline. The plugin is a framework-dependent folder; the SDK generates `int CESDK.CESDK.CEPluginInitialize(IntPtr args, int size)`, which fills the 36-byte packed `PluginInitRecord` and receives the 48-byte managed exports table. |
| NativeAOT plugin DLL                                                                                           | **Not supported**                                                      | Cheat Engine unloads plugins with `FreeLibrary`, which .NET does not support for NativeAOT libraries. No residence model exists in 2.0: a resident native adapter separate from an AOT component is a possible future architecture with its own contract and proof (audit A04-07, deferred), not a change of the existing profile. Q42 is recorded `NotApplicable` for this reason.                                                                                              |
| Classic native plugin exporting `CEPlugin_*`                                                                   | **Not provided by the SDK**                                            | The classic path receives the 159-slot classic table (the committed registry above); no managed route reaches it and the SDK has no classic facade. A package can never add native exports to a consumer: only `UnmanagedCallersOnly` methods of the published assembly become exports (https://learn.microsoft.com/dotnet/core/deploying/native-aot/interop#native-exports).                                                                                                    |
| x86 or ARM64 host                                                                                              | **Not supported**                                                      | `AbiArchitecture` accepts x64 only; the packaged target refuses other explicit `PlatformTarget` values with `CESDK9101`. x64 layout tests make no x86 or ARM64 promise.                                                                                                                                                                                                                                                                                                          |

**What "AOT-compatible libraries" means.** The shipping libraries set `IsAotCompatible=true`, and
`tests/CheatEngine.SDK.AotProbe` publishes the complete shipping graph as a standalone Native AOT executable. That
establishes one thing: the libraries pass the trimming and AOT analysis for that published graph (audit A23-F02-1). It
does not establish that Cheat Engine can load, host or unload an AOT artefact, and it is not a plugin profile. **A
NativeAOT publish success is never a Cheat Engine load success.**

**Why the NativeAOT plugin DLL route stays closed.**

1. **Unload.** Cheat Engine's native loader calls `FreeLibrary` when it removes a plugin (pinned `plugin.pas`), and
   .NET does not support unloading a NativeAOT library (EXT-01). Combining the two contracts is a deduction, not a
   proof that every AOT architecture is impossible; it is enough to refuse the route until a residence model is
   qualified.
2. **Table.** A classic native plugin receives the classic `ExportedFunctions` table, not the managed exports table
   the SDK is built on. The two layouts are unrelated (48 bytes against 1272), and nothing converts one into the
   other (ADR-02).
3. **No drop-in replacement.** Replacing the managed bootstrap with three `CEPlugin_*` exports is **not** a complete
   solution: it changes the load profile, the table the plugin receives, and the unload contract at once (audit
   A23-F02-3). `NativeExportNames` documents the names for completeness only, with this caveat.
4. **Coexistence.** Even a future residence model would have to repeat the two-plugin protocol (Q09, Q10): a solution
   to unloading does not isolate static state or Lua globals by itself.

| Diagnostic                                  | Where                                                                                                                                              | What it catches                                                                                                                                                                               |
|---------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `CESDK9102` (`analyzers/docs/CESDK9102.md`) | MSBuild warning of the packaged `build/CheatEngine.SDK.targets` (target `CheatEngineSdkWarnNativeAotPluginProfile`), direct package consumers only | A library (not `Exe`/`WinExe`) that sets `PublishAot=true`, with or without `NativeLib`. Suppressible with `NoWarn`.                                                                          |
| `CESDK0006` (`analyzers/docs/CESDK0006.md`) | Roslyn analyzer shipped in the package                                                                                                             | An `[UnmanagedCallersOnly]` method whose constant `EntryPoint` starts with `CEPlugin_`, including `NativeExportNames.*`. Unprefixed historical names are not flagged (documented limitation). |

**Evidence.** Q41 (NativeAOT publish and export inspection, C0/C2): C0 is `Passed` from the static contract tests
below — the loader harness refuses any `CEPlugin_*` export, any missing probe export and any unexpected export
(`tests/CheatEngine.SDK.NativeAotLoaderHarness`, unit tests in `tests/CheatEngine.SDK.NativeAotLoaderHarness.Tests`),
the probe exports exactly its two names, and `CESDK9102`/`CESDK0006` are tested; C2 stays `NotExecuted` until the
first green CI `aot` run is recorded. The harness maps the probe only in a short-lived process and never frees it
(`library.unload=not-attempted`). Q42 (removal of a NativeAOT plugin profile, C3/C4) is `NotApplicable`: the profile
is unsupported (EXT-01), so no Cheat Engine run is planned for it.

## Use

The enable callback receives a pointer to an exports record that Cheat Engine owns. Copy it during the call, honor its
size field and never keep the pointer. The project needs `AllowUnsafeBlocks`.

```csharp
using CheatEngine.SDK.Abi.Managed;

internal static unsafe class Exports
{
    public static bool TryCopy(ManagedExportedFunctions* exports, out ManagedExportedFunctions copy)
    {
        copy = default;
        if (exports is null || exports->SizeOfExportedFunctions < sizeof(ManagedExportedFunctions)) return false;

        copy = *exports;
        return true;
    }
}
```

Lifecycle callbacks return `Bool32`. The address of a method that returns `int` does not convert to the record's
function pointer. `PluginInitRecord.Name` must stay valid for the life of the process. The host calls the bootstrap
twice, so allocate the name once and reuse it.

Test a `Bool32` or `Bool8` with `IsTrue`, never with `RawValue == 1`. A `bool` converts implicitly to either type. The
reverse needs a cast or an `if`.

## SDK-004 migration boundary

The ABI structures remain physically compatible on x64, but four previously typed fields are intentionally now opaque
addresses: `AddressListPluginInit.Callback`, `DisassemblerContextPluginInit.Callback`,
`ExportedFunctionsPrefix.FixMemory`, and `ExportedFunctionsPrefix.GetAddressFromPointer`. Code that assigned or called
one of these fields must stop doing so. A replacement can be introduced only by an exact CE 7.7 host profile that
qualifies the signature, nullability, ownership, and invocation lifetime together.

## Classic debugger callback contract (SDK-018)

`DebugEventPluginInit.Callback` has the historical shape `stdcall int(void* DEBUG_EVENT)`. The native event pointer,
including every nested union it reaches, is borrowed only for that invocation. SDK-018 copies only the scalar event
code, process id, and thread id into `DebugEventObservation`; it does not publish the native pointer.

`DebugEventDecisionHandler` is synchronous and returns a `DebugEventDecision`, not a `Task`. A
`BoundedDebugEventObservationBuffer` is strictly telemetry: its reader never owns the callback result, and its
overflow policy can drop observations without changing the native disposition.

The current catalogued classic profile has no qualified `ContinueDebugEvent` entry point. Consequently the internal
dispatcher always returns zero, leaving continuation to Cheat Engine. A request for
`DebugEventDecision.PluginOwnsContinuation` is counted and rejected to that zero fallback. It can become a real
continuation path only after a live canary qualifies the suffix location, pointer-cell indirection, nullability,
calling convention, failure behavior, and exactly-once-before-return rule.

The dispatcher roots its static thunk and handler before registration, then closes admission, unregisters, drains
admitted callbacks, and finally frees the callback record. An unconfirmed unregister deliberately retains the root
for a retry. A callback cannot release itself, because classic host locking would otherwise risk a deadlock. This is
an internal, source-contract implementation; deterministic fixtures exercise it but do not qualify a live Cheat
Engine host profile.

## Promise

1. Every public struct has the x64 size and field offsets asserted with literal numbers. A struct without a row in the
   expected-size table fails the run.
2. Writing a `PluginInitRecord` through a pointer touches exactly 36 bytes, at aligned and unaligned addresses.
3. Every struct is sequential and blittable at any depth. Function pointers are unmanaged `Stdcall` and carry no `bool`,
   `char` or by-reference parameter.
4. Runtime marshalling is disabled, and every enum is 4 bytes wide.
5. `Bool32` and `Bool8` treat any non-zero pattern as true. They cross an unmanaged call like `int` and `byte`, even
   when the callee leaves garbage above the low byte of a `Bool8`.
6. `AbiArchitecture.IsSupported` is true for x64 only, and `ThrowIfUnsupported()` names the architecture it rejects.
7. `ManagedEntryPoint` agrees with the namespace, type name and method name that the entry point generator emits.

## Run the tests

```powershell
dotnet test --project tests/CheatEngine.SDK.Abi.Tests
```

Layout tests skip themselves in a 32-bit process. See [
`tests/CheatEngine.SDK.Abi.Tests/README.md`](../../tests/CheatEngine.SDK.Abi.Tests/README.md).
