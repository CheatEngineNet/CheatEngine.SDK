# Supply-chain contracts

This folder holds the reviewed data behind the SDK's supply-chain rules: which public API exists, which breaks against
the last release are intentional, which of them reach the Client, and how the toolchain, the NuGet audit and the
package inventory are pinned. Every rule here is enforced by an MSBuild guard (`CESDK9003`-`CESDK9009`) or by a C# test
in [`tests/CheatEngine.SDK.Repository.Tests`](../../tests/CheatEngine.SDK.Repository.Tests/README.md), never by a
script alone.

| File | Content | Written by |
|---|---|---|
| [`client-consumed-sdk-types.txt`](client-consumed-sdk-types.txt) | SDK types the Client consumes (its public-signature allowlist and the enums it translates), with provenance | orchestrator or integrator, when the Client list changes |
| [`client-induced-breaks.txt`](client-induced-breaks.txt) | Baseline suppressions that touch a consumed type | integrator, with every regeneration of the suppression file |
| [`apicompat-invisible-changes.txt`](apicompat-invisible-changes.txt) | Declared API removals ApiCompat cannot report, each with a reason | the lot that makes such a change |

## Public API tracking

Each of the six shipping libraries under `libs/` carries `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`, read by
[Microsoft.CodeAnalysis.PublicApiAnalyzers](https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/PublicApiAnalyzers/PublicApiAnalyzers.Help.md)
(referenced from [`eng/Shipping.props`](../Shipping.props)). A public declaration that is not listed fails the build
with RS0016; a listed declaration that no longer exists fails it with RS0017. `CESDK9003` fails a library that lacks
either file, because the analyzer silently ignores a project without them.

- **`PublicAPI.Shipped.txt` is the surface of the published CheatEngine.SDK 1.0.0 package**, not of the tagged sources
  alone: the EngineApi generator emits public API (`CheatEngine.SDK.Engine.Generated.MemoryScalars`) that no `.cs` file
  at the tag declares. The files were produced in a detached worktree at `v1.0.0` with the same SDK and Roslyn: the
  RS0016 fixer (`dotnet format analyzers --diagnostics RS0016`) wrote the hand-written surface, and the five
  generated declarations were taken from the RS0016 messages, because the fixer cannot edit generated documents
  although the analyzer does track them. That build was RS0016/RS0017-clean, and a metadata dump of its assemblies
  equals the dump of `lib/net10.0` of the published nupkg: 155 types and 1375 members on both sides, no difference.
- **`PublicAPI.Unshipped.txt` is the delta to the current tree.** Additions are plain lines. A declaration of Shipped
  that changed or disappeared is repeated with the `*REMOVED*` prefix, and its new form, if any, is added.
- **Who edits what.** A change to public API updates `PublicAPI.Unshipped.txt` in the same commit (the RS0016 code fix,
  or `dotnet format analyzers <project> --diagnostics RS0016 --severity info`). `PublicAPI.Shipped.txt` changes only at a
  release: the release applies the `*REMOVED*` lines to Shipped, moves the rest of Unshipped into it, and leaves
  Unshipped with its `#nullable enable` header. Files stay ordinally sorted after the header, so parallel changes merge
  by union and sort (`PublicApiFileTests`).

## ApiCompat baseline

