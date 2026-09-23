#Requires -Version 7.0
<#
.SYNOPSIS
    Second stage of the newest-SDK canary: regenerates the lock files with the SDK global.json now selects, then
    builds, packs and tests the solution with it, and keeps the lock-file patch.

.DESCRIPTION
    Runs after Set-CanarySdkVersion.ps1 and the composite setup action installed the SDK that global.json names:

    1. Check that the active SDK is the one global.json selects (rollForward: disable), and read the committed pin from
       HEAD's global.json.
    2. Regenerate every lock file with ./eng/Update-LockFiles.ps1, the repository's single regeneration sequence (it
       restores with --force-evaluate, never together with --locked-mode, NU1005), and write
       `git diff -- '*packages.lock.json'` to lock-files.patch. An empty patch is a valid result. With the pinned SDK
       the patch must be empty: otherwise the committed lock files are stale, and the canary fails.
    3. Build the solution in Release, pack src/CheatEngine.SDK, and, unless -SkipTests, run every test module in
       Release with CESDK_PACKAGED_UMBRELLA_NUPKG set to the packed file, so the packaging tests consume exactly that
       package (shared-contracts 1.8), and --fail-skips on.
    4. Write summary.md (pinned and canary SDK, changed lock files, build, pack and test totals) to the output folder
       and the job summary.

    Only a failure of the build, the pack or a test fails the canary; a non-empty patch with a newer SDK is the expected
    result and is attached to the health-sdk-canary artifact. How to apply it: eng/ci/health/README.md.

.PARAMETER OutputDirectory
    Where lock-files.patch, summary.md, the package and the TRX reports are written.

.PARAMETER SkipTests
    Stop after the pack (local rehearsals: the packaging tests take minutes).

.EXAMPLE
    ./eng/ci/health/Invoke-SdkCanary.ps1 -SkipTests -OutputDirectory "$env:TEMP/canary"
