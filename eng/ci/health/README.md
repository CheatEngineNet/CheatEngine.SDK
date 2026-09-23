# Scheduled health checks

The scripts of [`.github/workflows/scheduled-health.yml`](../../../.github/workflows/scheduled-health.yml), a weekly
workflow (Monday 02:37 UTC, and on manual dispatch) that watches what changes without a commit of this repository: new
security advisories, a newer .NET SDK, the toolchain that rebuilds the released native bridge, intermittent test
failures and external links. It is advisory: it is not part of `CI / Gate`, and a failure opens or updates one issue,
**Scheduled health check needs attention** (label `ci`), instead of blocking a pull request.

Audit anchors: register rows PR-CQ-55 (the workflow), PR-CQ-30 (flaky tests), A21-25 (release bridge rebuild);
audit chapter 21, "exit criteria": the package can be rebuilt from its release commit, and its version and content are
locked.

## Jobs

| Job | Script | Fails when | Artifact |
|---|---|---|---|
| `audit` (Strict NuGet audit) | `Invoke-VulnerabilityAudit.ps1` | any restore reports an advisory of any severity (NU1900 to NU1905), or any package is listed as vulnerable | none (job summary) |
| `canary` (Newest .NET SDK canary) | `Set-CanarySdkVersion.ps1`, then `Invoke-SdkCanary.ps1` | the build, the pack or a Release test fails with the newest SDK, or the pinned SDK regenerates different lock files | `health-sdk-canary`, 14 days: `lock-files.patch`, `summary.md`, TRX reports |
| `test-repeat` (Repeat threading-sensitive tests) | `Invoke-TestRepeat.ps1` | any of five runs of the Lua, Lua.Interop or Hosting test module fails | `health-test-repeat`, 14 days, on failure: TRX reports and dumps |
| `bridge-drift` (Release bridge rebuild) | `Invoke-BridgeDriftCheck.ps1` | the rebuild is `Failed`; `ToolchainDrift` is a warning that still opens the issue | `health-bridge-drift`, 30 days: `bridge-drift.json`, `summary.md`, the rebuilt DLL |
| `links` (External documentation links) | `Test-ExternalLink.ps1` | never; broken links set its `broken` output | none (job summary) |
| `notify` (Report health) | `Publish-HealthIssue.ps1` | cannot reach the issues API | none |

`notify` runs for scheduled runs only, with the only write permission of the workflow (`issues: write`). Every
decision the scripts take (tag selection, drift classification, SDK selection, report parsing, link verdicts, the issue
body) lives in the pure module `HealthCheck.psm1`, which `HealthCheckScriptTests` in
[`tests/CheatEngine.SDK.Repository.Tests`](../../../tests/CheatEngine.SDK.Repository.Tests/README.md) exercises offline;
`HealthCommand.psm1` holds the side-effect helpers (native commands with exit-code checks, git, job outputs).

## Strict NuGet audit

