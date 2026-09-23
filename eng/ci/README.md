# CI scripts

The scripts that the jobs of [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml) run. Each one also runs on a
developer machine (PowerShell 7, the exact .NET SDK of `global.json`), so a red CI step can be reproduced locally with
the same command. The workflow contract they implement is frozen by the C# tests in
[`tests/CheatEngine.SDK.Repository.Tests/Workflows`](../../tests/CheatEngine.SDK.Repository.Tests/Workflows) and by
[`NativeBridgePeAuditTests`](../../tests/CheatEngine.SDK.Tests/Packaging/NativeBridgePeAuditTests.cs); a rule that only a
script checked would silently rot, so every script either is the CI step itself or is asserted by those tests.

| Script | Job, step | What it proves |
|---|---|---|
| `Build-NativeBridge.ps1` | `native`, Build bridge | The Lua protection bridge builds with the pinned xmake, MSVC toolset and Windows SDK; a second build and a build from a copy of the two inputs in another directory have the same SHA-256 (reproducible, path independent); the PE header records the linker of the pinned toolset. Writes the toolchain facts to the step summary and the job outputs. |
| `Test-SdkPackage.ps1` | `build-test` (Release), Pack | The Release leg packed exactly one `CheatEngine.SDK.<version>.nupkg` (the exact name when a release version is required), with its nuspec identity, the embedded SPDX SBOM and the CI-built bridge. Exposes the file and its SHA-256 to the packaging tests. |
| `New-BuildInfo.ps1` | `build-test` (Release), Write build info | Writes `build-info.json` ([`build-info.v0.schema.json`](build-info.v0.schema.json)): source, run, .NET SDK, runner image, bridge toolchain and package hashes. |
| `Test-TestModuleInventory.ps1` | `build-test`, Check test module inventory | Every `tests/**/*.Tests.csproj` produced its TRX report, ran at least one test and, in Debug, wrote its coverage report. |
| `Test-CoverageBaseline.ps1` | `build-test` (Debug), Check coverage floors | Merges the per-module coverage with the pinned `dotnet-coverage` and holds every shipping assembly to its floor in [`eng/coverage-baseline.json`](../coverage-baseline.json). |
| `Invoke-ScriptAnalysis.ps1` | `lint`, Run PSScriptAnalyzer | Every tracked `*.ps1` passes a hash-verified PSScriptAnalyzer with [`eng/PSScriptAnalyzerSettings.psd1`](../PSScriptAnalyzerSettings.psd1). |
| `Invoke-ClientCanary.ps1` | `client-canary` (advisory, not in the gate) | Builds CheatEngine.Client against the package of the run and reports every error ([`client-canary-report.v0.schema.json`](client-canary-report.v0.schema.json)). It never fails the run because the Client breaks. |

## Run them locally

```powershell
# Native bridge (needs xmake 3.0.9 and the MSVC 14.44 toolset with the Windows SDK 10.0.26100.0).
./eng/ci/Build-NativeBridge.ps1 -VsToolset 14.44 -VsSdkVersion 10.0.26100.0

# The Debug leg of build-test.
dotnet test --solution CheatEngine.SDK.slnx -c Debug --no-build --results-directory artifacts/test-results/Debug `
  --fail-skips on --report-trx --hangdump --hangdump-timeout 15m --crashdump `
  --coverage --coverage-output-format xml --filter-not-trait "Category=Packaging"
./eng/ci/Test-TestModuleInventory.ps1 -ResultsDirectory artifacts/test-results/Debug -Configuration Debug -RequireCoverage
./eng/ci/Test-CoverageBaseline.ps1 -ResultsDirectory artifacts/test-results/Debug

# The Release pack checks.
dotnet pack src/CheatEngine.SDK -c Release --no-restore -o artifacts/nuget
./eng/ci/Test-SdkPackage.ps1 -PackageDirectory artifacts/nuget

# Lint.
./eng/ci/Invoke-ScriptAnalysis.ps1
```

`New-BuildInfo.ps1` reads the GitHub Actions environment (`GITHUB_*`, `ImageOS`, `ImageVersion`) and the
`BUILD_INFO_*` variables its help lists; set them by hand to try it outside CI. Consume a locally packed CheatEngine.SDK
only through an isolated `NUGET_PACKAGES` folder: MinVer gives every worktree at the same height the same version, and
the global package cache must never hold a branch package.

## Coverage floors

The Debug leg fails when an assembly's line coverage drops below its floor minus the tolerance of
`eng/coverage-baseline.json`. CI never raises a floor: the step summary and the `coverage-report` artifact carry
`suggested-coverage-baseline.json`, and raising the floors is a reviewed commit that copies it. Line coverage is the only
metric because the merged report keeps no branch data.

## Native bridge drift

A CI-built bridge whose bytes differ from the checked-in
`native/cheatengine-sdk-lua-bridge/runtimes/win-x64/native/cheatengine-sdk-lua-bridge.dll` is reported as a notice, never
as a failure: the checked-in DLL is refreshed deliberately, from the `lua-protection-bridge` artifact of a CI run. A
checked-in DLL built from other sources fails the `native` job (source fingerprint check).
