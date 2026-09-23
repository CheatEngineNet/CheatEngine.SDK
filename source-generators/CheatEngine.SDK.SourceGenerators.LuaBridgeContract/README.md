# CheatEngine.SDK.SourceGenerators.LuaBridgeContract

Repository-only incremental Roslyn generator for the managed operation selector used by
`CheatEngine.SDK.Lua.Interop`'s C11 Lua-protection microkernel.

## Objective

`libs/CheatEngine.SDK.Lua.Interop/Protected/protected-operations.json` is passed explicitly as an `AdditionalFile` to
`CheatEngine.SDK.Lua.Interop`. This component validates the bridge-relevant parts of that catalogue and emits the
internal `LuaProtectedOperation` enum plus `LuaProtectedOperationContract.Count`, `RequiredBitmap` and `IsDefined`.
The enum is ordered by numeric opcode, never by JSON-array position; the bitmap is independently derived and must
equal the catalogue declaration.

`Count` is the number of active operations, not a maximum-opcode sentinel. ABI opcodes may be sparse after a
versioned retirement; the bitmap and `IsDefined` retain exact membership semantics.

## Boundary

This is a `netstandard2.0` Roslyn component compiled with C# 14. It is not a NuGet asset, is referenced as an
analyzer only by `CheatEngine.SDK.Lua.Interop`, and never emits or compiles C/C++. The generated source becomes part
of that already-shipping interop assembly. It has no runtime parser, reflection, allocation table or dependency.

The generator reads only compiler-provided `AdditionalTextsProvider` content. It does not access files, environment
variables, the clock, current culture or the network. Invalid JSON, duplicate operation id/opcode, an invalid bitmap,
or an incompatible bridge declaration gives a localized `CESDK4001` error on the additional file and emits nothing.
Two matching catalogue files give `CESDK4002` on both files and emit nothing.

## Testing

```powershell
dotnet test --project tests/CheatEngine.SDK.SourceGenerators.LuaBridgeContract.Tests
```

The test project supplies in-memory `AdditionalText` instances. It proves numeric ordering, bitmap derivation,
semantic compilation, deterministic identical re-runs and source-located diagnostics without relying on an installed
Cheat Engine or the filesystem behavior of the generator.

## Out of scope

The C11 bridge owns `lua_pcallk`, `setjmp`/`longjmp` containment and its exported ABI. The separate operation catalogue
and bridge tests own native/source drift evidence. This component intentionally has no policy for direct Lua APIs;
that belongs to the dedicated analyzer work.
