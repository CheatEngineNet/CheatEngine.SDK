#Requires -Version 7.0
<#
.SYNOPSIS
    Runs a pinned, hash-verified PSScriptAnalyzer over every PowerShell script of the repository.

.DESCRIPTION
    The CI lint job and developers run the same analysis:
      1. PSScriptAnalyzer $moduleVersion is downloaded from the PowerShell Gallery (a nupkg), its SHA-256 is compared
         with the pinned value, and the verified package is expanded and imported from -ModuleDirectory. Nothing is
         installed into a PowerShell module path.
         https://learn.microsoft.com/powershell/gallery/how-to/working-with-packages/manual-download
      2. Every *.ps1 script and *.psm1 module known to git (tracked, plus untracked non-ignored files) is analyzed with
         eng/PSScriptAnalyzerSettings.psd1.
      3. Every Error, Warning or ParseError record fails the run, unless the allowlist below names that file and rule
         with a reason. An allowlist entry that no longer matches a record also fails, so the list only shrinks.
    -EnableExit is not used: it counts error records only.
    https://learn.microsoft.com/powershell/module/psscriptanalyzer/invoke-scriptanalyzer

.PARAMETER ModuleDirectory
    Where the verified PSScriptAnalyzer package is downloaded and expanded. Reused when it already holds the pinned
    package with the pinned hash.

.PARAMETER Settings
    The settings file.

.EXAMPLE
    ./eng/ci/Invoke-ScriptAnalysis.ps1 -ModuleDirectory $env:TEMP/psscriptanalyzer
#>
[CmdletBinding()]
param(
	[string] $ModuleDirectory = (Join-Path ($env:RUNNER_TEMP ?? [IO.Path]::GetTempPath()) 'psscriptanalyzer'),

	[string] $Settings = 'eng/PSScriptAnalyzerSettings.psd1'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$InformationPreference = 'Continue'

$moduleVersion = '1.25.0'
# SHA-256 of https://www.powershellgallery.com/api/v2/package/PSScriptAnalyzer/1.25.0 (14 658 674 bytes; the gallery's
# SHA-512 5/tXMmLmBLqymRSuYpIdJLl8L4i1GbQSd7QD72zhY/FLfRs23HNEmmjlrlqNlQKJGxDCZBMf0YE63ym/ExwWYw== matches it).
$moduleSha256 = '14e634c828eb98efb9f40b2918ba90f139ed5eccdf663a2a747736d996995d60'

# Findings accepted for one file, each with the reason and the work that removes it.
$allowlist = @(
	[pscustomobject] @{
		Path = 'eng/lua-bridge/Test-ProtectedOperationCatalog.ps1'
		Rule = 'PSUseApprovedVerbs'
		Reason = 'Require-Property and Escape-Regex go away when the protected-operation catalogue script is rebuilt (audit A20-Q13).'
	}
)

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$moduleRoot = [IO.Path]::GetFullPath($ModuleDirectory, $PWD.Path)
$package = Join-Path $moduleRoot "PSScriptAnalyzer.$moduleVersion.nupkg"
$expanded = Join-Path $moduleRoot "PSScriptAnalyzer/$moduleVersion"
$manifest = Join-Path $expanded 'PSScriptAnalyzer.psd1'

New-Item -ItemType Directory -Path $moduleRoot -Force | Out-Null
if (-not (Test-Path -LiteralPath $package -PathType Leaf)) {
	$uri = "https://www.powershellgallery.com/api/v2/package/PSScriptAnalyzer/$moduleVersion"
	Invoke-WebRequest -Uri $uri -OutFile $package -MaximumRetryCount 3 -RetryIntervalSec 5
}
$actualSha256 = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualSha256 -cne $moduleSha256) {
	Remove-Item -LiteralPath $package -Force
	throw "PSScriptAnalyzer $moduleVersion has SHA-256 $actualSha256, expected $moduleSha256."
}
if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) {
	Expand-Archive -LiteralPath $package -DestinationPath $expanded -Force
}
Import-Module -Name $manifest -Force
$loaded = Get-Module -Name PSScriptAnalyzer
if ($null -eq $loaded -or $loaded.Version -ne [version] $moduleVersion) {
	throw "Expected PSScriptAnalyzer $moduleVersion to be loaded from '$manifest'."
}

$settingsPath = [IO.Path]::GetFullPath($Settings, $repositoryRoot)
$scripts = @(& git -C $repositoryRoot ls-files --cached --others --exclude-standard -- '*.ps1' '*.psm1')
if ($LASTEXITCODE -ne 0) {
	throw "git ls-files failed with exit code $LASTEXITCODE."
}
$scripts = @($scripts | Sort-Object -Unique)
if ($scripts.Count -eq 0) {
	throw "No *.ps1 or *.psm1 file found under '$repositoryRoot'."
}

$failures = [Collections.Generic.List[string]]::new()
$allowed = [Collections.Generic.List[string]]::new()
$usedAllowlist = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($script in $scripts) {
	$records = @(Invoke-ScriptAnalyzer -Path (Join-Path $repositoryRoot $script) -Settings $settingsPath)
	foreach ($record in $records) {
		if ("$($record.Severity)" -notin @('Error', 'Warning', 'ParseError')) {
			continue
		}
		$location = "${script}:$($record.Line)"
		$entry = $allowlist | Where-Object { $_.Path -ceq $script -and $_.Rule -ceq $record.RuleName } | Select-Object -First 1
		if ($null -ne $entry) {
			[void] $usedAllowlist.Add("$($entry.Path)|$($entry.Rule)")
			$allowed.Add("$location $($record.RuleName): allowlisted ($($entry.Reason))")
			continue
		}
		$failures.Add("$location $($record.RuleName) [$($record.Severity)]: $($record.Message)")
		Write-Host "::error file=$script,line=$($record.Line),title=$($record.RuleName)::$($record.Message)"
	}
}
foreach ($entry in $allowlist) {
	if (-not $usedAllowlist.Contains("$($entry.Path)|$($entry.Rule)")) {
		$failures.Add("Allowlist entry $($entry.Path) $($entry.Rule) no longer matches a finding: remove it from eng/ci/Invoke-ScriptAnalysis.ps1.")
	}
}

$allowed | ForEach-Object { Write-Information $_ }
if ($env:GITHUB_STEP_SUMMARY) {
	@(
		'### PSScriptAnalyzer'
		''
		"PSScriptAnalyzer $moduleVersion over $($scripts.Count) scripts: $($failures.Count) failing and $($allowed.Count) allowlisted records."
	) | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}
if ($failures.Count -gt 0) {
	$failures | ForEach-Object { Write-Information $_ }
	throw "PSScriptAnalyzer reported $($failures.Count) finding(s) in $($scripts.Count) scripts."
}
Write-Information "PSScriptAnalyzer $moduleVersion`: $($scripts.Count) scripts clean ($($allowed.Count) allowlisted records)."
