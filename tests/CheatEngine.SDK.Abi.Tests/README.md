# CheatEngine.SDK.Abi.Tests

Tests that pin the memory layout of `CheatEngine.SDK.Abi` to the Cheat Engine plugin ABI, with no Cheat Engine and no
Lua DLL.

## Objective

Prove that every struct, enum and constant of `CheatEngine.SDK.Abi` has the documented x64 size, offset and value. Prove
that the assembly stays blittable and `Stdcall` only.

## Why it exists

A wrong size or offset corrupts memory inside the Cheat Engine process. Nothing else in the solution notices, because
the compiler accepts any layout. The suite pins the documented numbers. Only [
`tests/CheatEngine.SDK.LivePlugin`](../CheatEngine.SDK.LivePlugin/README.md#run-it-in-cheat-engine) runs against Cheat
Engine itself.

## How it works

Independent techniques check the layout, so one mistake cannot hide behind another. `Managed/` and `Native/` mirror
the `CheatEngine.SDK.Abi` namespaces. `Support/` holds `Layout`, the `AbiShape` gate, the per-field `FieldLayoutGate`
with its literal `FieldLayoutExpectations` table, and their tests. Expected numbers are literals next to the assertion
or
in that table, never derived from the code under test.

| Technique              | What it does                                                                                                                                                                                                                                                                                                                                                                               |
|------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Address-of arithmetic  | `Layout.SizeOf<T>()` and `Layout.OffsetOf` measure the size and every field offset.                                                                                                                                                                                                                                                                                                        |
| Raw bytes              | The packed 36-byte init record is written into a guard-filled buffer at an aligned and an odd address. The 48-byte exports record is built as raw bytes, then read through the struct.                                                                                                                                                                                                     |
| Host simulation        | `&Method` of a real `[UnmanagedCallersOnly]` `Stdcall` function is stored in every typed function-pointer slot, then called through the field.                                                                                                                                                                                                                                             |
| Per-field gate         | `FieldLayout/FieldLayoutContractTests` walks every instance field of every struct (public, internal, nested private, compiler-generated backing fields) and compares its offset (measured with the IL `ldflda` instruction), width and kind with one literal row of `Support/FieldLayoutExpectations.cs`, in both directions.                                                              |
| Native-fact comparison | The native CI job builds the checked `ce77-native-abi-facts.txt`; the Debug build-test job passes it in and sets the required gate. A compiled managed test measures every fixture-covered layout (C-header records and, since schema 3, the packed managed bootstrap record and the managed exports) and compares its size, alignment, and every field's offset and width to that output. |

The test assembly applies `[assembly: DisableRuntimeMarshalling]`, so calls take the path a plugin takes.
`BoolCallBoundaryTests` calls through pointers whose signature differs by one substitution (`int` for `Bool32`, `byte`
for `Bool8`). Every branch of `AbiArchitecture` is tested through its internal overloads.

`NativeAbiFixtureManagedComparisonTests` has no local fixture dependency. The Debug build-test CI job downloads the
facts the native job built, supplies `CE77_NATIVE_ABI_FACTS_PATH` and sets `CE77_NATIVE_ABI_REQUIRED=true` for its
solution test run, which makes a missing facts path fail the comparison gate. Ordinary managed runs omit the required
mode and can omit the facts path; that local opt-out is
intentional and is not a substitute for an exact-host test.

- `AssemblyConformanceTests` compares an expected-size table with every struct of the assembly (public, internal and
  nested, keyed by full name) in both directions, then runs the `AbiShape` reflection gate over every struct.
- `FieldLayoutContractTests` is the per-field gate: a field without a row, a row without a field, or a wrong offset,
  width or kind fails the run.
  `Reflected_offsets_agree_with_address_of_offsets_for_the_packed_init_record_and_the_managed_exports`
  proves that the reflected offsets equal the C# address-of arithmetic, and two wrong-on-purpose nested structs prove
  the
  gate reports each class of error (including a size-preserving retype that a size check misses).
- The gate rejects `bool`, `char`, references, foreign value types, managed or non-`Stdcall` function pointers and
  by-reference parameters at any depth. It exists because `delegate* unmanaged[Cdecl]<bool, char>` compiles without a
  diagnostic under `DisableRuntimeMarshalling`.
- `AbiShapeTests` proves each rule against a wrong-on-purpose fixture. The fixtures are nested in that class, never in
  `CheatEngine.SDK.Abi`.
- One case stays a review rule: an implicit `[StructLayout(LayoutKind.Sequential)]` has identical metadata.

## Promise

- A struct, public or not, without a row in the expected-size table fails the run
  (`AssemblyConformanceTests.Every_structure_is_listed_in_the_expected_size_table`,
  `Every_structure_including_internal_and_nested_ones_has_the_expected_size`).
- Adding, removing, moving or retyping a field of any struct without editing `Support/FieldLayoutExpectations.cs` fails
  the run (`FieldLayoutContractTests.Every_instance_field_of_every_abi_structure_has_a_layout_row`,
  `Every_layout_row_names_an_existing_field`, `Every_field_has_the_expected_offset_on_64_bit`,
  `Every_field_has_the_expected_width_on_64_bit`, `Every_field_has_the_expected_kind`).
- Only `PluginInitRecord` is packed. A stray `Pack` on another struct fails, even when size and offsets do not change.
  An unpacked mirror is 40 bytes and writes 4 bytes past the host's 36-byte variable.
- The type-0 selection record matches the host type of `plugin.pas` field by field, and both same-size Pascal mirrors
  are told apart by a per-field check (`SelectedRecordOracleTests`).
- The embedded [classic slot registry](../CheatEngine.SDK.Repository.Tests/Abi/TestData/classic-slot-registry.json)
  agrees with `ExportedFunctionsPrefix`
  on slots 0-17, their offsets, widths and opacity (`ClassicSlotRegistryPrefixTests`).
- A classic slot is observed only when the declared and the physical sizes both reach `8 * (slot + 1)` bytes, for every
  slot and boundary, and the prefix copy is all or nothing between slot boundaries
  (`ClassicExportedFunctionsSlotReaderTests`, `ClassicExportedFunctionsPrefixReaderTests`).
- The classic debug dispatcher admits callbacks from native OS threads, returns the Cheat Engine fallback, drains the
  running callbacks on release and refuses late ones (`ClassicDebugEventDispatcherTests`, Q38 C1).
- Layout tests skip in a 32-bit process. Every other test runs, so a default run never ends with zero tests.

## Run the tests

```powershell
dotnet test --project tests/CheatEngine.SDK.Abi.Tests
```
