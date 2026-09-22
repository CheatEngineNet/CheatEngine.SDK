# CheatEngine.SDK.Tests

Tests for the `CheatEngine.SDK` NuGet package as a plugin author receives it.

## Objective

Pack `src/CheatEngine.SDK`, build throwaway plugin projects against the packed package, run the required package-only
executables, and check what they get. The local pack is test input, not a package publication.

## Why it exists

A project reference proves that the source compiles, not that the installed package behaves. So this project has no
`ProjectReference`. It runs `dotnet pack` and `dotnet build` as subprocesses and sees exactly what a consumer sees.

## How it works

1. One collection fixture (`PackagedUmbrellaFixture`) packs `src/CheatEngine.SDK/CheatEngine.SDK.csproj` in Release
   into a temporary local feed. The package id, that project path and the lower-cased id NuGet uses as the extraction
   folder name live in one place, `UmbrellaPackage`.
2. It restores the consumers below and builds them in Release. Every consumer shares the fixture-local package
   directory,
   which is isolated from other fixture runs.
3. The generated `NuGet.Config` maps the exact `CheatEngine.SDK` package id to the freshly packed local feed, never an
   earlier NuGet extraction or another package source. Nuget.org remains available for other package ids. The packed Lua
   runtime consumer then runs against the checked-in offline Lua 5.3 fixture.
4. A separate duplicate-name diagnostic source is built only to verify its intended compiler diagnostic.
5. The tests read the `.nupkg` and, per consumer, what the table lists.

| Consumer                                                  | What it sets                                                | What the tests read                                                                                                                                                                                      |
|-----------------------------------------------------------|-------------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `DefaultConsumer`                                         | Nothing, so it takes every package default                  | Build properties, entry point, atomic build/publish deployment folder                                                                                                                                    |
| `ExplicitUnsafeFalseConsumer`                             | `AllowUnsafeBlocks=false`                                   | `AllowUnsafeBlocks`                                                                                                                                                                                      |
| `LuaFunctionOptInConsumer`                                | `[LuaFunction]` + `AllowUnsafeBlocks=true`                  | The documented explicit unsafe opt-in compiles the generated registration thunk                                                                                                                          |
| `LuaFunctionWithoutUnsafeConsumer`                        | `[LuaFunction]`, no unsafe opt-in                           | The package analyzer rejects the project with `CESDK2001`                                                                                                                                                |
| Packed Lua runtime consumer                               | Executable, restored only from the fresh local feed         | Runs generated bindings against the checked-in offline Lua 5.3 fixture: custom and generic marshalling, the generated callback/lease-collision contract, and the factual origin of a failing `LuaStatus` |
| Duplicate-name diagnostic source                          | Two otherwise valid Lua exports with one name               | The package analyzer rejects the source with `CESDK2005`                                                                                                                                                 |
| Package-only AOT publication consumer                     | Temporary executable published trimmed with Native AOT      | The published standalone executable runs; this is not evidence that Cheat Engine can load, host, or unload an AOT plugin                                                                                 |
| `EntryPointOffConsumer`                                   | `CheatEngineSdkGenerateEntryPoint=false` + manual bootstrap | The author-owned entry point                                                                                                                                                                             |
| `IndirectConsumer`                                        | Only a reference to a temporary relay pkg                   | Direct-only build properties, bootstrap and native bridge stay absent                                                                                                                                    |
| `UnsetPlatformTargetConsumer`                             | `PlatformTarget` empty                                      | The direct package target accepts the host-selected x64 architecture                                                                                                                                     |
| `AnyCpuPlatformTargetConsumer`                            | `PlatformTarget=AnyCPU`                                     | The direct package target accepts a managed library loadable in the x64 CE host                                                                                                                          |
| `X64PlatformTargetConsumer`                               | `PlatformTarget=x64`                                        | The direct package target accepts the explicit supported architecture                                                                                                                                    |
| `X86/Arm/Arm64/Itanium/UnsupportedPlatformTargetConsumer` | Explicit unsupported target                                 | The direct package target rejects every unsupported architecture with `CESDK9101`                                                                                                                        |

The build consumers are `net10.0` class libraries with one valid plugin class, in a temporary directory outside the
repository. The packed Lua runtime consumer and package-only AOT publication consumer are temporary executables. The
normal consumers use x64; the `PlatformTarget` cases deliberately use the permitted and rejected values listed above.
Every pack of one commit has the same version, and NuGet treats an extracted id and version as immutable. A restore into
the machine-wide packages folder could reuse a stale extraction. So every fixture run uses `--packages` with its own
directory, shared by that fixture's consumers, and `RestoreIsolationTests` asserts the extraction landed there.