`src/CheatEngine.SDK` sets `EnablePackageValidation` and `PackageValidationBaselineVersion` 1.0.0: every pack compares
`lib/net10.0` with the published 1.0.0 package
([baseline validator](https://learn.microsoft.com/dotnet/fundamentals/apicompat/package-validation/baseline-version-validator)).
Restore downloads the baseline as a `PackageDownload`, so a pack needs nuget.org once; the local escape hatch for an
offline pack is `-p:DisablePackageBaselineValidation=true`, which `CESDK9006` refuses in CI.

Intentional breaks are listed in
[`src/CheatEngine.SDK/CompatibilitySuppressions.xml`](../../src/CheatEngine.SDK/CompatibilitySuppressions.xml). A stale
suppression fails the pack (`ApiCompatPermitUnnecessarySuppressions` stays false), strict mode stays off so additions
remain legal, and CI never regenerates, relaxes or bypasses the file (`CESDK9006`). Only the integrator regenerates it,
locally, after each rebase:

```powershell
dotnet pack src/CheatEngine.SDK -c Release -p:ApiCompatGenerateSuppressionFile=true -o artifacts/nuget-regenerate
```

The resulting diff must equal the union of the breaks the integrated lots declared. Because the file declares
intentional breaks, `CESDK9007` requires the package major to exceed the baseline major: the line is 2.0
(`MinVerMinimumMajorMinor`), so untagged commits pack as `2.0.0-alpha.0.N` with `AssemblyVersion` 2.0.0.0.

Three records must agree (`CompatibilitySuppressionTests`): the baseline suppressions, the `*REMOVED*` lines, and
[`apicompat-invisible-changes.txt`](apicompat-invisible-changes.txt). Every suppression names a removed line of its
library, and every removed line is suppressed or listed as invisible with a reason. Matching is by declaring type and
member name, not by overload. Only CP0001, CP0002 and CP0011 are accepted today, because their trace in the PublicAPI
files is known; another rule id needs a reviewed extension of the test.

The breaks against 1.0.0 at the time of writing:

| Target | Rule | Change |
|---|---|---|
| `AddressResolutionOptions` constructor, `UseHostSymbolTable` accessors, `Deconstruct` | CP0002 | `UseHostSymbolTable` removed |
| `AddressListPluginInit.Callback`, `DisassemblerContextPluginInit.Callback` | CP0002 | field type changed from a typed function pointer to `void*` (ApiCompat reports a field-type change as a missing member) |
| `MemoryAccessFailure.DestinationTooSmall`, `.WriteFailed`, `.InvalidResult` | CP0011 | renumbered 4, 5, 6 to 5, 8, 9 |

Attribute changes are invisible to both tools (the attribute rules CP0014-CP0016 are off by default):
`LuaClassAttribute` and `LuaPropertyAttribute` lost `Inherited = false`, and `MemoryScanSession.Scanner` and `.Results`
gained `[RequiresPluginEnabled]`. They are recorded in the header of the invisible-changes file and belong in the
release notes.

## Declared breaks and the Client

The Client consumes CheatEngine.SDK 1.0.0 and stays on it until it migrates to 2.x. Any break on a type it consumes is a
Client public break, or a Client behavior change for an enum it translates (audit A11-19, A20-Q48-2).

- [`client-consumed-sdk-types.txt`](client-consumed-sdk-types.txt) copies the Client's list with its commit. The SDK never
  reads the Client repository; the orchestrator or the integrator refreshes this copy when the Client list changes. A
  name that exists neither in 1.0.0 nor now is marked `# unresolved (reason)`, and the test fails when a marked name
  starts resolving or an unmarked one stops.
- [`client-induced-breaks.txt`](client-induced-breaks.txt) is derived: every baseline suppression whose containing type
  is consumed. The test prints the expected lines when the file drifts. The Client turns this list into the "Client
  impact" section of its `docs/migration/sdk-2.0.md`.

This is SDK-side C0 evidence for Q48 (`CompatibilitySuppressionTests` carries `Qualification=Q48`). It never closes Q48
at C1 or C3, which need the Client's consumer-contract tests, and it never closes F05: nothing is published from this
branch.

## Enum contracts

`EnumContractTests` reads enum members from the PublicAPI files (`T.M = <int> -> T`), which RS0016/RS0017 keep equal to
the code:

- The nine `Engine.Enums` types that mirror Cheat Engine constants, plus `Runtime.CheatEngineArchitecture` and
  `Runtime.TargetAbi` (SDK decodings of Cheat Engine codes that appear in public Client signatures), keep their 1.0.0
  members. An added member needs a reviewed entry in the test.
- Every enum declared since 1.0.0 is classified (`StatusOrOutcome`, `Policy`, `ReasonOrEvidence`, `AbiDecision`), so a
  new enum cannot skip the review.
- A `StatusOrOutcome` enum must not read as success when a value was never assigned: its zero member is `Unknown`
  (tolerated: `Unspecified`, `Uninitialized`, `NotAttempted`). Eight enums still start with `Success` or `Released`;
  they are listed with the lot that renumbers them (S-SCAN, S-RT, S-RES, S-GEN-A), and the list can only shrink.

## Source versus package (1.0.0)

The identities of the published baseline, as verified on 2026-09-23 (audit A22-04, SRC02-07, SRC03-01):

| Identity | Value | Where it comes from |
|---|---|---|
| Tag | `v1.0.0`, annotated tag object `11a2b34f913b8814808b017c24134f8af535cd96` | `git rev-parse v1.0.0` |
| Commit | `a6fefb93e9c6f85a1bcedb68bf97e6741175b227` | the tag's commit, equal to the nuspec `<repository commit>` of the package |
| Tree | `41678f939547b2215e106ee3bbc8c2878815652e` | `git rev-parse v1.0.0^{tree}` |
| NuGet content hash (SHA-512) | `n7nHqZ8vzo7Vf20jF0fkh/jUtR3yo1TwRGpXE7ERxZeJ4C5S/Nsft4lqOg7zGwfsD5Nh9tTVgdw4PrybJRF0gA==` | `.nupkg.metadata` of the extracted package, equal to the Client's `packages.lock.json` |
| nuget.org signed file | SHA-256 `3e8c98583ac71af25a5bd7053e7583fbafcd196139fae7c0b04bae9b40a7cd33` | the downloaded `.nupkg` (repository-signed by nuget.org) |
| Public surface | 155 types, 1375 members in 7 assemblies | `PublicAPI.Shipped.txt` of the six libraries (the umbrella `CheatEngine.SDK.dll` declares no type) |
| Changes since | the `*REMOVED*` lines and additions of `PublicAPI.Unshipped.txt`, the suppression file | this folder |

## Toolchain pin

[`global.json`](../../global.json) requires .NET SDK 10.0.401 exactly (`rollForward: disable`) and names the install
command in `sdk.errorMessage`. Lock files record the SDK's implicit packages (ILLink, ILCompiler), so the SDK and the
locks move together ([global.json](https://learn.microsoft.com/dotnet/core/tools/global-json#rollforward)). The
analysis level is pinned next to it in [`Directory.Build.props`](../../Directory.Build.props) (`10.0-recommended`,
never `latest`), and `CESDK9004` fails an override.

Raising the SDK is one pull request: update `global.json` (version and error message), raise the analysis-level pin if
the major or minor changed, fix the new diagnostics, regenerate the lock files, and update the CI setup that installs
the SDK. `ToolchainPinTests` keeps the version, error message and pin consistent.

## Lock files

Every project, inside or outside the solution, restores against a committed `packages.lock.json`
(`RestorePackagesWithLockFile` in `Directory.Build.props`; `CESDK9005` fails a project that opts out of lock files or of
Central Package Management). The files are written by one script only, on Windows with the pinned SDK:

```powershell
./eng/Update-LockFiles.ps1           # regenerate every lock file and list the changed ones
./eng/Update-LockFiles.ps1 -Verify   # regenerate, then fail on any difference or untracked lock file (CI lock-files job)
```

The script enumerates projects from git (a new project is picked up without editing it), restores the solution and then
every project outside it with `--force-evaluate`, gives an unchanged lock its committed bytes back, verifies with
`--locked-mode --force` (without `--force` a no-op restore would compare nothing), and checks the structure: version 2 for every project, a `net10.0/win-x64` section with
`runtime.win-x64.Microsoft.DotNet.ILCompiler` for Native AOT projects (otherwise `dotnet publish --no-restore` fails), and
no `CheatEngine.*` package resolved from a feed. `LockFileTests` mirror these checks offline.

- Never edit a lock file by hand and never let an IDE restore the repository. On a merge conflict, take either side and
  run the script again.
- Regenerate after any change to `Directory.Packages.props`, a package reference, a project file or `global.json`: the
  implicit `Microsoft.NET.ILLink.Tasks` and ILCompiler packages move with the SDK version.
- CI restores pass `--locked-mode` at their entry points. `RestoreLockedMode` is deliberately not set in MSBuild: it would
  make every `--force-evaluate` restore, including the `-Verify` run, fail with NU1005.
- Dependabot updates `Directory.Packages.props` but does not regenerate SDK-implicit entries: check out its branch, run
  the script, commit and push the lock files. SDK-implicit packages (ILLink, ILCompiler) are not Dependabot updates;
  they move with `global.json`.
- The ApiCompat baseline (CheatEngine.SDK 1.0.0) is a `PackageDownload`, which NuGet does not record in lock files; its
  identity is recorded in [Source versus package](#source-versus-package-100).

## NuGet audit

Restore audits every package, direct and transitive, at every severity (`NuGetAudit`, `NuGetAuditMode` `all`,
`NuGetAuditLevel` `low`), following the documented
[dedicated audit pipeline](https://learn.microsoft.com/nuget/concepts/auditing-packages#running-nuget-audit-in-ci)
pattern:

| Code | Meaning | Ordinary build | `restore -p:AuditPipeline=true` |
|---|---|---|---|
| NU1903, NU1904 | high, critical advisory | error (appended to `WarningsAsErrors`) | error |
| NU1901, NU1902 | low, moderate advisory | warning | error |
| NU1900, NU1905 | audit source unreachable or without vulnerability data | warning | error |

The strict run is the scheduled health workflow. CI solution restores also assert, through
[`Directory.Solution.targets`](../../Directory.Solution.targets), that every project of the solution was audited or up
to date. `CESDK9009` fails a project that weakens the policy (audit off, another mode or level, a blocking code in
`NoWarn` or `WarningsNotAsErrors`, or a replaced `WarningsAsErrors`).

An advisory can be excluded only as a last resort, in `Directory.Build.props`, with one item per advisory URL:

```xml
<NuGetAuditSuppress Include="https://github.com/advisories/GHSA-xxxx-xxxx-xxxx"
                    Justification="Why the advisory does not apply to this repository"
                    Expires="2026-12-31"/>
```

`CESDK9009` refuses a suppression declared anywhere else or without both metadata, the strict run fails once `Expires`
is past, and a stable (release) version cannot be packed while any suppression exists: the release path never
suppresses.

## SBOM

Every pack, local, in the packaging tests or in CI, embeds an SPDX 2.2 software bill of materials at
`_manifest/spdx_2.2/manifest.spdx.json`, with its SHA-256 in `manifest.spdx.json.sha256`. It is produced by
[Microsoft.Sbom.Targets](https://github.com/microsoft/sbom-tool) (version pinned in
[`Directory.Packages.props`](../../Directory.Packages.props), referenced with `PrivateAssets="all"`, so the nuspec keeps
no dependency) after `Pack`: the tool unpacks the package, writes the manifest and packs it again. `CESDK9008` fails a
pack without it, and `GenerateSBOM` is set unconditionally so that the inventory a developer sees is the one the
release attests (the attestation uses the SPDX 2.2 predicate).

What it lists:

- the package itself: name `CheatEngine.SDK`, version equal to the package version (the tool reads the version at
  evaluation, before MinVer runs, so `Directory.Build.targets` re-points it after MinVer), supplier `CheatEngineNet`,
  document namespace under `https://github.com/CheatEngineNet/CheatEngine.SDK`;
- every file of the package except the manifest itself, with its SHA-256 and SHA-1: the seven `lib/net10.0` assemblies
  and their XML documentation, the five Roslyn components, the native bridge, the build assets, the README and the
  package metadata files;
- no component. The build-component scan is limited to `src/CheatEngine.SDK`, which holds no package manifest (its
  `packages.lock.json` is not a component source), and the package has no NuGet dependency, so the tool reports "There were no packages detected" (printed as a `##[warning]`
  line, not an MSBuild warning). Build-only tools (MinVer, Meziantou.Analyzer, BannedApiAnalyzers,
  PublicApiAnalyzers, Microsoft.Sbom.Targets) are not shipped and are not listed. PolySharp generates polyfill source
  that is compiled into the Roslyn components; it is source, not a package dependency, and is not listed either.

The package is therefore **not byte-reproducible**: the SPDX `documentNamespace` ends with a random part,
`creationInfo.created` is the pack time, and the package is re-zipped. Reproducibility is promised for what the SBOM
hashes: the embedded assemblies (deterministic, with `ContinuousIntegrationBuild` in CI) and the native bridge.
`SupplyChainPackageTests` checks the manifest against the actual package entries.

## Repository metadata and Source Link

`src/CheatEngine.SDK` sets `PublishRepositoryUrl`, so the nuspec carries
`<repository type="git" url="https://github.com/CheatEngineNet/CheatEngine.SDK" commit="<40-hex>"/>`, as the 1.0.0
package already did, and the embedded Portable PDB of every `lib/net10.0` assembly maps its sources to
`https://raw.githubusercontent.com/CheatEngineNet/CheatEngine.SDK/<same commit>/`
([Source Link](https://learn.microsoft.com/dotnet/standard/library-guidance/sourcelink)). Local packs contain local
paths in their PDBs (path mapping only runs in CI, `ContinuousIntegrationBuild`), so no test asserts their absence.

## Repository guards

| Id | Fails when | Fix |
|---|---|---|
| CESDK9003 | a `libs/` project lacks `PublicAPI.Shipped.txt` or `PublicAPI.Unshipped.txt` | add both files (header `#nullable enable`), then declare the API |
| CESDK9004 | a project's `AnalysisLevel` differs from the pin | remove the override, or raise the pin together with `global.json` |
| CESDK9005 | a project builds with `RestorePackagesWithLockFile` or `ManagePackageVersionsCentrally` other than `true` (restore reports NU1005 first when a lock file exists) | remove the override and run `./eng/Update-LockFiles.ps1` |
| CESDK9006 | package validation is off, has no baseline or runs in strict mode; a CPxxxx or PKVxxx code is in `NoWarn`; or a CI pack regenerates, permits unnecessary, or bypasses suppressions or the baseline | restore the settings; regenerate the suppression file locally (integrator) |
| CESDK9007 | the suppression file declares baseline breaks but the package major does not exceed the baseline major | raise `MinVerMinimumMajorMinor`, or remove the break |
| CESDK9008 | a packable project packs without `GenerateSBOM=true` or without the `Microsoft.Sbom.Targets` reference | set `GenerateSBOM` unconditionally and reference the package with `PrivateAssets="all"` |
| CESDK9009 | the NuGet audit policy is weakened, a suppression is misplaced, incomplete or expired (strict run), a release is packed with a suppression, or a CI solution restore did not audit every project | restore the policy; fix or upgrade the package; complete or remove the suppression |
