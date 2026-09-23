# CheatEngine.SDK.Tests

Tests for the `CheatEngine.SDK` NuGet package as a plugin author receives it.

## Objective

Pack `src/CheatEngine.SDK`, build throwaway plugin projects against the packed package, run the required package-only
executables, and check what they get; and test the release tooling that ships it (`eng/release`). The local pack is
test input, not a package publication.

## Why it exists

A project reference proves that the source compiles, not that the installed package behaves. So this project has no
`ProjectReference`. It runs `dotnet pack` and `dotnet build` as subprocesses and sees exactly what a consumer sees.

## How it works

1. One collection fixture (`PackagedUmbrellaFixture`) puts one `CheatEngine.SDK` package into a temporary local feed.
   Its origin is decided first, by `UmbrellaPackageSource`:
   - `CESDK_PACKAGED_UMBRELLA_NUPKG` set: it must be the absolute path of a `CheatEngine.SDK.<version>.nupkg` file. The
     fixture copies exactly that file and never packs. This is the CI Release leg: it packs once, passes the packed file,
     and the same file is uploaded as `nuget-package`, attested and published, so these tests are evidence about the
     shipped file.
   - Unset while `CI=true`: the fixture fails at once with an actionable message. A CI run must test the file it ships,
     and the Debug leg excludes these tests with `--filter-not-trait "Category=Packaging"`.
   - Unset outside CI: the fixture packs `src/CheatEngine.SDK/CheatEngine.SDK.csproj` in Release itself. That package is
     built from your working tree; it is evidence about the source (C1/C2), not about a file that was ever shipped.

   The package id, that project path, the variable name, the `Packaging` category and the lower-cased id NuGet uses as
   the extraction folder name live in one place, `UmbrellaPackage`. Every class of the `PackagedUmbrellaSuite`
   collection, and no other class, carries `[Trait("Category", "Packaging")]`.
2. It restores the consumers below and builds them in Release. Every consumer shares the fixture-local package
   directory, which is isolated from other fixture runs.
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

