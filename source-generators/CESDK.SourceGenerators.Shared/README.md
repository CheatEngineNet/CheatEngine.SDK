# CESDK.SourceGenerators.Shared

The code that more than one CESDK Roslyn component needs, in one project that builds an assembly of its own.

## Objective

Keep code that more than one Roslyn component needs in exactly one place: pipeline and emission helpers, the Lua
call-shape emitters, the plugin-shape predicate and the LuaBindings input validation.

## Why it exists

The three generators and `CESDK.Analyzers` share this code. A source file linked into several projects has several
owners but one namespace, and tools that keep namespaces in step with folders rewrite it toward whichever owner they
evaluated last. One project gives every type one owner and one namespace. NuGet does not resolve the dependencies of an
analyzer asset, but every assembly under `analyzers/dotnet/cs` is loaded beside the components, so this assembly travels
with them. Every type is `internal`, and `InternalsVisibleTo` in the project file names the components and the test
projects that may see it.

## How it works

A component adds a `ProjectReference` to this project. A project that consumes a component as an analyzer
(`OutputItemType="Analyzer"`) references this project the same way: `CESDK.Engine` for the `EngineApi` generator,
`CESDK.LivePlugin` and `CESDK.Benchmarks`. `CESDK` packs it next to the components with `PackAsAnalyzer`. The project
targets `netstandard2.0`, takes no runtime dependency and avoids file system, environment and culture APIs (RS1035).

| Namespace                                                                                           | Holds                                                                                                                                                                                                                                                                                                 | Used by                                                                                                                                                                                  |
|-----------------------------------------------------------------------------------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `CESDK.SourceGenerators.Shared`: `EquatableArray`, `SourceWriter`, `CSharpLiteral`                  | A value-equal immutable array for pipeline models, a text writer with `\n` line ends and four-space indentation, and ASCII-only `"..."` and `"..."u8` literals.                                                                                                                                       | All three generators, and `EquatableArray` in `CESDK.Analyzers`.                                                                                                                         |
| `CESDK.SourceGenerators.Shared`: `GeneratedCodeText`, `TrackingNames`, `HintNames`, `BuildProperty` | The generated-file header and `[GeneratedCode]` attribute, the `CESDK.` step-name prefix, stable ASCII hint names, and a boolean `build_property.*` reader that only `EntryPoint` uses.                                                                                                               | All three generators.                                                                                                                                                                    |
| `CESDK.SourceGenerators.Shared`: `AnnotationsMetadataNames`, `ManagedEntryPointNames`               | The metadata names of the annotation attributes and the plugin base class, and the bootstrap identity `CESDK.CESDK.CEPluginInitialize`.                                                                                                                                                               | All three generators, and `AnnotationsMetadataNames` in `CESDK.Analyzers`.                                                                                                               |
| `CESDK.SourceGenerators.Shared.LuaEmit`                                                             | Roslyn-free models and emitters for a wrapper that calls a Lua global, an `[UnmanagedCallersOnly]` thunk for a `[LuaFunction]` method, and the `RegisterLuaFunctions` and `UnregisterLuaFunctions` pair.                                                                                              | `LuaBindings` (models built from symbols) and `EngineApi` (from spec text). `CESDK.Analyzers` uses the value kinds, names, call form and the argument and result models, but no emitter. |
| `CESDK.SourceGenerators.Shared.Shapes`                                                              | `PluginShape.Inspect` and `PluginShapeIssues`: one flag per reason a `[CheatEnginePlugin]` class cannot be constructed by the generated entry point. The base class `CESDK.Hosting.Plugin.CheatEnginePlugin` is matched by name and namespace, and any constructor callable with no arguments counts. | `EntryPoint` emits only when the result is `None`. `CESDK.Analyzers` rule `CESDK0001` reports one diagnostic per flag.                                                                   |
| `CESDK.SourceGenerators.Shared.LuaBindings.Model` and `.Parsing`                                    | The input validation of `[LuaFunction]`, `[LuaGlobal]` and their containing types: symbols in, flags out, with no emitter and no pipeline.                                                                                                                                                            | `LuaBindings` and `CESDK.Analyzers`, so a shape the generator skips is exactly a shape the CESDK2xxx rules report.                                                                       |

`CESDK.Analyzers.CodeFixes` reaches this project through `CESDK.Analyzers` and reads `PluginShapeIssues` from it.

## Promise

- A record that holds an `EquatableArray<T>` stays value-equal across pipeline runs, and `default` behaves as empty
  (`Record_holding_an_array_gets_value_equality`, `Default_value_behaves_as_the_empty_array`).
- `SourceWriter` output never contains `\r` (`Output_never_contains_a_carriage_return`).
- `[GeneratedCode]` carries the assembly version, so generated files do not change with each commit
  (`CreateGeneratedCodeAttribute_version_is_stable_across_commits`).
- Constants that mirror `net10.0` code stay in step through tests: `ManagedEntryPointNames` against `CESDK.Abi`, and
  `LuaValueKind` against the marshallers of `CESDK.Lua`.
- Every name the generators write into generated code or look symbols up by denotes a real type: the metadata names
  against `CESDK.Annotations` and `CESDK.Hosting` (`AnnotationsMetadataNamesTests`), and the `global::CESDK.Lua` names
  and the `LuaState` recognition against `CESDK.Lua` (`LuaApiNamesTests`). A namespace move that leaves a string behind
  fails there instead of silently switching a generator or a rule off.
- The entry-point generator and rule `CESDK0001` agree on which plugin classes can be constructed
  (`PluginShapeParityTests`).

## Run the tests

`CESDK.SourceGenerators.EntryPoint.Tests/SharedCode` covers the writer, literals, array, header, step names,
`BuildProperty` and the metadata names. `CESDK.SourceGenerators.LuaBindings.Tests/SharedCode` covers `HintNames`, the
`LuaEmit` helpers and the names written into generated code.
`Shapes` runs through the `EntryPoint` and `CESDK.Analyzers` test projects.

```powershell
dotnet test --project tests/CESDK.SourceGenerators.EntryPoint.Tests --filter-namespace "CESDK.SourceGenerators.EntryPoint.Tests.SharedCode"
```
