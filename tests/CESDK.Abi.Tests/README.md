# CESDK.Abi.Tests

Tests that pin the memory layout of `CESDK.Abi` to the Cheat Engine plugin ABI, with no Cheat Engine and no Lua DLL.

## Objective

Prove that every struct, enum and constant of `CESDK.Abi` has the documented x64 size, offset and value. Prove that the
assembly stays blittable and `Stdcall` only.

## Why it exists

A wrong size or offset corrupts memory inside the Cheat Engine process. Nothing else in the solution notices, because
the compiler accepts any layout. The suite pins the documented numbers. Only [
`tests/CESDK.LivePlugin`](../CESDK.LivePlugin/README.md#run-it-in-cheat-engine) runs against Cheat Engine itself.

## How it works

Three independent techniques check the layout, so one mistake cannot hide behind another. `Managed/` and `Native/`
mirror the `CESDK.Abi` namespaces. `Support/` holds `Layout`, the `AbiShape` gate and its tests. Expected numbers are
literals next to the assertion, never derived from the code under test.

| Technique             | What it does                                                                                                                                                                           |
|-----------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Address-of arithmetic | `Layout.SizeOf<T>()` and `Layout.OffsetOf` measure the size and every field offset.                                                                                                    |
| Raw bytes             | The packed 36-byte init record is written into a guard-filled buffer at an aligned and an odd address. The 48-byte exports record is built as raw bytes, then read through the struct. |
| Host simulation       | `&Method` of a real `[UnmanagedCallersOnly]` `Stdcall` function is stored in every typed function-pointer slot, then called through the field.                                         |

The test assembly applies `[assembly: DisableRuntimeMarshalling]`, so calls take the path a plugin takes.
`BoolCallBoundaryTests` calls through pointers whose signature differs by one substitution (`int` for `Bool32`, `byte`
for `Bool8`). Every branch of `AbiArchitecture` is tested through its internal overloads.

- `AssemblyConformanceTests` compares an expected-size table with the public structs in both directions, then runs the
  `AbiShape` reflection gate over every struct.
- The gate rejects `bool`, `char`, references, foreign value types, managed or non-`Stdcall` function pointers and
  by-reference parameters at any depth. It exists because `delegate* unmanaged[Cdecl]<bool, char>` compiles without a
  diagnostic under `DisableRuntimeMarshalling`.
- `AbiShapeTests` proves each rule against a wrong-on-purpose fixture. The fixtures are nested in that class, never in
  `CESDK.Abi`.
- Two cases stay a review rule. An implicit `[StructLayout(LayoutKind.Sequential)]` has identical metadata. A new struct
  with a size row but no per-field offset test passes.

## Promise

- A public struct without a row in the expected-size table fails the run.
- Only `PluginInitRecord` is packed. A stray `Pack` on another struct fails, even when size and offsets do not change.
  An unpacked mirror is 40 bytes and writes 4 bytes past the host's 36-byte variable.
- Layout tests skip in a 32-bit process. Every other test runs, so a default run never ends with zero tests.

## Run the tests

```powershell
dotnet test --project tests/CESDK.Abi.Tests
```