The `Release/` folder tests the scripts of [`eng/release`](../../eng/release/README.md) without the fixture: it builds
a small synthetic nupkg, SBOM, `build-info.json` and asset folder in a temporary directory and runs the scripts with
`pwsh` 7 (`PowerShellScript` removes the GitHub Actions file-command variables from their environment, so a test run
never writes into a CI job's summary). `Packaging/ReleaseTupleTests` runs the same scripts on the package under test.

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

Local run: the fixture packs the working tree (it needs nuget.org once, for package validation against 1.0.0).

```powershell
dotnet test --project tests/CheatEngine.SDK.Tests --fail-skips on
```

Fast run without the fixture, as the Debug CI leg does it (no pack, no consumer build):

```powershell
dotnet test --project tests/CheatEngine.SDK.Tests --fail-skips on --filter-not-trait "Category=Packaging"
```

Exact-package run, as the Release CI leg does it: pack once, then hand the fixture that file. Restore nothing from
this package into the machine-wide NuGet folder; the fixture restores into its own isolated folder.

```powershell
dotnet build CheatEngine.SDK.slnx -c Release
$feed = Join-Path ([IO.Path]::GetTempPath()) 'cheatengine-sdk-exact-feed'
Remove-Item $feed -Recurse -Force -ErrorAction SilentlyContinue
dotnet pack src/CheatEngine.SDK -c Release --no-restore -o $feed
$env:CESDK_PACKAGED_UMBRELLA_NUPKG = (Get-ChildItem $feed -Filter 'CheatEngine.SDK.*.nupkg').FullName
dotnet test --project tests/CheatEngine.SDK.Tests -c Release --no-build --fail-skips on
Remove-Item Env:CESDK_PACKAGED_UMBRELLA_NUPKG
```

`PackageProvenanceTests` writes `Consumed <file> sha256=<hex> origin=<Prebuilt|SelfPacked>` to the test output, so
the TRX report names the file the facts are about.

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
- A clean consumer takes nothing from the development tree (qualification scenario Q40, C1/C2 evidence only): in the
  build and publish folders of the default consumer, `.deps.json` resolves `CheatEngine.SDK` as a `package` library at
  `cheatengine.sdk/<version>` whose `sha512` is the hash of the package under test, and no `CheatEngine.SDK*` library
  is `project`-typed; neither `.deps.json` nor `.runtimeconfig.json` names the repository root (in any separator form)
  or `additionalProbingPaths`, and no `*.runtimeconfig.dev.json` exists; every `lib/net10.0` assembly of the package is
  deployed byte for byte; no `lua*.dll` is deployed (`CleanConsumerIsolationTests`). Each rule is shown to fail on the
  leak it exists for with synthetic manifests (`ConsumerManifestRuleTests`). The relay carrier maps `CheatEngine.SDK` to
  the fixture feed only, like every consumer, so a version already on nuget.org can never be restored in its place.
- A direct consumer's build and publish bridges are byte-identical to the package's `build/native` entry, whatever the
  package origin (`Direct_consumer_bridges_are_byte_identical_to_the_packed_build_native_entry`).
- The checked-in C11 Lua protection bridge is parsed as PE/COFF without loading it: it is PE32+ AMD64, exports exactly
  four symbols, imports only its reviewed CRT/Kernel32 contract, has no delay-load table and cannot acquire a Lua
  module. Its build and publish copies are SHA-256-identical to the audited source asset (`NativeBridgePeAuditTests` and
  `NativeBridgePackagingAuditTests`; the bridge contract is described in the
  [bridge README](../../native/cheatengine-sdk-lua-bridge/README.md)). Its `bridge-audit-manifest.json` records the
  committed bridge, and `BridgeAuditManifestTests` compare it with the committed blob read through `git cat-file`,
  never with the working-tree DLL that CI replaces with its own build: source hashes and fingerprint, DLL SHA-256, PE
  facts, embedded fingerprint, pinned xmake version and exact-case asset path. They carry no trait, so both CI legs run
  them, and a failure prints the complete expected manifest.
- Consumers build against the package packed by this run, never an earlier extraction (`RestoreIsolationTests`).
- With `CESDK_PACKAGED_UMBRELLA_NUPKG` set, every packaging fact is about exactly that file: the feed copy is
  byte-identical to it and the fixture never packs; without it, only a run outside CI may pack, and a CI run fails
  before any work (`PackageProvenanceTests`). The selection rules reject a relative or missing path, the relay carrier
  and symbol packages, and a missing variable under `CI=true` (`UmbrellaPackageSourceTests`). Every class sharing the
  fixture carries the `Packaging` category and no other class does, so the Debug filter never packs and never empties
  the module (`PackagedUmbrellaTraitTests`).
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
- The release tuple schema and the C# validator the tests use have the same required, enum and const lists, and every
  schema object rejects unknown properties (`ReleaseTupleSchemaTests`); the validator itself rejects unknown or missing
  properties, mixed stages, local paths, and asset lists or tags that do not match the package
  (`ReleaseTupleValidatorTests`).
- `New-ReleaseTuple.ps1` writes a valid tuple (UTF-8 without BOM, LF) that mirrors the package, its bridge, its SBOM,
  `build-info.json` and `SHA256SUMS`; it fails when build-info names another package or another bridge fingerprint,
  reports a packed bridge that differs from the audited one without failing, records `null` hashes and no receipt when
  the qualification files are absent, lists committed receipts and never their event logs, requires the nuget.org
  identities and both bundles for a `Published` tuple, and writes no local path (`ReleaseTupleScriptTests`).
- The byte scan that reads the bridge fingerprint on Linux finds exactly the value the committed DLL exports, and
  refuses bytes without exactly one fingerprint (`BridgeFingerprintExtractionTests`); `Export-PackageSbom.ps1` exports
  the embedded SBOM byte for byte and refuses a package without an SPDX 2.2 one (`PackageSbomScriptTests`);
  `New-Sha256Sums.ps1` writes the `sha256sum -c` format and refuses duplicate or non-local names
  (`Sha256SumsScriptTests`).
- On the package under test, the SBOM export, `SHA256SUMS` and a `PrePublish` tuple succeed and describe that exact file
  (`Pre_publish_tuple_of_the_package_under_test_is_valid`).
