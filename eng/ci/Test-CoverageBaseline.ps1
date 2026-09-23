#Requires -Version 7.0
<#
.SYNOPSIS
    Merges the per-module coverage reports and holds each shipping assembly to its line-coverage floor.

.DESCRIPTION
    The Debug leg writes one Microsoft Code Coverage XML report per test module (the format SonarQube Cloud reads).
    This script:
      1. merges them with the pinned dotnet-coverage tool (.config/dotnet-tools.json) into a Cobertura report, the
         line union of every module of the same build;
      2. keeps the shipping assemblies only, the ProjectReference items of src/CheatEngine.SDK/CheatEngine.SDK.csproj
         (test assemblies are excluded by the collector), and drops generated sources (*.g.cs, anything under obj/),
         which Sonar does not count either;
      3. compares each assembly's line coverage with its floor in eng/coverage-baseline.json, minus the file's
         tolerance (percentage points), and fails naming the assembly, floor and value when one is below;
      4. writes a Markdown summary and suggested-coverage-baseline.json (every value floored to 0.1) to the output
         directory and the step summary. CI never edits eng/coverage-baseline.json: raising a floor is a reviewed
         commit that copies the suggestion.

    Line coverage is the metric because it is what survives the merge: the collector records blocks, not branch
    conditions, and dotnet-coverage merges by line (the merged report has no block counts and marks no line as a
    branch), so a branch floor would always read 100 %.

.PARAMETER ResultsDirectory
    The Debug --results-directory; its top-level *.xml files are the per-module reports.

.PARAMETER Cobertura
    An already merged Cobertura report, instead of -ResultsDirectory.

.PARAMETER Baseline
    The floors file.

.PARAMETER OutputDirectory
    Receives merged.cobertura.xml, summary.md and suggested-coverage-baseline.json.

.EXAMPLE
    ./eng/ci/Test-CoverageBaseline.ps1 -ResultsDirectory artifacts/test-results/Debug