Ordinary restores fail only on high and critical advisories; the other audit codes are warnings, so that an advisory
published overnight does not turn every required check red without a commit (see
[`eng/api/README.md`](../../api/README.md#nuget-audit)). This job is the dedicated audit run of that policy: it restores
the solution and every project outside it with `--locked-mode --force -p:AuditPipeline=true` (every audit code becomes
an error; `--force` makes the audit run even when the assets are up to date), then lists the vulnerable (direct and
transitive) and deprecated packages as JSON with `dotnet package list ... --no-restore`.

When it fails: upgrade the package (or the package that brings it transitively, `dotnet nuget why`), regenerate the lock
files with `./eng/Update-LockFiles.ps1`, and open a pull request. Suppressing an advisory is a last resort with an
expiry date ([`eng/api/README.md`](../../api/README.md#nuget-audit)); an expired suppression fails this job.

## Newest .NET SDK canary

`global.json` pins the SDK exactly (`rollForward: disable`) because the lock files record the packages the SDK adds
implicitly (ILLink, ILCompiler). The canary tells, before the Dependabot `dotnet-sdk` pull request arrives, what the
next SDK of the same channel changes:

1. `Set-CanarySdkVersion.ps1` reads `latest-sdk` of the channel's
   [release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json) and rewrites only
   `sdk.version` in the runner's checkout (the test runner section stays: without it `dotnet test` would use VSTest).
   It runs before the composite setup action, which installs exactly the SDK `global.json` names.
2. `Invoke-SdkCanary.ps1` regenerates the lock files with `./eng/Update-LockFiles.ps1`, saves the difference as
   `lock-files.patch`, builds the solution in Release, packs `src/CheatEngine.SDK` and runs every Release test module with
   `CESDK_PACKAGED_UMBRELLA_NUPKG` set to that package.

When the newest SDK is the pinned one, the summary says so, the canary rebuilds with the pinned SDK, and the patch must
be empty; a non-empty patch then means the committed lock files are stale.

To apply the patch to the Dependabot `dotnet-sdk` pull request (on Windows, with the new SDK installed):

```powershell
gh pr checkout <number>
git apply lock-files.patch            # from the health-sdk-canary artifact of a run with that SDK
./eng/Update-LockFiles.ps1 -Verify    # must pass with the new SDK
```

Then update the SDK version mentioned in `global.json`'s `errorMessage` and in the documentation, commit and push.
Dependabot stops rebasing a pull request once someone else pushes to it.

## Flaky tests

Required runs never retry a test. A test that fails intermittently is fixed or deleted in the pull request that finds
it: `--fail-skips on` makes a skipped test a failure, so it cannot be hidden with `Skip`. `test-repeat` is the weekly
detector for tests that pass once and fail on a later run: it builds the three modules whose tests drive the real Lua
state and the native bridge (`[Trait("Category", "NativeLua")]`) once in Debug, runs each five times, and writes a
pass/fail grid. Hang dumps are collected when `eng/Tests.props` references the HangDump extension.

## Release bridge rebuild

`Invoke-BridgeDriftCheck.ps1` rebuilds the native protection bridge of the newest release tag that ships one (the
v0.x tags of the former CESDK package do not), from the tag's committed `cheatengine_sdk_lua_bridge.c` and `xmake.lua`
bytes, twice, in two temporary directories of different depth outside the checkout, with the same toolchain pins as the
`native` job of `ci.yml` (`BRIDGE_VS_TOOLSET`, `BRIDGE_VS_SDKVER`, compared by `GovernanceWorkflowTests`). It compares
the result with the DLL committed at the tag and with `build/native/cheatengine-sdk-lua-bridge.dll` inside the released
package on nuget.org:

| Classification | Meaning | What to do |
|---|---|---|
| `Reproduced` | today's toolchain rebuilds the released bytes | nothing |
| `ToolchainDrift` | the sources and fingerprint match the release, the bytes differ | read the toolchain facts in the report (MSVC toolset, compiler, Windows SDK, runner image). The released package stays valid; the next release will carry other bytes, and its notes should say why |
| `Failed` | the two rebuilds differ (path dependence), the rebuild does not export the tag's source fingerprint, the tag's committed DLL is not the released one, or the package could not be read | investigate before the next release: the release is not reproducible as recorded |

Run it locally (Windows, xmake 3.0.9 and MSVC):

```powershell
./eng/ci/health/Invoke-BridgeDriftCheck.ps1 -Tag v1.0.0 -OutputDirectory "$env:TEMP/drift"
```

## External links

`Test-ExternalLink.ps1` requests every external `http(s)` link of the tracked Markdown files (fenced code, code spans,
local hosts, example domains and templated URLs are skipped; links into this repository's `main` branch are checked offline by the
documentation tests). 404 and 410 are broken; rate limiting, server errors and timeouts are inconclusive warnings. A
pull request never depends on an external site.

## Running the scripts locally

Every script runs on a developer machine from PowerShell 7, from the repository root, after the .NET SDK of
`global.json` is installed. Two of them change the working tree and belong in a throwaway clone:

```powershell
git clone --no-hardlinks . "$env:TEMP/canary"
Set-Location "$env:TEMP/canary"
./eng/ci/health/Set-CanarySdkVersion.ps1 -SdkVersion 10.0.401     # rewrites global.json in the clone
./eng/ci/health/Invoke-SdkCanary.ps1 -SkipTests -OutputDirectory "$env:TEMP/canary-out"
```

The others only write under `-OutputDirectory` (default `artifacts/health/<job>`, which git ignores):

```powershell
./eng/ci/health/Invoke-VulnerabilityAudit.ps1
./eng/ci/health/Invoke-TestRepeat.ps1 -Iterations 2
./eng/ci/health/Test-ExternalLink.ps1
```

`Publish-HealthIssue.ps1` writes to GitHub and runs in the workflow only.

## Scheduling and validation

- GitHub disables a scheduled workflow of a public repository after 60 days without repository activity; re-enable it
  from the Actions tab
  ([events that trigger workflows](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows)).
- A workflow can be dispatched only once it is on the default branch, so the first run of this workflow happens after
  it is merged: `gh workflow run scheduled-health.yml`, then check each job summary.
- The `canary` job may pass `cache: 'true'` to the composite action (the one workflow allowed to); it does not, so every
  health run restores from nuget.org like the release path.
