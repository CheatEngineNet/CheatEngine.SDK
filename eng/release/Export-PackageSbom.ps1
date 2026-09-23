#Requires -Version 7.4
<#
.SYNOPSIS
Extracts the SPDX 2.2 SBOM embedded in a CheatEngine.SDK package, byte for byte.

.DESCRIPTION
Microsoft.Sbom.Targets embeds the SBOM at _manifest/spdx_2.2/manifest.spdx.json when the package is packed. The
release attests that exact document (actions/attest with sbom-path derives the predicate
https://spdx.dev/Document/v2.2 from its spdxVersion) and attaches it to the release, so the bytes must not be
re-serialized. Fails when the entry is absent, is not SPDX-2.2, or disagrees with the .sha256 checksum the SBOM tool
writes next to it.

.EXAMPLE
./eng/release/Export-PackageSbom.ps1 -PackagePath $pwd/artifacts/nuget/CheatEngine.SDK.2.0.0.nupkg `
  -OutputPath $pwd/artifacts/release/CheatEngine.SDK.2.0.0.spdx.json
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)] [string] $PackagePath,
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

$SbomEntry = '_manifest/spdx_2.2/manifest.spdx.json'

foreach ($path in @($PackagePath, $OutputPath)) {
  if (-not [IO.Path]::IsPathFullyQualified($path)) { throw "'$path' must be an absolute path." }
}
if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) { throw "The package '$PackagePath' does not exist." }
$packageFile = [IO.Path]::GetFileName($PackagePath)

$entries = @(Get-ZipEntryName -Path $PackagePath)
if ($entries -cnotcontains $SbomEntry) { throw "'$packageFile' embeds no SPDX 2.2 SBOM at $SbomEntry." }
$sbom = Read-ZipEntry -Path $PackagePath -EntryName $SbomEntry

try {
  $document = [Text.Encoding]::UTF8.GetString($sbom) | ConvertFrom-Json
}
catch {
  throw "The SBOM of '$packageFile' is not JSON: $($_.Exception.Message)"
}
$spdxVersion = [string](Get-JsonValue -InputObject $document -Name 'spdxVersion')
if ($spdxVersion -cne 'SPDX-2.2') { throw "The SBOM of '$packageFile' declares spdxVersion '$spdxVersion', expected SPDX-2.2." }
if (-not [string](Get-JsonValue -InputObject $document -Name 'SPDXID')) { throw "The SBOM of '$packageFile' has no SPDXID." }

$sha256 = Get-Sha256Hex -Bytes $sbom
$sidecar = "$SbomEntry.sha256"
if ($entries -ccontains $sidecar) {
  $declared = [Text.Encoding]::ASCII.GetString((Read-ZipEntry -Path $PackagePath -EntryName $sidecar)).Trim().ToLowerInvariant()
  if ($declared -cne $sha256) { throw "$sidecar declares $declared, but the SBOM has SHA-256 $sha256." }
}

$directory = [IO.Path]::GetDirectoryName($OutputPath)
[void][IO.Directory]::CreateDirectory($directory)
[IO.File]::WriteAllBytes($OutputPath, $sbom)
Write-Host "Exported the $spdxVersion SBOM of $packageFile ($sha256) to $([IO.Path]::GetFileName($OutputPath))."
