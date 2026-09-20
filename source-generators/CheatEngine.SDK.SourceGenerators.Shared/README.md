# CheatEngine.SDK.SourceGenerators.Shared

The code that more than one CheatEngine.SDK Roslyn component needs, in one project that builds an assembly of its own.

## Objective

Keep code that more than one Roslyn component needs in exactly one place: pipeline and emission helpers, the Lua
call-shape emitters, the plugin-shape predicate and the LuaBindings input validation.

## Why it exists

The three generators and `CheatEngine.SDK.Analyzers` share this code. A source file linked into several projects has several
owners but one namespace, and tools that keep namespaces in step with folders rewrite it toward whichever owner they
evaluated last. One project gives every type one owner and one namespace. NuGet does not resolve the dependencies of an
analyzer asset, but every assembly under `analyzers/dotnet/cs` is loaded beside the components, so this assembly travels
with them. Every type is `internal`, and `InternalsVisibleTo` in the project file names the components and the test
projects that may see it.

## How it works

A component adds a `ProjectReference` to this project. A project that consumes a component as an analyzer
(`OutputItemType="Analyzer"`) references this project the same way: `CheatEngine.SDK.Engine` for the `EngineApi` generator,
`CheatEngine.SDK.LivePlugin` and `CheatEngine.SDK.Benchmarks`. `CheatEngine.SDK` packs it next to the components with `PackAsAnalyzer`. The project
targets `netstandard2.0`, takes no runtime dependency and avoids file system, environment and culture APIs (RS1035).

| Namespace                                                                                                     | Holds                                                                                                                                                                                                                                                                                                           | Used by                                                                                                                                                                                            |
|---------------------------------------------------------------------------------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `CheatEngine.SDK.SourceGenerators.Shared`: `EquatableArray`, `SourceWriter`, `CSharpLiteral`                  | A value-equal immutable array for pipeline models, a text writer with `\n` line ends and four-space indentation, and ASCII-only `"..."` and `"..."u8` literals.                                                                                                                                                 | All three generators, and `EquatableArray` in `CheatEngine.SDK.Analyzers`.                                                                                                                         |
| `CheatEngine.SDK.SourceGenerators.Shared`: `GeneratedCodeText`, `TrackingNames`, `HintNames`, `BuildProperty` | The generated-file header and `[GeneratedCode]` attribute, the `CheatEngine.SDK.` step-name prefix, stable ASCII hint names, and a boolean `build_property.*` reader that only `EntryPoint` uses.                                                                                                               | All three generators.                                                                                                                                                                              |
| `CheatEngine.SDK.SourceGenerators.Shared`: `AnnotationsMetadataNames`, `ManagedEntryPointNames`               | The metadata names of the annotation attributes and the plugin base class, and the bootstrap identity `CESDK.CESDK.CEPluginInitialize`.                                                                                                                                                                         | All three generators, and `AnnotationsMetadataNames` in `CheatEngine.SDK.Analyzers`.                                                                                                               |
| `CheatEngine.SDK.SourceGenerators.Shared.LuaEmit`                                                             | Roslyn-free models and emitters for a wrapper that calls a Lua global, an `[UnmanagedCallersOnly]` thunk for a `[LuaFunction]` method, and the `RegisterLuaFunctions` and `UnregisterLuaFunctions` pair.                                                                                                        | `LuaBindings` (models built from symbols) and `EngineApi` (from spec text). `CheatEngine.SDK.Analyzers` uses the value kinds, names, call form and the argument and result models, but no emitter. |
| `CheatEngine.SDK.SourceGenerators.Shared.Shapes`                                                              | `PluginShape.Inspect` and `PluginShapeIssues`: one flag per reason a `[CheatEnginePlugin]` class cannot be constructed by the generated entry point. Current callers resolve the base-class symbol from the referenced SDK assembly, and only a real zero-parameter constructor (implicit included) counts. | `EntryPoint` emits only when the result is `None`. `CheatEngine.SDK.Analyzers` rule `CESDK0001` reports one diagnostic per flag.                                                                   |
| `CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Model` and `.Parsing`                                    | The input validation of `[LuaFunction]`, `[LuaGlobal]` and their containing types: symbols in, flags out, with no emitter and no pipeline.                                                                                                                                                                      | `LuaBindings` and `CheatEngine.SDK.Analyzers`, so a shape the generator skips is exactly a shape the CESDK2xxx rules report.                                                                       |

`CheatEngine.SDK.Analyzers.CodeFixes` reaches this project through `CheatEngine.SDK.Analyzers` and reads `PluginShapeIssues` from it.

## Promise

- A record that holds an `EquatableArray<T>` stays value-equal across pipeline runs, and `default` behaves as empty
  (`Record_holding_an_array_gets_value_equality`, `Default_value_behaves_as_the_empty_array`).
- `SourceWriter` output never contains `\r` (`Output_never_contains_a_carriage_return`).
- `[GeneratedCode]` carries the assembly version, so generated files do not change with each commit
  (`CreateGeneratedCodeAttribute_version_is_stable_across_commits`).
- Constants that mirror `net10.0` code stay in step through tests: `ManagedEntryPointNames` against `CheatEngine.SDK.Abi`, and
  `LuaValueKind` against the marshallers of `CheatEngine.SDK.Lua`.
- Every name the generators write into generated code or use as a lookup key denotes a real type. Generator-facing
  annotations, the plugin base, and `LuaState` are then accepted only when their defining assembly is the referenced
  SDK assembly; a same-FQN source or foreign reference cannot impersonate them. `AnnotationsMetadataNamesTests`,
  `LuaApiNamesTests`, and contract-identity tests catch drift before a string can silently switch a generator or rule
  off.
- The entry-point generator and rule `CESDK0001` agree on which plugin classes can be constructed
  (`PluginShapeParityTests`).

## Run the tests

`CheatEngine.SDK.SourceGenerators.EntryPoint.Tests/SharedCode` covers the writer, literals, array, header, step names,
`BuildProperty` and the metadata names. `CheatEngine.SDK.SourceGenerators.LuaBindings.Tests/SharedCode` covers `HintNames`, the
`LuaEmit` helpers and the names written into generated code.
`Shapes` runs through the `EntryPoint` and `CheatEngine.SDK.Analyzers` test projects.

```powershell
dotnet test --project tests/CheatEngine.SDK.SourceGenerators.EntryPoint.Tests --filter-namespace "CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.SharedCode"
```