`EntryPointOffConsumer` proves that the packaged `CompilerVisibleProperty` reaches the generator: CESDK0003 requires
the manual `CESDK.CESDK.CEPluginInitialize(System.IntPtr, int)` contract, and that manually-declared type would collide
if the generator had not really been disabled. This manual bootstrap is deliberately distinct from the direct-package
generated bootstrap. The generator is deliberately silent when the direct-package property is absent, so the default
consumer also proves that the direct `build/` asset supplies `true`. `IndirectConsumer` is a real NuGet dependency chain
(`IndirectConsumer -> relay package -> CheatEngine.SDK`), rather than a project-reference approximation; its plugin
source still compiles from the package's transitive library assets, but it receives none of the direct-only `build/`
behavior, generated bootstrap, or native bridge. The runtime consumer's checked-in Lua 5.3 fixture is a native Lua
library, not a live Cheat Engine host: its run does not prove Cheat Engine runtime loading. Consumers sit outside the
repository, so they import none of its build settings. Each generated `NuGet.Config` maps its exact local package ids
(including the temporary relay when applicable) to the fresh local feed and leaves nuget.org available for other ids.

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
- The packed Lua runtime consumer restores the exact `CheatEngine.SDK` id from this run's local feed into package
  storage
  shared by that fixture's consumers; nuget.org remains available only for other ids. It then executes against the
  checked-in offline Lua 5.3 fixture. It checks generated custom and generic binding marshalling,
  the generated callback/lease-collision contract, and that a failed generated call preserves the factual LuaStatus
  origin. The fixture is native Lua only, never a live Cheat Engine host; this is not a Cheat Engine runtime-loading
  result.
- A separate duplicate-name diagnostic source uses two otherwise valid exports with the same Lua name and is rejected
  with `CESDK2005`; it is not the executable runtime consumer.
- A separate temporary package-only executable is published with trimming and Native AOT and then run. This validates
  only that standalone publication/execution path: it neither establishes nor implies Cheat Engine loading, hosting, or
  unloading an AOT plugin.
- Direct package consumers with an unset `PlatformTarget`, `AnyCPU`, or `x64` build successfully. Every other
  explicit architecture — including `x86`, `ARM`, `ARM64`, `Itanium`, and an unknown value — is rejected with
  `CESDK9101` by the packaged build target (`PlatformTargetTests`).
- The direct consumer remains deployable after clean/rebuild: its output folder contains the plugin, all SDK runtime
  assemblies, `.deps.json`, `.runtimeconfig.json` and the native bridge (`DeploymentLayoutTests`).
- The checked-in C11 Lua protection bridge is parsed as PE/COFF without loading it: it is PE32+ AMD64, exports exactly
  four symbols, imports only its reviewed CRT/Kernel32 contract, has no delay-load table and cannot acquire a Lua
  module. Its build and publish copies are SHA-256-identical to the audited source asset (`NativeBridgePeAuditTests` and
  `NativeBridgePackagingAuditTests`; the bridge contract is described in the
  [bridge README](../../native/cheatengine-sdk-lua-bridge/README.md)).
- Consumers build against the package packed by this run, never an earlier extraction (`RestoreIsolationTests`).
- The packed `.nupkg` embeds an SPDX 2.2 SBOM at `_manifest/spdx_2.2/manifest.spdx.json` that describes this package id
  and version and lists every other entry of the package with its SHA-256, including the seven libraries, the five
  Roslyn components and the native bridge (`Package_embeds_an_spdx_2_2_sbom_describing_itself`,
  `Sbom_lists_every_shipped_assembly_and_the_native_bridge_with_its_sha256`,
  `Sbom_file_inventory_equals_the_package_entries`).
- The nuspec names the repository and the exact 40-hex commit, and the embedded PDB of every `lib/net10.0` assembly maps
  its sources to that commit through Source Link (`Nuspec_names_the_repository_and_the_exact_commit`,
  `Embedded_libraries_carry_source_link_to_the_repository_commit`).
- The package version is on the `MinVerMinimumMajorMinor` line or later, every `lib/net10.0` assembly carries
  `<major>.0.0.0` as its assembly version, and no repository contract file (`CompatibilitySuppressions.xml`, PublicAPI
  files, lock files) is packed (`Package_version_is_on_the_minver_minimum_line_or_later`,
  `Embedded_assemblies_carry_the_package_major_as_assembly_version`, `Package_carries_no_repository_contract_file`).
  The pack itself also runs package validation against the published 1.0.0 baseline, so it needs nuget.org once.
