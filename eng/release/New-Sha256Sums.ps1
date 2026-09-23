#Requires -Version 7.4
<#
.SYNOPSIS
Writes a SHA256SUMS file for release assets, in the format 'sha256sum -c' and 'shasum -a 256 -c' read.

.DESCRIPTION
One line per asset: 64 lowercase hex digits, two spaces, the file name. Lines are sorted ordinally by name, end with
LF, and the file ends with a newline; UTF-8 without BOM. Every name must be a plain file name inside -Directory.
Under 'pwsh -File', where an array argument arrives as one string, -Name also accepts a comma-separated list.

.EXAMPLE
./eng/release/New-Sha256Sums.ps1 -Directory $pwd/artifacts/release -OutputPath $pwd/artifacts/release/SHA256SUMS `
  -Name CheatEngine.SDK.2.0.0.nupkg, CheatEngine.SDK.2.0.0.spdx.json
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)] [string] $Directory,
  [Parameter(Mandatory)] [string[]] $Name,
  [Parameter(Mandatory)] [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ReleaseTools.psm1') -Force

# One line per failure: readable in any console width and an annotation on the GitHub run.
trap {
  Write-Host "::error::$($_.Exception.Message)"
  exit 1
}

foreach ($path in @($Directory, $OutputPath)) {
  if (-not [IO.Path]::IsPathFullyQualified($path)) { throw "'$path' must be an absolute path." }
}
if (-not (Test-Path -LiteralPath $Directory -PathType Container)) { throw "'$Directory' is not a directory." }

$names = [Collections.Generic.List[string]]::new()
foreach ($item in $Name) {
  foreach ($part in $item.Split(',')) {
    $trimmed = $part.Trim()
    if ($trimmed.Length -gt 0) { $names.Add($trimmed) }
  }
}
if ($names.Count -eq 0) { throw 'Name at least one asset.' }

$hashes = [ordered]@{}
foreach ($asset in $names) {
  if ($asset -match '[\\/]' -or $asset -in @('.', '..')) { throw "'$asset' is not a plain file name." }
  if ($hashes.Contains($asset)) { throw "'$asset' is named twice." }
  $path = Join-Path $Directory $asset
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "'$asset' is not a file in '$Directory'." }
  if ([string]::Equals([IO.Path]::GetFullPath($path), [IO.Path]::GetFullPath($OutputPath), [StringComparison]::OrdinalIgnoreCase)) {
    throw "SHA256SUMS cannot list itself ('$asset')."
  }
  $hashes[$asset] = Get-Sha256Hex -Path $path
}

Write-Utf8File -Path $OutputPath -Content (ConvertTo-Sha256SumsText -Hashes $hashes)
Write-Host "Wrote $($hashes.Count) checksum(s) to $([IO.Path]::GetFileName($OutputPath))."
