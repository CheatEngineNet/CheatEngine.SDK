# Repository Guidelines

## Project Structure & Module Organization

CheatEngine.SDK is a Windows x64, .NET 10 SDK for Cheat Engine 7.7 plugins.

- `libs/`: layered assemblies for annotations, ABI definitions, Lua interop, Lua operations, engine APIs, and hosting.
- `src/CheatEngine.SDK/`: umbrella NuGet package and consumer build properties.
- `source-generators/` and `analyzers/`: generated bindings, entry points, diagnostics, and code fixes.
- `native/`: bundled Cheat Engine Lua test DLL and the source plus prebuilt Windows x64 Lua protection bridge.
- `tests/`: matching test projects, shared native fixtures, benchmarks, and `CheatEngine.SDK.LivePlugin`.
- `exemples/`: guides, recipes, and API documentation; preserve this directory spelling.
- `eng/` and `.github/`: shared build configuration and CI. Treat `artifacts/` as generated output.

## Build, Test, and Development Commands

Use the SDK selected by `global.json` (10.0.401, `latestFeature`). Ordinary managed builds use the checked-in bridge
binary and need no C toolchain. Only bridge maintainers and CI rebuild it, using xmake and a Windows x64 C compiler.

```powershell
dotnet restore CheatEngine.SDK.slnx
dotnet build CheatEngine.SDK.slnx -c Debug --no-restore
dotnet test --solution CheatEngine.SDK.slnx -c Debug --fail-skips on
dotnet test --solution CheatEngine.SDK.slnx -c Release
dotnet pack src/CheatEngine.SDK -c Release -o artifacts/nuget
```

These restore dependencies, compile the solution, validate both configurations, and produce the package. For host
testing, follow `tests/CheatEngine.SDK.LivePlugin/README.md`; keep the complete plugin output together, including
`cheatengine-sdk-lua-bridge.dll`. Configure Cheat Engine to use .NET 10 explicitly.

## Coding Style & Naming Conventions

Follow `.editorconfig`: UTF-8; C# uses tabs for logical nesting and spaces only for continuation alignment;
configuration/project files use two spaces. `.gitattributes` owns line-ending normalization for the working tree. Use
file-scoped namespaces, explicit accessibility, PascalCase public members, and existing local naming patterns. Preserve
native Lua identifiers and ABI layouts.

Builds enforce compiler and analyzer diagnostics as errors, with configured exceptions. Document public APIs and provide
a README beside every project.

**LINQ is forbidden**, including query expressions and `System.Linq` operators. Use explicit loops and collection APIs
to control allocations and iteration costs. Avoid unrelated refactors.

## Testing Guidelines

Tests use xUnit v3 with Microsoft.Testing.Platform. Name tests `Subject_condition_expected`; add focused regressions for
behavioral fixes. Debug validation rejects skips; Release permits the existing Debug-only guard skip. Coverage can be
collected with `--coverage --coverage-output-format xml`.

Native tests use the bundled Lua DLL. Preserve stack balance, callback lifetimes, ownership, and native error
boundaries. Distinguish fixture results from live Cheat Engine verification.

## Commit & Pull Request Guidelines

History uses imperative subjects such as `Fix CI validation findings`; no conventional-commit prefix is required. Do not
add `Co-authored-by` trailers to commits. Keep commits focused. PR descriptions should explain the problem, resulting
behavior, relevant issues, validation commands/results, and remaining live-host limitations. Update affected
documentation and report build, test, and package results before requesting review.
