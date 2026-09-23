#Requires -Version 7.0
<#
.SYNOPSIS
    Runs the threading-sensitive test modules several times in a row and fails when any run fails.

.DESCRIPTION
    Required CI never retries a test (flaky-test policy, eng/ci/health/README.md): a test that fails intermittently is
    fixed or deleted in the pull request that finds it, because --fail-skips on forbids hiding it with Skip. This script
    is the weekly detector for tests that pass once and fail on a later run. It targets the modules whose tests drive the
    real Lua state and the native bridge ([Trait("Category", "NativeLua")]): Lua, Lua.Interop and Hosting.

    1. Restore each project in locked mode and build it once in Debug.
    2. For each iteration and project: `dotnet test --project <p> -c Debug --no-build --fail-skips on --report-trx` into
       <OutputDirectory>/<iteration>/<project>, with --hangdump when eng/Tests.props references the HangDump extension
       (without it, Microsoft.Testing.Platform rejects the option with exit code 5).
    3. Write a pass/fail grid (projects x iterations, with the failed-test count from each TRX report) to summary.md and
       the job summary, and fail when any run failed.

.PARAMETER Project
    Test projects to repeat (repository-relative .csproj paths). Defaults to the three NativeLua modules.

.PARAMETER Iterations
    Number of runs per project.

.PARAMETER OutputDirectory
    Where the TRX reports, dumps and summary.md are written.

.EXAMPLE
    ./eng/ci/health/Invoke-TestRepeat.ps1 -Iterations 2 -OutputDirectory "$env:TEMP/repeat"
#>
[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string[]] $Project = @(
        'tests/CheatEngine.SDK.Lua.Tests/CheatEngine.SDK.Lua.Tests.csproj',
        'tests/CheatEngine.SDK.Lua.Interop.Tests/CheatEngine.SDK.Lua.Interop.Tests.csproj',
        'tests/CheatEngine.SDK.Hosting.Tests/CheatEngine.SDK.Hosting.Tests.csproj'
    ),

    [ValidateRange(1, 50)]
    [int] $Iterations = 5,

    [string] $OutputDirectory = 'artifacts/health/test-repeat'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module -Name (Join-Path -Path $PSScriptRoot -ChildPath 'HealthCheck.psm1') -Force
Import-Module -Name (Join-Path -Path $PSScriptRoot -ChildPath 'HealthCommand.psm1') -Force

$root = Get-HealthRepositoryRoot
$output = New-HealthOutputDirectory -Path $OutputDirectory
$testsProps = Get-Content -Raw -LiteralPath (Join-Path -Path $root -ChildPath 'eng/Tests.props')
$dumpArguments = @(
    if (Test-PackageReference -ProjectText $testsProps -PackageId 'Microsoft.Testing.Extensions.HangDump') {
        '--hangdump', '--hangdump-timeout', '10m'
    })

# results[project][iteration] = cell text; a failed run keeps its exit code and failed-test count.
$results = [ordered]@{}
$failedRuns = 0

Push-Location -LiteralPath $root
try {
    foreach ($testProject in $Project) {
        if (-not (Test-Path -LiteralPath (Join-Path -Path $root -ChildPath $testProject) -PathType Leaf)) {
            throw "The test project '$testProject' does not exist."
        }

        Invoke-NativeCommand -FilePath 'dotnet' -ArgumentList @('restore', $testProject, '--locked-mode') -Description "Locked restore of $testProject"
        Invoke-NativeCommand -FilePath 'dotnet' -ArgumentList @('build', $testProject, '-c', 'Debug', '--no-restore') -Description "Debug build of $testProject"
        $results[$testProject] = [System.Collections.Generic.List[string]]::new()
    }

    for ($iteration = 1; $iteration -le $Iterations; $iteration++) {
        foreach ($testProject in $Project) {
            $name = [System.IO.Path]::GetFileNameWithoutExtension($testProject)
            $resultsDirectory = Join-Path -Path $output -ChildPath "$iteration/$name"
            $arguments = @(
                'test', '--project', $testProject, '-c', 'Debug', '--no-build', '--fail-skips', 'on', '--report-trx',
                '--results-directory', $resultsDirectory) + $dumpArguments
            $exitCode = Invoke-NativeCommand -FilePath 'dotnet' -ArgumentList $arguments -AllowFailure `
                -Description "Iteration $iteration of $name"

            $failedTests = 0
            foreach ($report in @(Get-ChildItem -LiteralPath $resultsDirectory -Filter '*.trx' -File -Recurse -ErrorAction SilentlyContinue)) {
                $failedTests += (Get-TrxSummary -Xml (Get-Content -Raw -LiteralPath $report.FullName)).Failed
            }

            if ($exitCode -eq 0) {
                $results[$testProject].Add('Passed')
            }
            else {
                $failedRuns++
                $results[$testProject].Add("**Failed** ($failedTests failed, exit code $exitCode)")
            }
        }
    }
}
finally {
    Pop-Location
}

$header = '| Project | ' + ((1..$Iterations | ForEach-Object { "Run $_" }) -join ' | ') + ' |'
$separator = '| --- |' + (' --- |' * $Iterations)
$summary = @(
    '## Repeated threading-sensitive tests',
    '',
    "$Iterations runs of each module in Debug, with --fail-skips on$(if ($dumpArguments.Count) { ' and hang dumps' }).",
    '',
    $header,
    $separator
)
foreach ($testProject in $results.Keys) {
    $summary += "| $([System.IO.Path]::GetFileNameWithoutExtension($testProject)) | $($results[$testProject] -join ' | ') |"
}

Write-HealthSummary -Line $summary
[System.IO.File]::WriteAllLines((Join-Path -Path $output -ChildPath 'summary.md'), [string[]] $summary, [System.Text.UTF8Encoding]::new($false))

if ($failedRuns -gt 0) {
    throw "$failedRuns of $($Iterations * $Project.Count) test runs failed: a test is flaky or broken. Fix or delete it (eng/ci/health/README.md#flaky-tests)."
}
