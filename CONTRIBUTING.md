# Contributing to CheatEngine.SDK

Thanks for contributing. CheatEngine.SDK is a Windows x64, .NET 10 SDK for Cheat Engine 7.7 plugins. Keep changes
focused, preserve existing patterns, and update documentation when behavior or public APIs change.

## Prerequisites

- Windows x64
- .NET SDK 10.0.401 exactly. [`global.json`](global.json) sets `rollForward: disable`, because the NuGet lock files
  record the packages that the SDK adds implicitly. Install it with
  `winget install Microsoft.DotNet.SDK.10 --version 10.0.401`.
- PowerShell 7 (`pwsh`) on `PATH`: the repository scripts and the repository policy tests run it.
- Git

Ordinary managed work uses the checked-in Lua bridge and does not need a C toolchain. If you change [
`native/cheatengine-sdk-lua-bridge`](native/cheatengine-sdk-lua-bridge/README.md), also install xmake and a Windows x64
C toolchain.

## Build and test

Run these commands from the repository root:

```powershell
dotnet restore CheatEngine.SDK.slnx --locked-mode
dotnet build CheatEngine.SDK.slnx -c Debug --no-restore
dotnet test --solution CheatEngine.SDK.slnx -c Debug --fail-skips on
dotnet test --solution CheatEngine.SDK.slnx -c Release --fail-skips on
```

