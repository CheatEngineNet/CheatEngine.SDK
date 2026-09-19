# CESDK.Abi

Blittable C# mapping of the binary interface between Cheat Engine 7.7 and a plugin.

## Objective

Describe the records that Cheat Engine and a plugin exchange, byte for byte, so `CESDK.Hosting` can read and write them
through pointers. The assembly holds layouts, constants and two ABI booleans. It references no other CESDK assembly.
Filling the records and reacting to them belongs to `CESDK.Hosting`.

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

| Type                               | Namespace           | Content                                                                                                     | Path    |
|------------------------------------|---------------------|-------------------------------------------------------------------------------------------------------------|---------|
| `PluginInitRecord`                 | `CESDK.Abi.Managed` | 36 bytes, packed: name, three lifecycle callbacks, version                                                  | Managed |
| `ManagedExportedFunctions`         | `CESDK.Abi.Managed` | 48 bytes: size, `GetLuaState`, `LuaRegister`, `LuaPushClassInstance`, `ProcessMessages`, `CheckSynchronize` | Managed |
| `ManagedEntryPoint`                | `CESDK.Abi.Managed` | Constants: `CESDK.CESDK`, `CEPluginInitialize`, results 1 and 0                                             | Managed |
| `PluginVersion`                    | `CESDK.Abi.Native`  | 16 bytes: version, plugin name                                                                              | Both    |
| `Bool32`                           | `CESDK.Abi`         | 4 bytes: Win32 `BOOL`, lifecycle results                                                                    | Both    |
| `Bool8`                            | `CESDK.Abi`         | 1 byte: Pascal `boolean`, the `CheckSynchronize` result                                                     | Managed |
| `AbiConstants`                     | `CESDK.Abi`         | `SdkVersion` = 6                                                                                            | Both    |
| `AbiArchitecture`                  | `CESDK.Abi`         | `IsSupported` and `ThrowIfUnsupported()`, x64 only                                                          | Both    |
| `PluginType`, `AutoAssemblerPhase` | `CESDK.Abi.Native`  | 4-byte enums                                                                                                | Classic |
| `*PluginInit` records              | `CESDK.Abi.Native`  | One registration record per `PluginType`                                                                    | Classic |
| `NativeExportNames`                | `CESDK.Abi.Native`  | `CEPlugin_GetVersion`, `CEPlugin_InitializePlugin`, `CEPlugin_DisablePlugin`                                | Classic |

`CESDK.Hosting` loads plugins through the managed path. The classic records are layout definitions, checked by the same
size and offset tests. This assembly does not map the classic exported-functions table.

Some slots are `void*` on purpose. `LuaRegister` stays untyped so nobody calls it by accident. For plugin types 3 and 4,
`cepluginsdk.h` and `cepluginsdk.pas` disagree on the callback signature, so those slots are untyped too. The record
passed to the type 0 callback is `void*` for the same reason.

The `CESDK` package embeds the assembly and its XML documentation under `lib/net10.0`. It is not a package of its own.

## Use

The enable callback receives a pointer to an exports record that Cheat Engine owns. Copy it during the call, honor its
size field and never keep the pointer. The project needs `AllowUnsafeBlocks`.

```csharp
using CESDK.Abi.Managed;

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
dotnet test --project tests/CESDK.Abi.Tests
```

Layout tests skip themselves in a 32-bit process. See [
`tests/CESDK.Abi.Tests/README.md`](../../tests/CESDK.Abi.Tests/README.md).
