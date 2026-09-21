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

The source index records the installed-file hashes reviewed for the historic CE 7.7 baseline. The independently
compiled fixture is deliberately more limited: it compiles a checked-in transcription of the pinned upstream C-header
subset under MSVC x64, validates 104 facts, and compares its `sizeof`, `offsetof`, alignment, export, and topology facts
with a versioned expectation. The native CI job also passes that facts file into a compiled managed test, which measures
the matching managed record sizes, offsets, and alignments directly. It is therefore a `compiled-transcription-fixture` proof, not proof that a live CE host
loads a slot, uses a given Pascal boolean width, or provides a non-null table entry. The records stay internal until a
dedicated owning facade and exact-host canary exist.

The type-6 popup callback is a specifically unresolved contract. The historical C header declares `BOOL* show`, while
the pinned Pascal host implementation passes `PBool`. Its effective write width is `Unknown` until the required CE 7.7
x64 live canary verifies it, so the callback slot is an opaque pointer rather than a callable managed signature. Do not
write a one-byte flag, and do not invoke this callback slot from a production plugin, before that proof exists.

The `CheatEngine.SDK` package embeds the assembly and its XML documentation under `lib/net10.0`. It is not a package of
its own.

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