A locked restore fails when a lock file no longer matches the projects; see
[Lock files and Dependabot](#lock-files-and-dependabot). Locally, the packaging tests pack the SDK themselves; in CI
they consume the exact package of the run.

To create the package locally:

```powershell
dotnet pack src/CheatEngine.SDK -c Release -o artifacts/nuget
```

The pack validates the API against the published CheatEngine.SDK 1.0.0 package, so it needs nuget.org once, and it
embeds the SPDX SBOM.

For manual host validation, use the [live-plugin guide](tests/CheatEngine.SDK.LivePlugin/README.md). Qualification
evidence on the exact host follows the [local qualification protocol](docs/qualification/local-protocol.md).

## Continuous integration

Pull requests run `Pull request CI`, pushes to `main` run `Main CI`, and version tags run `Release`. All three call the
reusable [`ci.yml`](.github/workflows/ci.yml), which builds each thing once and passes it on as an artifact. Every job
runs on a pinned runner label (`windows-2025` or `ubuntu-24.04`), every restore is locked against the committed lock
files, and no job uses a NuGet cache. The scripts behind the steps live in [`eng/ci`](eng/ci/README.md) and run locally
with the same commands.

| Job                           | What it checks                                                                                                                                                                                                                                                                                                                                                                                                                          |
|-------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `native`                      | Builds the Lua protection bridge with the pinned xmake, MSVC toolset and Windows SDK three times (a second output directory, then a copy of the sources elsewhere) and requires the same bytes; checks that the checked-in DLL was built from the checked-in sources; builds the classic ABI fixture facts. A CI-built DLL that differs from the checked-in one is a notice, not a failure.                                             |
| `build-test` (Debug, Release) | Builds the solution once per configuration. Release packs first and checks the single nupkg, its SBOM and its bridge; the packaging tests then consume that exact file through `CESDK_PACKAGED_UMBRELLA_NUPKG`, and the same file is uploaded as `nuget-package` with `build-info.json`. Debug excludes the packaging tests by trait (`--filter-not-trait "Category=Packaging"`), compares the ABI fixture facts and collects coverage. |
| `aot`                         | Publishes and runs the Native AOT probes.                                                                                                                                                                                                                                                                                                                                                                                               |
| `sonar`                       | Analyzes the code with SonarQube Cloud from the Debug coverage.                                                                                                                                                                                                                                                                                                                                                                         |
| `lint`                        | Runs actionlint and PSScriptAnalyzer (over every tracked `*.ps1`), each pinned by version and SHA-256, and zizmor with offline audits, pinned by action commit and version.                                                                                                                                                                                                                                                             |
| `format`                      | Verifies the whitespace formatting of every C# file, including projects outside the solution.                                                                                                                                                                                                                                                                                                                                           |
| `dependency-review`           | Reviews dependency changes of pull requests against `.github/dependency-review-config.yml`; other events record a notice.                                                                                                                                                                                                                                                                                                               |
| `lock-files`                  | Runs `./eng/Update-LockFiles.ps1 -Verify`: the committed lock files must equal a fresh restore.                                                                                                                                                                                                                                                                                                                                         |
| `gate`                        | Produces `CI / Gate` from the results of every job above.                                                                                                                                                                                                                                                                                                                                                                               |

Both `build-test` legs run every `tests/**/*.Tests` module in a single `dotnet test --solution` call with
`--fail-skips on`, hang and crash dumps, and then check that every module produced its report. A skipped test fails the
run, and the packaging tests are excluded from Debug by trait, never skipped.

### Required checks

`CI / Gate` and `PR policy` are the two required checks. There is no merge queue.

- **`CI / Gate`** requires every job to succeed, with one exception: `sonar` must run and pass exactly when
  `SONAR_EXPECTED` is true, and must be skipped otherwise. It is false for fork and Dependabot pull requests, which
  receive no secrets, for release runs, which never request Sonar, and for the dormant `merge_group` clause. A Sonar run
  that was not expected fails the gate too, and no other job may be skipped. The quality gate fails pull requests and is
  only reported on `main`. Drafts do not run CI until they are marked ready for review.
- **`PR policy`** ([`pr-policy.yml`](.github/workflows/pr-policy.yml), rules in `eng/ci/PullRequestPolicy.psm1`) runs on
  drafts too and again on every title or description edit. The title has at most 72 characters, no trailing period and
  no Conventional Commit or area prefix, and starts with an uppercase imperative verb. A pull request that touches
  `libs/`, `src/`, `analyzers/`, `source-generators/` or `native/` (lock files excluded) must change `CHANGELOG.md`,
  unless its description contains the marker `<!-- changelog: not-needed -->` alone on a line, with a reason; a quoted
  marker, as in this sentence or the pull request template, does not waive the rule. Dependabot pull requests are
  exempt, with a notice.

Advisory workflows run outside the gate: CodeQL (C#, C/C++ and the workflows), OpenSSF Scorecard, the online zizmor
audits, the NuGet dependency graph submission, and the weekly [scheduled health checks](eng/ci/health/README.md)
(strict NuGet audit, newest-SDK canary, repeated threading-sensitive tests, release bridge rebuild, external links).
CodeRabbit reviews every pull request, but its findings and pre-merge checks are advisory.

### Workflow changes

- `WorkflowContractTests` in the
  [`tests/CheatEngine.SDK.Repository.Tests`](tests/CheatEngine.SDK.Repository.Tests/README.md) project freeze the
  pipeline: job ids and names, `gate.needs` (a job missing from it fails), SHA-pinned actions, pinned
  runners, timeouts, locked restores and the reserved artifact names. Change a workflow and its tests in the same pull
  request.
- Dependabot does not update `runs-on`. Move to a new runner image in one pull request that changes every workflow
  together with the label lists of the Repository tests, `eng/ci/New-BuildInfo.ps1` and
  `eng/ci/build-info.v0.schema.json`.
- The Debug leg fails when an assembly's line coverage drops below its floor in
  [`eng/coverage-baseline.json`](eng/coverage-baseline.json) minus the tolerance. CI never raises a floor: copy
  `suggested-coverage-baseline.json` from the step summary or the `coverage-report` artifact in a reviewed commit.

### Flaky tests

Required runs never retry a test. With `--fail-skips on` a skipped test fails the run, so a flaky test cannot be hidden
with `Skip`: fix it or delete it in the pull request that finds it. The weekly scheduled health workflow repeats the
threading-sensitive test modules to find tests that fail intermittently.

### Run the checks locally

```powershell
dotnet format whitespace . --folder --verify-no-changes --exclude artifacts   # the format job
actionlint                                                                     # 1.7.12, from the repository root
zizmor --offline .github                                                       # 1.30.1, reads .github/zizmor.yml
./eng/ci/Invoke-ScriptAnalysis.ps1                                             # PSScriptAnalyzer, hash-verified
./eng/Update-LockFiles.ps1 -Verify                                             # the lock-files job
```

## Style and analyzers

- Follow [`.editorconfig`](.editorconfig): UTF-8, tab-based C# indentation (spaces only for continuation alignment), and
  two-space configuration/project-file indentation. [`.gitattributes`](.gitattributes) owns working-tree line-ending
  normalization: CRLF, except the LF-pinned bridge inputs `cheatengine_sdk_lua_bridge.c` and `xmake.lua`, whose hashes
  are embedded in the bridge DLL.
- Formatting is part of the build: `EnforceCodeStyleInBuild` turns the style rules, including IDE0055 formatting, into
  errors. Run `dotnet format CheatEngine.SDK.slnx --no-restore` before committing; the `format` job also checks the
  projects outside the solution.
- Use file-scoped namespaces, explicit accessibility, PascalCase public members, and the local naming patterns already
  present.
- Public APIs require XML documentation. Builds treat compiler and analyzer diagnostics as errors, except for configured
  exceptions. The analysis level is pinned (`10.0-recommended`) and moves only with the SDK.
- Do not use LINQ, including query expressions or `System.Linq` operators. Prefer explicit loops and collection APIs.
- Preserve native Lua identifiers and ABI layouts. Avoid unrelated refactors.

The package includes analyzers and code fixes. See the [diagnostic reference](analyzers/docs/README.md) for the `CESDK`
rules and their fixes.

## Public API and compatibility

- The six shipping libraries under `libs/` track their public API in `PublicAPI.Shipped.txt`, the surface of the
  published 1.0.0 package, and `PublicAPI.Unshipped.txt`, the delta. A public API change updates
  `PublicAPI.Unshipped.txt` in the same commit (RS0016 and RS0017 fail the build otherwise). `PublicAPI.Shipped.txt`
  changes only at a release.
- Every pack is validated against CheatEngine.SDK 1.0.0. An intentional break is declared in
  `src/CheatEngine.SDK/CompatibilitySuppressions.xml`, with its `*REMOVED*` line and a CHANGELOG entry. The file is
  regenerated locally by the maintainer who integrates the change, never by CI.
- An enum added since 1.0.0 that reports a status or an outcome starts with a neutral zero member (`Unknown`); enums
  that mirror Cheat Engine constants keep their 1.0.0 members.

The details, including the regeneration command, are in [`eng/api/README.md`](eng/api/README.md#public-api-tracking).

## Lock files and Dependabot

- Every project restores against a committed `packages.lock.json`. Only `./eng/Update-LockFiles.ps1` writes them, on
  Windows with the pinned SDK: run it after any change to `Directory.Packages.props`, a package reference, a project
  file or `global.json`, and commit the result on its own (`Regenerate lock files after <reason>`). Never edit a lock
  file by hand; on a merge conflict, take either side and run the script again
  ([details](eng/api/README.md#lock-files)).
- Dependabot NuGet pull requests do not regenerate the SDK-implicit entries: check out the branch
  (`gh pr checkout <number>`), run `./eng/Update-LockFiles.ps1`, commit and push.
- For a Dependabot `dotnet-sdk` pull request, apply the `lock-files.patch` of the newest-SDK canary, make the
  `global.json` `errorMessage` name the new version, update the documentation that names the SDK, and run
  `./eng/Update-LockFiles.ps1 -Verify` ([procedure](eng/ci/health/README.md#newest-net-sdk-canary)).

## NuGet audit

Restore audits every direct and transitive package. High and critical advisories (NU1903, NU1904) fail every build;
lower severities are warnings, and the weekly strict audit fails on any advisory. An advisory suppression is a last
resort: only in `Directory.Build.props`, with a justification and an expiry date, and never in a stable release
([details](eng/api/README.md#nuget-audit)).

## Repository guards

The MSBuild guards `CESDK9003` to `CESDK9009` fail the build when the supply-chain policy is weakened: the PublicAPI
files, the analysis-level pin, lock files and Central Package Management, package validation, the major version that
declared breaks require, the SBOM, and the NuGet audit. The [guard table](eng/api/README.md#repository-guards) gives the
condition and the fix of each one.

## Qualification evidence

- Evidence has five levels: C0 static contract, C1 managed tests, C2 native fixture, C3 the exact Cheat Engine host
  with the plugin loaded, and C4 several components. A C1 or C2 result is never presented as host-qualified, and no
  document states a global percentage. See [`docs/qualification`](docs/qualification/README.md).
- A test that evidences a scenario carries `[Trait("Qualification", "Qxx")]`, and the matching cell of
  `docs/qualification/matrix.json` changes in the same pull request; `QualificationMatrixTests` checks both directions.
- C3 and C4 cells change only with a committed receipt produced by `eng/qualification/Invoke-LocalQualification.ps1`
  under the [local qualification protocol](docs/qualification/local-protocol.md). The runner never runs in CI, and no
  workflow references it.

## Documentation

Every Markdown file is checked offline by `DocumentationIntegrityTests` in `tests/CheatEngine.SDK.Repository.Tests`:
exact-case relative links and anchors, no absolute local paths, no reference to the retired `documentations/` tree,
absolute links only in packed READMEs, and the "Recreated 2026-09" header on pages under `docs/`. Run
`dotnet test --project tests/CheatEngine.SDK.Repository.Tests/CheatEngine.SDK.Repository.Tests.csproj`.

## Branches and pull requests

1. Update your local `main`, then create a focused branch from it.
2. Make the smallest change that solves the problem.
3. Run the relevant build, test, and package commands.
4. Open a pull request against `main`; do not push directly to the protected branch.
5. Record consumer-visible changes under `[Unreleased]` in [`CHANGELOG.md`](CHANGELOG.md).

Fill in the pull request template: state the problem, resulting behavior, validation commands and results, the
qualification level of the evidence, public API and release impact, and any remaining live-host limitations. Include
documentation changes that the work requires. Pull requests are squash-merged once `CI / Gate` and `PR policy` pass, so
the pull request title becomes the commit subject on `main`.

## Commits

Use focused commits with an imperative subject of at most 72 characters, without a Conventional Commit prefix or a
trailing period, such as `Fix CI validation findings`. The body explains why. Regenerated lock files go in their own
commit. Do not add `Co-authored-by` trailers.

## Security

Report vulnerabilities privately, as [`SECURITY.md`](SECURITY.md) describes, never in a public issue, discussion or pull
request.

## Releases

Versions are derived by MinVer from the nearest `v*` tag; the current minimum major/minor line is `2.0`, as configured
in [`Directory.Build.props`](Directory.Build.props). Pushing a valid `v<major>.<minor>.<patch>` tag (an optional SemVer
prerelease is allowed) starts the draft-first release workflow. It builds and tests the tag, attests the package
(provenance and SPDX 2.2 SBOM), and creates a draft release with the package, the SBOM, `SHA256SUMS`, the sigstore
bundles and the release tuple. After manual approval on the `nuget` environment it publishes that exact package,
verifies it on nuget.org, and only then publishes the release, whose notes are the `CHANGELOG.md` section of that
version. A stable version also passes the [qualification gate](RELEASING.md#qualification-gate). See
[`RELEASING.md`](RELEASING.md).