#>
[CmdletBinding()]
param(
    [string] $OutputDirectory = 'artifacts/health/sdk-canary',
    [switch] $SkipTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module -Name (Join-Path -Path $PSScriptRoot -ChildPath 'HealthCheck.psm1') -Force
Import-Module -Name (Join-Path -Path $PSScriptRoot -ChildPath 'HealthCommand.psm1') -Force

$root = Get-HealthRepositoryRoot
$output = New-HealthOutputDirectory -Path $OutputDirectory
$solution = 'CheatEngine.SDK.slnx'
$package = 'src/CheatEngine.SDK/CheatEngine.SDK.csproj'

$selected = [string] ((Get-Content -Raw -LiteralPath (Join-Path -Path $root -ChildPath 'global.json') | ConvertFrom-Json -AsHashtable)['sdk']['version'])
$pinned = [string] (((Get-GitOutput -ArgumentList @('show', 'HEAD:global.json')) -join "`n" | ConvertFrom-Json -AsHashtable)['sdk']['version'])
$active = @(Get-NativeCommandOutput -FilePath 'dotnet' -ArgumentList @('--version') -Description 'Reading the active .NET SDK version')[0].Trim()
if ($active -cne $selected) {
    throw "The active .NET SDK is $active, but global.json selects ${selected}: run the composite setup action (or install $selected) first."
}

$sameSdk = $selected -ceq $pinned
$status = [ordered]@{ Build = 'Not run'; Pack = 'Not run'; Tests = if ($SkipTests) { 'Skipped (-SkipTests)' } else { 'Not run' } }
$failures = [System.Collections.Generic.List[string]]::new()
$patchPath = Join-Path -Path $output -ChildPath 'lock-files.patch'
$changedLocks = @()

Push-Location -LiteralPath $root
try {
    & (Join-Path -Path $root -ChildPath 'eng/Update-LockFiles.ps1')
    if ($LASTEXITCODE -ne 0) {
        throw "eng/Update-LockFiles.ps1 failed with exit code $LASTEXITCODE."
    }

    [void] (Get-GitOutput -ArgumentList @('diff', "--output=$patchPath", '--', '*packages.lock.json'))
    $changedLocks = @(Get-GitOutput -ArgumentList @('diff', '--name-only', '--', '*packages.lock.json')) +
    @(Get-GitOutput -ArgumentList @('ls-files', '--others', '--exclude-standard', '--', '*packages.lock.json'))
    if ($sameSdk -and $changedLocks.Count -gt 0) {
        $failures.Add("With the pinned SDK $pinned, regenerating changed $($changedLocks.Count) committed lock file(s): run ./eng/Update-LockFiles.ps1 and commit the result.")
    }

    Invoke-NativeCommand -FilePath 'dotnet' -Description "Release build with SDK $selected" `
        -ArgumentList @('build', $solution, '-c', 'Release', '--no-restore')
    $status.Build = 'Succeeded'

    $packageDirectory = Join-Path -Path $output -ChildPath 'nuget'
    Invoke-NativeCommand -FilePath 'dotnet' -Description "Pack with SDK $selected" `
        -ArgumentList @('pack', $package, '-c', 'Release', '--no-build', '-o', $packageDirectory)
    $packed = @(Get-ChildItem -LiteralPath $packageDirectory -Filter 'CheatEngine.SDK.*.nupkg' -File)
    if ($packed.Count -ne 1) {
        throw "The pack produced $($packed.Count) CheatEngine.SDK packages in $packageDirectory; expected exactly one."
    }

    $status.Pack = "Succeeded ($($packed[0].Name))"

    if (-not $SkipTests) {
        $testArguments = @(
            'test', '--solution', $solution, '-c', 'Release', '--no-build', '--fail-skips', 'on', '--report-trx',
            '--results-directory', (Join-Path -Path $output -ChildPath 'test-results'))
        $testsProps = Get-Content -Raw -LiteralPath (Join-Path -Path $root -ChildPath 'eng/Tests.props')
        if (Test-PackageReference -ProjectText $testsProps -PackageId 'Microsoft.Testing.Extensions.HangDump') {
            $testArguments += @('--hangdump', '--hangdump-timeout', '15m')
        }

        $previousPackage = $env:CESDK_PACKAGED_UMBRELLA_NUPKG
        $env:CESDK_PACKAGED_UMBRELLA_NUPKG = $packed[0].FullName
        try {
            $testExitCode = Invoke-NativeCommand -FilePath 'dotnet' -ArgumentList $testArguments -AllowFailure `
                -Description "Release tests with SDK $selected"
        }
        finally {
            $env:CESDK_PACKAGED_UMBRELLA_NUPKG = $previousPackage
        }

        $totals = [ordered]@{ Total = 0; Passed = 0; Failed = 0; NotExecuted = 0 }
        foreach ($report in @(Get-ChildItem -LiteralPath (Join-Path -Path $output -ChildPath 'test-results') -Filter '*.trx' -File -Recurse -ErrorAction SilentlyContinue)) {
            $counters = Get-TrxSummary -Xml (Get-Content -Raw -LiteralPath $report.FullName)
            foreach ($name in @($totals.Keys)) {
                $totals[$name] += $counters.$name
            }
        }

        $status.Tests = "$(if ($testExitCode -eq 0) { 'Passed' } else { "Failed (exit code $testExitCode)" }): $($totals.Total) total, $($totals.Passed) passed, $($totals.Failed) failed, $($totals.NotExecuted) not executed"
        if ($testExitCode -ne 0) {
            $failures.Add("The Release tests failed with SDK $selected (exit code $testExitCode).")
        }
    }
}
catch {
    $failures.Add($_.Exception.Message)
}
finally {
    Pop-Location
}

$comparison = if ($sameSdk) {
    "The newest SDK equals the pinned SDK ($pinned): the canary rebuilt with the pinned SDK."
}
else {
    "The canary ran with SDK $selected; global.json pins $pinned."
}

$lockCell = [string] $changedLocks.Count
if ($changedLocks.Count -gt 0) {
    $lockCell += ': ' + (($changedLocks | ForEach-Object { '`' + $_ + '`' }) -join ', ')
}

$summary = @(
    '## Newest .NET SDK canary',
    '',
    $comparison,
    '',
    '| Step | Result |',
    '| --- | --- |',
    "| Pinned SDK (HEAD global.json) | ``$pinned`` |",
    "| Canary SDK | ``$selected`` |",
    "| Lock files changed | $lockCell |",
    "| Build (Release) | $($status.Build) |",
    "| Pack | $(ConvertTo-HealthTableCell -Text $status.Pack) |",
    "| Tests (Release) | $($status.Tests) |",
    ''
)
if ($changedLocks.Count -gt 0 -and -not $sameSdk) {
    $summary += 'The `health-sdk-canary` artifact holds `lock-files.patch` for the Dependabot `dotnet-sdk` pull request (eng/ci/health/README.md).'
}

foreach ($failure in $failures) {
    $summary += "- **Failed:** $(ConvertTo-HealthTableCell -Text $failure)"
}

Write-HealthSummary -Line $summary
[System.IO.File]::WriteAllLines((Join-Path -Path $output -ChildPath 'summary.md'), [string[]] $summary, [System.Text.UTF8Encoding]::new($false))

if ($failures.Count -gt 0) {
    throw "The SDK canary failed: $($failures -join ' ')"
}
