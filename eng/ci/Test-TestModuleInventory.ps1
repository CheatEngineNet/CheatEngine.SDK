#Requires -Version 7.0
<#
.SYNOPSIS
    Proves that every test module of the repository ran and produced its reports.

.DESCRIPTION
    `dotnet test --solution` only runs the modules the solution lists, and a module that is dropped, renamed or fails
    to start leaves no failing test behind. This script compares two sets:
      - expected: the base names of every tests/**/*.Tests.csproj known to git (tracked, plus untracked non-ignored
        files so a new module is checked before its first commit);
      - produced: the <assembly> part of every deterministic TRX name <assembly>_<tfm>_<arch>.trx at the top of the
        results directory (Microsoft.Testing.Extensions.TrxReport 2.3.0 or later).
    They must be equal, and every module must have executed at least one test. With -RequireCoverage, the directory
    must also hold exactly one coverage XML file per module.

    It then prints, and appends to the GitHub step summary, one row per module with its passed, failed and skipped
    counts, read from the TRX ResultSummary counters.

.PARAMETER ResultsDirectory
    The --results-directory of the dotnet test run.

.PARAMETER Configuration
    Debug or Release; used in the summary title.

.PARAMETER RequireCoverage
    Also require one coverage report (*.xml) per module (the Debug leg).

.EXAMPLE
    ./eng/ci/Test-TestModuleInventory.ps1 -ResultsDirectory artifacts/test-results/Debug -Configuration Debug -RequireCoverage
#>
[CmdletBinding()]
param(
	[Parameter(Mandatory)]
	[string] $ResultsDirectory,

	[Parameter(Mandatory)]
	[ValidateSet('Debug', 'Release')]
	[string] $Configuration,

	[switch] $RequireCoverage
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$InformationPreference = 'Continue'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$trxName = '^(?<module>.+)_(?<tfm>net\d+\.\d+)_(?<arch>x64|x86|arm64)\.trx$'

function Get-GitFile {
	param([Parameter(Mandatory)] [string[]] $Arguments)

	$files = @(& git -C $repositoryRoot ls-files @Arguments)
	if ($LASTEXITCODE -ne 0) {
		throw "git ls-files $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
	}
	return $files
}

# In git pathspecs '*' also matches '/', so this finds the test projects at any depth under tests/.
$projects = @(Get-GitFile -Arguments @('--', 'tests/*.Tests.csproj')) +
	@(Get-GitFile -Arguments @('--others', '--exclude-standard', '--', 'tests/*.Tests.csproj'))
$expected = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
foreach ($project in $projects) {
	[void] $expected.Add([IO.Path]::GetFileNameWithoutExtension($project))
}
if ($expected.Count -eq 0) {
	throw "No tests/**/*.Tests.csproj found under '$repositoryRoot'."
}

$directory = [IO.Path]::GetFullPath($ResultsDirectory, $PWD.Path)
if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
	throw "The results directory '$directory' does not exist: the test run produced nothing."
}

$produced = [Collections.Generic.SortedDictionary[string, IO.FileInfo]]::new([StringComparer]::Ordinal)
$unrecognized = [Collections.Generic.List[string]]::new()
foreach ($file in @(Get-ChildItem -LiteralPath $directory -Filter '*.trx' -File)) {
	if ($file.Name -notmatch $trxName) {
		$unrecognized.Add($file.Name)
		continue
	}
	$module = $Matches['module']
	if ($produced.ContainsKey($module)) {
		throw "Module $module produced more than one TRX file: $($produced[$module].Name) and $($file.Name)."
	}
	$produced.Add($module, $file)
}

$rows = [Collections.Generic.List[string]]::new()
$problems = [Collections.Generic.List[string]]::new()
$missing = @($expected | Where-Object { -not $produced.ContainsKey($_) })
$unexpected = @($produced.Keys | Where-Object { -not $expected.Contains($_) })
if ($missing.Count -gt 0) {
	$problems.Add("No test report for: $($missing -join ', '). Each tests/**/*.Tests.csproj must be in CheatEngine.SDK.slnx and run.")
}
if ($unexpected.Count -gt 0) {
	$problems.Add("Test reports without a tests/**/*.Tests.csproj: $($unexpected -join ', ').")
}
if ($unrecognized.Count -gt 0) {
	$problems.Add("TRX files without the deterministic <assembly>_<tfm>_<arch>.trx name: $($unrecognized -join ', ').")
}

$totals = @{ passed = 0; failed = 0; skipped = 0 }
foreach ($module in $produced.Keys) {
	[xml] $trx = [IO.File]::ReadAllText($produced[$module].FullName)
	$counters = $trx.TestRun.ResultSummary.Counters
	$executed = [int] $counters.executed
	$passed = [int] $counters.passed
	$failed = [int] $counters.failed + [int] $counters.error + [int] $counters.timeout + [int] $counters.aborted
	$skipped = [int] $counters.notExecuted
	$totals.passed += $passed
	$totals.failed += $failed
	$totals.skipped += $skipped
	if ($executed -eq 0) {
		$problems.Add("$module executed no test.")
	}
	$rows.Add("| $module | $passed | $failed | $skipped |")
}

if ($RequireCoverage) {
	$coverage = @(Get-ChildItem -LiteralPath $directory -Filter '*.xml' -File)
	if ($coverage.Count -ne $produced.Count) {
		$problems.Add("Expected one coverage report per test module ($($produced.Count)), found $($coverage.Count).")
	}
}

$summary = @(
	"### Test modules ($Configuration)"
	''
	"$($produced.Count) of $($expected.Count) expected modules reported."
	''
	'| Module | Passed | Failed | Skipped |'
	'| --- | ---: | ---: | ---: |'
) + $rows + @("| **Total** | **$($totals.passed)** | **$($totals.failed)** | **$($totals.skipped)** |")
if ($env:GITHUB_STEP_SUMMARY) {
	$summary | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}
$summary | ForEach-Object { Write-Information $_ }

if ($problems.Count -gt 0) {
	foreach ($problem in $problems) {
		Write-Host "::error title=Test module inventory::$problem"
	}
	throw "The test module inventory failed: $($problems -join ' ')"
}
Write-Information "All $($expected.Count) test modules ran."
