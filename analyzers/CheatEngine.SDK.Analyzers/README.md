# CheatEngine.SDK.Analyzers

Roslyn analyzers that explain in the editor why Cheat Engine would refuse a CheatEngine.SDK plugin.

## Objective

Report every `CESDKnnnn` diagnostic about plugin code. The rules cover the plugin class shape, native callbacks that can
throw, and Lua bindings the generators cannot implement.

## Why it exists

A source generator that receives unusable input emits nothing and says nothing. The author is left with a plugin that
Cheat Engine cannot load and no message. The generators never report, so the analyzers do. Each silent case becomes a
diagnostic that names the cause and links to a page with the fix.

## How it works

The assembly ships inside the `CheatEngine.SDK` package under `analyzers/dotnet/cs`, never as a package of its own. Its public
surface is the `DiagnosticIds` constants (`CheatEngine.SDK.Analyzers.Diagnostics`) and three analyzers. Everything else is
internal.

| Analyzer                                                            | Rules                                 | Subject                                                                   |
|---------------------------------------------------------------------|---------------------------------------|---------------------------------------------------------------------------|
| `CheatEngine.SDK.Analyzers.Plugin.CheatEnginePluginAnalyzer`        | `CESDK0001`, `CESDK0002`, `CESDK0004` | The `[CheatEnginePlugin]` class and the namespaces of the plugin assembly |
| `CheatEngine.SDK.Analyzers.Usage.UnmanagedCallersOnlyGuardAnalyzer` | `CESDK1004`                           | Methods and local functions marked `[UnmanagedCallersOnly]`               |
| `CheatEngine.SDK.Analyzers.Generation.LuaBindingAnalyzer`           | `CESDK2001` to `CESDK2004`            | Members marked `[LuaFunction]` or `[LuaGlobal]`                           |

Identifiers follow three ranges: `CESDK0xxx` for plugin shape and bootstrap (category `CheatEngine.SDK.Plugin`), `CESDK1xxx` for
runtime-safety usage (`CheatEngine.SDK.Usage`) and `CESDK2xxx` for generator input (`CheatEngine.SDK.Generation`). Identifiers are never
renumbered or reused. The [rule catalog](../docs/README.md) holds one page per rule, and every descriptor's help link
points to its page.

The shape checks are the generators' own code. `PluginShape` and the LuaBindings shape files live in [
`CheatEngine.SDK.SourceGenerators.Shared`](../../source-generators/CheatEngine.SDK.SourceGenerators.Shared/README.md), which this assembly
and the generators all use, so a shape that a generator skips is a shape that an analyzer reports.

`CESDK0002`, `CESDK0004` and the duplicate name check of `CESDK2003` need the whole compilation. They run at compilation
end, so they are reported in build output and in full-solution analysis where an IDE offers it, not while typing. Their
descriptors carry the `CompilationEnd` tag that `RS1037` requires. For `CESDK2003` the tag sits on the whole descriptor,
so an IDE defers all its checks to build or full-solution analysis.

`CESDK0004` exists because Cheat Engine requires the type `CESDK.CESDK` in the plugin assembly, and the SDK generates
it. Inside the namespace `CESDK`, or any namespace under it, the simple name `CESDK` binds to that generated class, so
a qualified name that starts with `CESDK.` no longer resolves to a namespace the plugin declared under `CESDK` (`CS0426`).
The SDK itself lives under `CheatEngine.SDK` and is not affected: the rule reports a root namespace named exactly
`CESDK` and nothing else. It reads namespace declarations through a syntax node action, so the driver skips generated
trees. That keeps the generated `namespace CESDK` of the entry point from triggering the rule.

`CheatEngineSdkGenerateEntryPoint=false` silences `CESDK0001` and `CESDK0002`, which describe what the generated entry point
needs. `CESDK0004` stays on, because Cheat Engine looks up a type `CESDK.CESDK` either way.

Every analyzer is stateless, runs concurrently and skips generated code. It registers nothing unless the CheatEngine.SDK
types it reads resolve. `CESDK1004` accepts only what provably cannot throw, and its exact definition is on
the [rule page](../docs/CESDK1004.md). A new accepted shape needs a reason: it runs no code, allocates nothing and
cannot fail. Add a test on each side of the new boundary.

The analyzer tells the code fix what it found through `Diagnostic.Properties["CheatEngine.SDK.PluginClassProblem"]`. Change both
sides together. The fixes live in [`CheatEngine.SDK.Analyzers.CodeFixes`](../CheatEngine.SDK.Analyzers.CodeFixes/README.md), because a
compiler-loaded assembly must not reference `Microsoft.CodeAnalysis.Workspaces` (`RS1038`).

The assembly takes no runtime dependency. It references `Microsoft.CodeAnalysis.CSharp` 5.9.0,
`Microsoft.CodeAnalysis.Analyzers` and `PolySharp`, all with `PrivateAssets="all"`, because NuGet does not resolve the
dependencies of an analyzer. `EnforceExtendedAnalyzerRules` turns on `RS1035`, which bans file system, environment,
console, culture and `Random` access.

## Promise

- Every rule has a descriptor in its range and category. The descriptor links to a page in `analyzers/docs` that starts
  with the identifier. `AnalyzerReleases.Unshipped.md` holds a row for the rule. `DiagnosticCatalogTests` fails when one
  of these is missing.
- A rule and its generator agree. `PluginShapeParityTests` and `LuaBindingAnalyzerTests` run the real generator and the
  analyzer over the same compilation. Over a matrix of shapes, the generator emits exactly when the analyzer stays
  silent.
- The plugin rules and `CESDK1004` report nothing in generated code or in a project that does not reference CheatEngine.SDK.
  Tests: `Class_in_generated_code_is_not_analysed`, `Method_in_generated_code_is_not_analysed`,
  `Project_without_a_cheatengine_sdk_reference_is_not_analysed`.
- `CESDK1004` accepts a closed list of statements and values and reports every other top-level statement. Calls inside
  `catch` and `finally` blocks are the one limit: they are not proven, as the rule page states.
  `UnmanagedCallersOnlyGuardTests` covers the rejected shapes and values.
- The package carries only the DLL of the assembly (`_CheatEngineSdkPackRoslynComponents` in `src/CheatEngine.SDK/CheatEngine.SDK.csproj`). The build
  fails with `CESDK9002` when the Roslyn pins and `RoslynComponentFloor` in `eng/RoslynComponent.props` disagree.

## Use

Plugin authors get the analyzers through the `CheatEngine.SDK` package and need nothing else. The assembly is built against Roslyn
5.9.0, so the consumer needs .NET SDK 10.0.401 or later. An older compiler reports `CS9057` and skips the analyzers.

Set a severity in `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.CESDK1004.severity = error
```

A project in this repository references the analyzer as a component:

```xml
<ProjectReference Include="../../analyzers/CheatEngine.SDK.Analyzers/CheatEngine.SDK.Analyzers.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

## Add a rule

Add a constant to `Diagnostics/DiagnosticIds.cs`, a descriptor to `Diagnostics/DiagnosticDescriptors.cs`, a row to
`AnalyzerReleases.Unshipped.md`, a page to `analyzers/docs` and tests. Run the tests as described in [
`tests/CheatEngine.SDK.Analyzers.Tests`](../../tests/CheatEngine.SDK.Analyzers.Tests/README.md):

```powershell
dotnet test --project tests/CheatEngine.SDK.Analyzers.Tests
```
