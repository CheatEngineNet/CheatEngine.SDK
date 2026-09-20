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

| Consumer                                                  | What it sets                                                | What the tests read                                                               |
|-----------------------------------------------------------|-------------------------------------------------------------|-----------------------------------------------------------------------------------|
| `DefaultConsumer`                                         | Nothing, so it takes every package default                  | Build properties, entry point, atomic build/publish deployment folder             |
| `ExplicitUnsafeFalseConsumer`                             | `AllowUnsafeBlocks=false`                                   | `AllowUnsafeBlocks`                                                               |
| `LuaFunctionOptInConsumer`                                | `[LuaFunction]` + `AllowUnsafeBlocks=true`                  | The documented explicit unsafe opt-in compiles the generated registration thunk   |
| `LuaFunctionWithoutUnsafeConsumer`                        | `[LuaFunction]`, no unsafe opt-in                           | The package analyzer rejects the project with `CESDK2001`                         |
| `EntryPointOffConsumer`                                   | `CheatEngineSdkGenerateEntryPoint=false` + manual bootstrap | The author-owned entry point                                                      |
| `IndirectConsumer`                                        | Only a reference to a temporary relay pkg                   | Direct-only build properties, bootstrap and native bridge stay absent             |
| `UnsetPlatformTargetConsumer`                             | `PlatformTarget` empty                                      | The direct package target accepts the host-selected x64 architecture              |
| `AnyCpuPlatformTargetConsumer`                            | `PlatformTarget=AnyCPU`                                     | The direct package target accepts a managed library loadable in the x64 CE host   |
| `X64PlatformTargetConsumer`                               | `PlatformTarget=x64`                                        | The direct package target accepts the explicit supported architecture             |
| `X86/Arm/Arm64/Itanium/UnsupportedPlatformTargetConsumer` | Explicit unsupported target                                 | The direct package target rejects every unsupported architecture with `CESDK9101` |

Each consumer is a `net10.0` class library with one valid plugin class, in a temporary directory outside the
repository. The normal consumers use x64; the `PlatformTarget` cases deliberately use the permitted and rejected
values listed above. Every pack of one commit has the same version, and NuGet treats an extracted id and version as
immutable. A restore into the machine-wide packages folder could reuse a stale extraction. So every restore uses
`--packages` with a directory of its own, and `RestoreIsolationTests` asserts the extraction landed there.

`EntryPointOffConsumer` proves that the packaged `CompilerVisibleProperty` reaches the generator: CESDK0003 requires
the manual `CESDK.CESDK.CEPluginInitialize(System.IntPtr, int)` contract, and that manually-declared type would collide
if the generator had not really been disabled. The generator is deliberately silent when the direct-package property is
absent, so the default consumer also proves that the direct `build/` asset supplies `true`. `IndirectConsumer` is a real
NuGet dependency chain (`IndirectConsumer -> relay package -> CheatEngine.SDK`), rather than a project-reference
approximation; its plugin source still compiles from the package's transitive library assets, but it receives none of
the direct-only `build/` behavior. The run needs no Cheat Engine. Consumers sit outside the repository, so they import
none of its build settings. They restore only from the local feed and nuget.org.

## Run the tests

```powershell
dotnet test --project tests/CheatEngine.SDK.Tests
```

## Promise

- `lib/net10.0` holds the six libraries (`Abi`, `Annotations`, `Engine`, `Hosting`, `Lua`, `Lua.Interop`) with their XML
  docs, plus an empty `CheatEngine.SDK.dll`. `analyzers/dotnet/cs` holds `Analyzers`, `Analyzers.CodeFixes`,
  `SourceGenerators.EntryPoint`, `SourceGenerators.LuaBindings` and their `SourceGenerators.Shared` dependency.
  `build/` holds props, targets and the bridge under
  `build/native`; `buildTransitive/` and `runtimes/win-x64/native` are intentionally absent. The `EngineApi` generator
  is repository-internal and never ships. The package has no NuGet dependencies (`PackageContentsTests`,
  `NuspecDependencyTests`).
- A direct default consumer gets `CESDK.CESDK`, the entry-point type Cheat Engine requires in every plugin assembly,
  with a two-parameter `CEPluginInitialize`. An opted-out consumer must provide the exact manual bootstrap; an
  indirect consumer receives neither the direct property nor a generated bootstrap (`EntryPointTests`,
  `DirectReferenceIsolationTests`). The second parameter remains opaque and is never treated as a record size.
- For a direct reference, `EnableDynamicLoading` and `CheatEngineSdkGenerateEntryPoint` default to true.
  `AllowUnsafeBlocks` remains false unless a `[LuaFunction]` consumer explicitly opts in; the fixture compiles the
  opt-in consumer and observes `CESDK2001` from one that does not (`BuildPropertyDefaultsTests`). An indirect reference
  does not receive the direct package defaults.
- A temporary real relay package cannot propagate the defaults, generator or native bridge to its own consumer
  (`DirectReferenceIsolationTests`).
- Direct package consumers with an unset `PlatformTarget`, `AnyCPU`, or `x64` build successfully. Every other
  explicit architecture — including `x86`, `ARM`, `ARM64`, `Itanium`, and an unknown value — is rejected with
  `CESDK9101` by the packaged build target (`PlatformTargetTests`).
- The direct consumer remains deployable after clean/rebuild: its output folder contains the plugin, all SDK runtime
  assemblies, `.deps.json`, `.runtimeconfig.json` and the native bridge (`DeploymentLayoutTests`).
- The checked-in C11 Lua protection bridge is parsed as PE/COFF without loading it: it is PE32+ AMD64, exports exactly
  four symbols, imports only its reviewed CRT/Kernel32 contract, has no delay-load table and cannot acquire a Lua
  module. Its build and publish copies are SHA-256-identical to the audited source asset (`NativeBridgePeAuditTests` and
  `NativeBridgePackagingAuditTests`; the detailed contract is
  `native/cheatengine-sdk-lua-bridge/AUDIT.md`).
- Consumers build against the package packed by this run, never an earlier extraction (`RestoreIsolationTests`).
