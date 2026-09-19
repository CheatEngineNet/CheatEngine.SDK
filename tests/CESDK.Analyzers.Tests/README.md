# CESDK.Analyzers.Tests

Tests for `CESDK.Analyzers` and `CESDK.Analyzers.CodeFixes`: every diagnostic, every code fix, and agreement with the
generators.

## Objective

Prove that each CESDK diagnostic reports where it should and stays silent elsewhere. Prove that each code fix produces
the expected source. Prove that the analyzers agree with the generators they mirror.

## Why it exists

Analyzers run inside every consumer's compiler. A false report blocks a valid plugin, and a fix that emits invalid code
breaks a build. None of this needs Cheat Engine or a Lua DLL.

## How it works

| Path                                                                        | Role                                                                                                                                                                                                                                                                                                               |
|-----------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `Plugin/`, `Usage/`, `Generation/`                                          | Rule tests, plus code fix tests in `Plugin/` and `Usage/`. One folder per analyzer namespace.                                                                                                                                                                                                                      |
| `Diagnostics/DiagnosticCatalogTests.cs`                                     | Checks every descriptor: id format, category, help link, `analyzers/docs` page, release-tracking row, single reporter, Fix All provider.                                                                                                                                                                           |
| `Plugin/PluginShapeParityTests.cs`, `Generation/LuaBindingAnalyzerTests.cs` | Run the real generator and the analyzer over one compilation and compare verdicts.                                                                                                                                                                                                                                 |
| `Infrastructure/`                                                           | Verifier entry points (`AnalyzerVerifier`, `CodeFixVerifier`), test setups (`CesdkAnalyzerTest`, `CesdkCodeFixTest`), `ContractStubs`, `RepositoryLayout` and `TestText`. `LocalFrameworkReferences` takes the highest installed `10.0.x` targeting pack, else the running runtime, so no test restores a package. |

- Tests use the Roslyn testing library with its framework-agnostic `DefaultVerifier`, so xUnit v3 reports a mismatch as
  an ordinary failure. Markup such as `{|CESDK0001:span|}` announces a diagnostic. Any diagnostic not announced,
  compiler errors included, fails the test.
- Plugin and usage tests compile against source stubs of the plugin contract in a separate `CESDK.ContractStubs`
  project. A plugin references the SDK assemblies and never declares `CESDK.Annotations` itself, so stubs in the test
  compilation would trip CESDK0004 on every test.
- `PluginShapeParityTests` builds one raw compilation from `ContractStubs.Combined`. `LuaBindingAnalyzerTests` binds to
  the real `CESDK.Annotations`, `CESDK.Lua.Interop` and `CESDK.Lua` assemblies, because `LuaState` is part of the
  recognized shape.
- `CodeFixVerifier` sets `MarkupMode.Allow`: CESDK0001 stands for several problems fixed one at a time, so the fixed
  source states what remains. Fix All tests use several plugin classes, so CESDK0002 fires by design, before and after.

## Promise

- A descriptor without a documentation page or a release-tracking row fails `DiagnosticCatalogTests`.
- CESDK0001 stays silent for a class exactly when `EntryPointGenerator` emits an entry point for it. CESDK2002,
  CESDK2003 and CESDK2004 stay silent exactly when `LuaBindingsGenerator` emits a binding.
- `CesdkGenerateEntryPoint=false` silences CESDK0001 and CESDK0002 but not CESDK0004.
- CESDK0001 and CESDK1004 stay silent in generated code and in projects without a CESDK reference.
- Every code fix, Fix All included, is compared with its expected source, and diagnostics that remain must be stated.

## Run the tests

```powershell
dotnet test --project tests/CESDK.Analyzers.Tests
dotnet test --project tests/CESDK.Analyzers.Tests --filter-class "CESDK.Analyzers.Tests.Usage.UnmanagedCallersOnlyGuardTests"
```
