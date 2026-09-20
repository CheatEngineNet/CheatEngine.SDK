# CheatEngine.SDK.Analyzers.Tests

Tests for `CheatEngine.SDK.Analyzers` and `CheatEngine.SDK.Analyzers.CodeFixes`: every diagnostic, every code fix, and
agreement with the
generators.

## Objective

Prove that each CheatEngine.SDK diagnostic reports where it should and stays silent elsewhere. Prove that each code fix
produces
the expected source. Prove that the analyzers agree with the generators they mirror.

## Why it exists

Analyzers run inside every consumer's compiler. A false report blocks a valid plugin, and a fix that emits invalid code
breaks a build. None of this needs Cheat Engine or a Lua DLL.

## How it works

| Path                                                                        | Role                                                                                                                                                                                                                                                                                                                                 |
|-----------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `Plugin/`, `Usage/`, `Generation/`                                          | Rule tests, plus code fix tests in `Plugin/` and `Usage/`. One folder per analyzer namespace.                                                                                                                                                                                                                                        |
| `Diagnostics/DiagnosticCatalogTests.cs`                                     | Checks every descriptor: id format, category, help link, `analyzers/docs` page, release-tracking row, single reporter, Fix All provider.                                                                                                                                                                                             |
| `Plugin/PluginShapeParityTests.cs`, `Generation/LuaBindingAnalyzerTests.cs` | Run the real generator and the analyzer over one compilation and compare verdicts.                                                                                                                                                                                                                                                   |
| `Infrastructure/`                                                           | Verifier entry points (`AnalyzerVerifier`, `CodeFixVerifier`), test setups (`CheatEngineSdkAnalyzerTest`, `CheatEngineSdkCodeFixTest`), `ContractStubs`, `RepositoryLayout` and `TestText`. `LocalFrameworkReferences` takes the highest installed `10.0.x` targeting pack, else the running runtime, so no test restores a package. |

- Tests use the Roslyn testing library with its framework-agnostic `DefaultVerifier`, so xUnit v3 reports a mismatch as
  an ordinary failure. Markup such as `{|CESDK0001:span|}` announces a diagnostic. Any diagnostic not announced,
  compiler errors included, fails the test.
- Plugin and usage tests compile against metadata references to the real `CheatEngine.SDK.Annotations` and
  `CheatEngine.SDK.Hosting` assemblies. This makes recognition depend on the defining assembly as well as the type name,
  just as it does in a plugin. The host-mandated `CESDK.CESDK` type is different: the
  `CS0426` tests of CESDK0004 add it as a generated source, as the entry point generator does in a real plugin.
- `PluginShapeParityTests` and `LuaBindingAnalyzerTests` bind to real SDK metadata. The latter also references
  `CheatEngine.SDK.Lua.Interop` and `CheatEngine.SDK.Lua`, because `LuaState` is part of the recognized shape.
- `CodeFixVerifier` sets `MarkupMode.Allow`: CESDK0001 stands for several problems fixed one at a time, so the fixed
  source states what remains. Fix All tests use several plugin classes, so CESDK0002 fires by design, before and after.

## Promise

- A descriptor without a documentation page or a release-tracking row fails `DiagnosticCatalogTests`.
- CESDK0001 stays silent for a class exactly when `EntryPointGenerator` emits an entry point for it. CESDK2002,
  CESDK2003 and CESDK2004 stay silent exactly when `LuaBindingsGenerator` emits a binding.
- `CheatEngineSdkGenerateEntryPoint=true` enables generated-bootstrap rules CESDK0001, CESDK0002, CESDK0004 and
  CESDK0005. Explicit `false` requires CESDK0003's exact manual bootstrap instead. An absent property—an indirect
  package-consumer shape—stays silent rather than assigning bootstrap ownership.
- CESDK0001 and CESDK1004 stay silent in generated code and in projects without a CheatEngine.SDK reference.
- Every code fix, Fix All included, is compared with its expected source, and diagnostics that remain must be stated.

## Run the tests

```powershell
dotnet test --project tests/CheatEngine.SDK.Analyzers.Tests
dotnet test --project tests/CheatEngine.SDK.Analyzers.Tests --filter-class "CheatEngine.SDK.Analyzers.Tests.Usage.UnmanagedCallersOnlyGuardTests"
```
