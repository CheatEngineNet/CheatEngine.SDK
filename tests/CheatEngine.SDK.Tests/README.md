# CheatEngine.SDK.Tests

Tests for the `CheatEngine.SDK` NuGet package as a plugin author receives it.

## Objective

Pack `src/CheatEngine.SDK`, build throwaway plugin projects against the packed package, and check what they get.

## Why it exists

A project reference proves that the source compiles, not that the installed package behaves. So this project has no
`ProjectReference`. It runs `dotnet pack` and `dotnet build` as subprocesses and sees exactly what a consumer sees.

## How it works

1. One collection fixture (`PackagedUmbrellaFixture`) packs `src/CheatEngine.SDK/CheatEngine.SDK.csproj` in Release
   into a temporary local feed. The package id, that project path and the lower-cased id NuGet uses as the extraction
   folder name live in one place, `UmbrellaPackage`.
2. It restores the consumers below from that feed and builds them in Release.
3. The tests read the `.nupkg` and, per consumer, what the table lists.

| Consumer                      | What it sets                               | What the tests read                                                                                 |
|-------------------------------|--------------------------------------------|-----------------------------------------------------------------------------------------------------|
| `DefaultConsumer`             | Nothing, so it takes every package default | `AllowUnsafeBlocks`, `EnableDynamicLoading`, `CheatEngineSdkGenerateEntryPoint` and the entry point |
| `ExplicitUnsafeFalseConsumer` | `AllowUnsafeBlocks=false`                  | `AllowUnsafeBlocks`                                                                                 |
| `EntryPointOffConsumer`       | `CheatEngineSdkGenerateEntryPoint=false`   | The entry point                                                                                     |

Each consumer is an x64 `net10.0` class library with one valid plugin class, in a temporary directory outside the
repository. Every pack of one commit has the same version, and NuGet treats an extracted id and version as immutable. A
restore into the machine-wide packages folder could reuse a stale extraction. So every restore uses `--packages` with a
directory of its own, and `RestoreIsolationTests` asserts the extraction landed there.

`EntryPointOffConsumer` proves that the packaged `CompilerVisibleProperty` reaches the generator. The generator's own
default is also true, so the default consumer alone would pass for the wrong reason. The run needs no Cheat Engine.
Consumers sit outside the repository, so they import none of its build settings. They restore only from the local feed
and nuget.org.

## Run the tests

```powershell
dotnet test --project tests/CheatEngine.SDK.Tests
```

## Promise

- `lib/net10.0` holds the six libraries (`Abi`, `Annotations`, `Engine`, `Hosting`, `Lua`, `Lua.Interop`) with their XML
  docs, plus an empty `CheatEngine.SDK.dll`. `analyzers/dotnet/cs` holds `Analyzers`, `Analyzers.CodeFixes`,
  `SourceGenerators.EntryPoint` and `SourceGenerators.LuaBindings`. `build/` and `buildTransitive/` hold
  `CheatEngine.SDK.props`. The `EngineApi` generator is repository-internal and never ships. The package has no NuGet
  dependencies (`PackageContentsTests`, `NuspecDependencyTests`).
- A default consumer gets `CESDK.CESDK`, the entry-point type Cheat Engine requires in every plugin assembly, with a
  two-parameter `CEPluginInitialize`, and `CheatEngineSdkGenerateEntryPoint=false` removes it (`EntryPointTests`).
- `AllowUnsafeBlocks`, `EnableDynamicLoading` and `CheatEngineSdkGenerateEntryPoint` default to true, and an explicit
  `AllowUnsafeBlocks=false` stays false (`BuildPropertyDefaultsTests`).
- Consumers build against the package packed by this run, never an earlier extraction (`RestoreIsolationTests`).
