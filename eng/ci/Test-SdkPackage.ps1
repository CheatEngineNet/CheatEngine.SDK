#Requires -Version 7.0
<#
.SYNOPSIS
    Checks the CheatEngine.SDK package the Release leg just packed, before any test consumes it.

.DESCRIPTION
    The Release leg packs before it tests, and the packaging tests consume this exact file
    (CESDK_PACKAGED_UMBRELLA_NUPKG), so the file that is tested is the file that is uploaded and released. This script
    asserts, in order:
      - the package directory holds exactly one file, CheatEngine.SDK.<version>.nupkg, and nothing else;
      - with -PackageVersion, the file name is exactly CheatEngine.SDK.<PackageVersion>.nupkg;
      - the nuspec id is CheatEngine.SDK and its version matches the file name;
      - the SPDX SBOM is embedded at _manifest/spdx_2.2/manifest.spdx.json;
      - with -ExpectedBridgeSha256, build/native/cheatengine-sdk-lua-bridge.dll is the bridge the native job built.
    It then writes the file name and its SHA-256 to the step summary, and `nupkg=<absolute path>` and
    `sha256=<lowercase hex>` to GITHUB_OUTPUT when running in GitHub Actions.

.PARAMETER PackageDirectory
    The pack output directory (dotnet pack -o).

.PARAMETER PackageVersion
    When set (tag releases), the only accepted package version.

.PARAMETER ExpectedBridgeSha256
    When set, the SHA-256 the packed native bridge must have (the native job's bridge-sha256 output).

.EXAMPLE
    ./eng/ci/Test-SdkPackage.ps1 -PackageDirectory artifacts/nuget
#>
[CmdletBinding()]
param(
	[Parameter(Mandatory)]
	[string] $PackageDirectory,

	[AllowEmptyString()]
	[string] $PackageVersion = '',

	[AllowEmptyString()]
	[string] $ExpectedBridgeSha256 = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$InformationPreference = 'Continue'

$packageId = 'CheatEngine.SDK'
$sbomEntry = '_manifest/spdx_2.2/manifest.spdx.json'
$bridgeEntry = 'build/native/cheatengine-sdk-lua-bridge.dll'

$directory = [IO.Path]::GetFullPath($PackageDirectory, $PWD.Path)
if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
	throw "The package directory '$directory' does not exist."
}

$files = @(Get-ChildItem -LiteralPath $directory -Recurse -File)
if ($files.Count -ne 1 -or $files[0].Name -notmatch "^$([regex]::Escape($packageId))\.(?<version>\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)\.nupkg$") {
	throw "Expected exactly one $packageId.<version>.nupkg in '$directory', found: $(($files | ForEach-Object Name) -join ', ')."
}
$package = $files[0]
$fileVersion = $Matches['version']
if ($PackageVersion -and $package.Name -cne "$packageId.$PackageVersion.nupkg") {
	throw "Packed $($package.Name), but the release requires $packageId.$PackageVersion.nupkg."
}

$stream = [IO.File]::OpenRead($package.FullName)
try {
	$archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Read)
	try {
		$nuspecEntry = $archive.GetEntry("$packageId.nuspec")
		if ($null -eq $nuspecEntry) {
			throw "$($package.Name) has no $packageId.nuspec at its root."
		}
		$reader = [IO.StreamReader]::new($nuspecEntry.Open())
		try {
			[xml] $nuspec = $reader.ReadToEnd()
		}
		finally {
			$reader.Dispose()
		}
		$nuspecId = $nuspec.package.metadata.id
		$nuspecVersion = $nuspec.package.metadata.version
		if ($nuspecId -cne $packageId -or $nuspecVersion -cne $fileVersion) {
			throw "$($package.Name) declares id '$nuspecId' version '$nuspecVersion' in its nuspec."
		}

		if ($null -eq $archive.GetEntry($sbomEntry)) {
			throw "$($package.Name) does not embed the SPDX SBOM at $sbomEntry (Microsoft.Sbom.Targets, CESDK9008)."
		}

		$bridge = $archive.GetEntry($bridgeEntry)
		if ($null -eq $bridge) {
			throw "$($package.Name) does not carry the native bridge at $bridgeEntry."
		}
		if ($ExpectedBridgeSha256) {
			$bridgeStream = $bridge.Open()
			try {
				$bridgeSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bridgeStream)).ToLowerInvariant()
			}
			finally {
				$bridgeStream.Dispose()
			}
			if ($bridgeSha256 -cne $ExpectedBridgeSha256.ToLowerInvariant()) {
				throw "$($package.Name) packs a bridge with SHA-256 $bridgeSha256, but the native job built $ExpectedBridgeSha256."
			}
		}
	}
	finally {
		$archive.Dispose()
	}
}
finally {
	$stream.Dispose()
}

$sha256 = (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash.ToLowerInvariant()

if ($env:GITHUB_OUTPUT) {
	@("nupkg=$($package.FullName)", "sha256=$sha256", "version=$fileVersion") |
		Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}
$summary = @(
	'### Package'
	''
	'| File | Version | SHA-256 |'
	'| --- | --- | --- |'
	"| ``$($package.Name)`` | ``$fileVersion`` | ``$sha256`` |"
	''
	"The packaging tests of this leg consume this exact file, and the ``nuget-package`` artifact uploads it unchanged."
)
if ($env:GITHUB_STEP_SUMMARY) {
	$summary | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}
Write-Information "Checked $($package.Name) (SHA-256 $sha256): one package, nuspec identity, SBOM and native bridge present."