#>
[CmdletBinding(DefaultParameterSetName = 'Results')]
param(
	[Parameter(Mandatory, ParameterSetName = 'Results')]
	[string] $ResultsDirectory,

	[Parameter(Mandatory, ParameterSetName = 'Merged')]
	[string] $Cobertura,

	[string] $Baseline = 'eng/coverage-baseline.json',

	[string] $OutputDirectory = 'artifacts/coverage-report'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$InformationPreference = 'Continue'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$baselineSchema = 'cheatengine-coverage-baseline/v0'
$packageProject = 'src/CheatEngine.SDK/CheatEngine.SDK.csproj'
$env:DOTNET_COVERAGE_TELEMETRY_OPTOUT = '1'
$env:DOTNET_COVERAGE_NOLOGO = '1'

function Get-FlooredPercent {
	param([Parameter(Mandatory)] [double] $Value)

	return [Math]::Floor($Value * 10) / 10
}

# The shipping assemblies are exactly what the package embeds or packs as analyzers.
[xml] $project = [IO.File]::ReadAllText((Join-Path $repositoryRoot $packageProject))
$shipping = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
foreach ($reference in @($project.Project.ItemGroup | ForEach-Object { $_.ChildNodes } | Where-Object { $_.LocalName -eq 'ProjectReference' })) {
	[void] $shipping.Add([IO.Path]::GetFileNameWithoutExtension($reference.Include.Replace('\', '/')))
}
if ($shipping.Count -eq 0) {
	throw "$packageProject lists no ProjectReference."
}

$baselinePath = [IO.Path]::GetFullPath($Baseline, $PWD.Path)
$floors = Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json -AsHashtable
if ($floors['schema'] -cne $baselineSchema) {
	throw "$Baseline must declare schema '$baselineSchema'."
}
$tolerance = [double] $floors['tolerance']
if ($tolerance -lt 0 -or $tolerance -gt 5) {
	throw "$Baseline tolerance must be between 0 and 5 percentage points, found $tolerance."
}
$listed = [Collections.Generic.SortedSet[string]]::new([string[]] @($floors['assemblies'].Keys), [StringComparer]::Ordinal)
if (-not $listed.SetEquals($shipping)) {
	throw "$Baseline must list exactly the shipping assemblies of ${packageProject}: $($shipping -join ', ')."
}

$output = [IO.Path]::GetFullPath($OutputDirectory, $PWD.Path)
New-Item -ItemType Directory -Path $output -Force | Out-Null
if ($PSCmdlet.ParameterSetName -eq 'Results') {
	$reports = @(Get-ChildItem -LiteralPath ([IO.Path]::GetFullPath($ResultsDirectory, $PWD.Path)) -Filter '*.xml' -File |
			ForEach-Object FullName)
	if ($reports.Count -eq 0) {
		throw "No coverage report (*.xml) in '$ResultsDirectory'."
	}
	$Cobertura = Join-Path $output 'merged.cobertura.xml'
	Push-Location -LiteralPath $repositoryRoot
	try {
		dotnet tool restore | Out-Host
		if ($LASTEXITCODE -ne 0) {
			throw "dotnet tool restore failed with exit code $LASTEXITCODE."
		}
		dotnet tool run dotnet-coverage merge --output $Cobertura --output-format cobertura @reports | Out-Host
		if ($LASTEXITCODE -ne 0) {
			throw "dotnet-coverage merge failed with exit code $LASTEXITCODE."
		}
	}
	finally {
		Pop-Location
	}
}

[xml] $report = [IO.File]::ReadAllText([IO.Path]::GetFullPath($Cobertura, $PWD.Path))
$measured = @{}
foreach ($package in @($report.coverage.packages.package)) {
	if (-not $shipping.Contains($package.name)) {
		continue
	}
	# A (file, line) pair can appear under several classes (partial and nested types); it is covered once any is hit.
	$lines = @{}
	foreach ($class in @($package.classes.class)) {
		$file = ([string] $class.filename).Replace('\', '/')
		if ($file.EndsWith('.g.cs', [StringComparison]::OrdinalIgnoreCase) -or $file -match '(^|/)obj/') {
			continue
		}
		foreach ($line in @($class.lines.line)) {
			$key = "$file|$($line.number)"
			$lines[$key] = ($lines[$key] -eq $true) -or ([long] $line.hits -gt 0)
		}
	}
	$covered = @($lines.Values | Where-Object { $_ }).Count
	$measured[$package.name] = [pscustomobject] @{
		Covered = $covered
		Valid = $lines.Count
		Percent = if ($lines.Count -eq 0) { 0.0 } else { 100.0 * $covered / $lines.Count }
	}
}

$problems = [Collections.Generic.List[string]]::new()
$rows = [Collections.Generic.List[string]]::new()
$suggested = [ordered]@{}
$totalCovered = 0
$totalValid = 0
foreach ($assembly in $shipping) {
	$floor = [double] $floors['assemblies'][$assembly]['line']
	if (-not $measured.ContainsKey($assembly)) {
		$problems.Add("$assembly was not measured: no test module loaded it with coverage.")
		$rows.Add("| $assembly | not measured | $floor | failed |")
		$suggested[$assembly] = [ordered]@{ line = $floor }
		continue
	}
	$actual = $measured[$assembly]
	$totalCovered += $actual.Covered
	$totalValid += $actual.Valid
	$percent = [Math]::Round($actual.Percent, 2)
	$status = 'ok'
	if ($actual.Percent -lt $floor - $tolerance) {
		$status = 'below floor'
		$problems.Add("$assembly line coverage is $percent %, below its floor of $floor % (tolerance $tolerance points).")
	}
	elseif ((Get-FlooredPercent -Value $actual.Percent) -gt $floor) {
		$status = 'above floor: raise it'
	}
	$rows.Add("| $assembly | $percent ($($actual.Covered)/$($actual.Valid)) | $floor | $status |")
	$suggested[$assembly] = [ordered]@{ line = [Math]::Max($floor, (Get-FlooredPercent -Value $actual.Percent)) }
}

$suggestion = [ordered]@{
	schema = $baselineSchema
	tolerance = $tolerance
	assemblies = $suggested
}
$suggestionJson = ConvertTo-Json -InputObject $suggestion -Depth 4
[IO.File]::WriteAllText((Join-Path $output 'suggested-coverage-baseline.json'), $suggestionJson + "`n", [Text.UTF8Encoding]::new($false))

$overall = if ($totalValid -eq 0) { 0 } else { [Math]::Round(100.0 * $totalCovered / $totalValid, 2) }
$summary = @(
	'### Coverage (shipping assemblies, line union of every test module)'
	''
	'| Assembly | Line % (covered/valid) | Floor % | Status |'
	'| --- | ---: | ---: | --- |'
) + $rows + @(
	"| **All shipping assemblies** | **$overall ($totalCovered/$totalValid)** | | |"
	''
	"Tolerance: $tolerance percentage points. Generated sources (*.g.cs, obj/) are excluded."
	''
	'<details><summary>Suggested eng/coverage-baseline.json (CI never writes it)</summary>'
	''
	'```json'
	$suggestionJson
	'```'
	''
	'</details>'
)
[IO.File]::WriteAllLines((Join-Path $output 'summary.md'), [string[]] $summary, [Text.UTF8Encoding]::new($false))
if ($env:GITHUB_STEP_SUMMARY) {
	$summary | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}
$summary | ForEach-Object { Write-Information $_ }

if ($problems.Count -gt 0) {
	foreach ($problem in $problems) {
		Write-Host "::error title=Coverage floor::$problem"
	}
	throw "Coverage fell below the floors of ${Baseline}: $($problems -join ' ')"
}
