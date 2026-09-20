# CheatEngine.SDK.SourceGenerators.LuaBridgeContract.Tests

Pure managed contract tests for the repository-only `LuaBridgeContractGenerator`.

## Objective

The generator is intentionally part of the SDK build, not a consumer NuGet asset. These tests prove that an in-memory
`protected-operations.json` becomes the internal `LuaProtectedOperation` enum and bitmap expected by the C11 bridge
contract, while malformed catalogue text fails at its external-file location instead of producing stale source.

## Coverage

`CatalogEmissionTests` verifies numeric opcode ordering despite reversed JSON array order, the exact derived bitmap,
clean semantic compilation, deterministic identical runs, replacement of a valid catalogue by another valid catalogue
or an invalid one without stale generated source, and the 11-operation/`0x7FF` contract of the production catalogue
embedded as test data. `CatalogDiagnosticsTests` verifies malformed JSON, duplicate JSON properties, wrong
bitmap, duplicate opcode and duplicate catalogue inputs. No test needs a native DLL, Cheat Engine process or
filesystem access by the generator.

## Run

```powershell
dotnet test --project tests/CheatEngine.SDK.SourceGenerators.LuaBridgeContract.Tests
```

## Out of scope

The tests do not assert C11 implementation behavior or Lua `longjmp` containment. Those belong to the native bridge
and Lua integration suites. The production wiring is separately checked by building `CheatEngine.SDK.Lua.Interop`,
which supplies the real catalogue as an `AdditionalFile`.
