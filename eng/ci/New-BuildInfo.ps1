#Requires -Version 7.0
<#
.SYNOPSIS
    Writes build-info.json (cheatengine-build-info/v0) for the package the Release leg packed.

.DESCRIPTION
    build-info.json records where a CheatEngine.SDK package came from: repository, commit and tree, the workflow run,
    the pull request head, the exact .NET SDK and global.json, the runner image, the native bridge toolchain and
    hashes (from the native job's outputs), and the package identity with its SHA-256, SHA-512 and SBOM entry. It is
    the precursor of the release tuple manifest (shared contract section 1.10) and is validated against
    eng/ci/build-info.v0.schema.json by the repository tests.

    Every value comes from the environment GitHub Actions provides, or from the named environment variables below,
    which ci.yml sets from job outputs; the script refuses to write a document with a missing or malformed value.
      BUILD_INFO_RUNNER_LABEL                   the runs-on label of the job (a literal in ci.yml)
      BUILD_INFO_PULL_REQUEST_NUMBER            github.event.pull_request.number (empty outside pull requests)
      BUILD_INFO_PULL_REQUEST_HEAD_SHA          github.event.pull_request.head.sha (empty outside pull requests)
      BUILD_INFO_XMAKE_VERSION                  needs.native.outputs.xmake-version
      BUILD_INFO_MSVC_TOOLSET                   needs.native.outputs.msvc-toolset
      BUILD_INFO_MSVC_VERSION                   needs.native.outputs.msvc-version
      BUILD_INFO_WINDOWS_SDK_VERSION            needs.native.outputs.windows-sdk-version
      BUILD_INFO_BRIDGE_SHA256                  needs.native.outputs.bridge-sha256
      BUILD_INFO_BRIDGE_FINGERPRINT             needs.native.outputs.bridge-fingerprint
      BUILD_INFO_CHECKED_IN_BRIDGE_SHA256       needs.native.outputs.checked-in-bridge-sha256

    Encoding (shared contract section 2.0): UTF-8 without BOM, lowercase hexadecimal hashes, standard base64 SHA-512,
    ISO 8601 UTC times, repository-relative paths only. globalJsonSha256 hashes global.json with CRLF normalized to LF,
    that is the committed blob, so the value does not depend on the checkout's line endings.

.PARAMETER PackagePath
    The nupkg the Release leg packed (Test-SdkPackage.ps1 output `nupkg`).

.PARAMETER OutputPath
    Where to write the document.

.EXAMPLE
    ./eng/ci/New-BuildInfo.ps1 -PackagePath artifacts/nuget/CheatEngine.SDK.2.0.0-alpha.0.1.nupkg
#>
[CmdletBinding()]
param(
	[Parameter(Mandatory)]
	[string] $PackagePath,

	[string] $OutputPath = 'artifacts/build-info/build-info.json'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$InformationPreference = 'Continue'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$schemaId = 'cheatengine-build-info/v0'
$sbomEntry = '_manifest/spdx_2.2/manifest.spdx.json'
$bridgeEntry = 'build/native/cheatengine-sdk-lua-bridge.dll'
$sha256Pattern = '^[0-9a-f]{64}$'
$objectIdPattern = '^[0-9a-f]{40}$'

function Get-RequiredValue {
	param(
		[Parameter(Mandatory)] [string] $Name,
		[string] $Pattern = '.'
	)

	$value = [Environment]::GetEnvironmentVariable($Name)
	if ([string]::IsNullOrWhiteSpace($value)) {
		throw "Environment variable $Name is empty; build-info.json cannot be written without it."
	}
	$value = $value.Trim()
	if ($value -cnotmatch $Pattern) {
		throw "Environment variable $Name has the malformed value '$value' (expected $Pattern)."
	}
	return $value
}

function Invoke-Git {
	param([Parameter(Mandatory)] [string[]] $Arguments)

	$output = & git -C $repositoryRoot @Arguments
	if ($LASTEXITCODE -ne 0) {
		throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
	}
	return "$output".Trim()
}

$package = Get-Item -LiteralPath ([IO.Path]::GetFullPath($PackagePath, $PWD.Path))
$packageBytes = [IO.File]::ReadAllBytes($package.FullName)

# Package identity from the nuspec, and the bridge the package actually carries.
$archive = [IO.Compression.ZipArchive]::new([IO.MemoryStream]::new($packageBytes), [IO.Compression.ZipArchiveMode]::Read)
try {
	$nuspecEntry = @($archive.Entries | Where-Object { $_.FullName -notmatch '/' -and $_.Name -like '*.nuspec' })
	if ($nuspecEntry.Count -ne 1) {
		throw "$($package.Name) must hold exactly one nuspec at its root."
	}
	$reader = [IO.StreamReader]::new($nuspecEntry[0].Open())
	try {
		[xml] $nuspec = $reader.ReadToEnd()
	}
	finally {
		$reader.Dispose()
	}
	if ($null -eq $archive.GetEntry($sbomEntry)) {
		throw "$($package.Name) does not embed $sbomEntry."
	}
	$bridge = $archive.GetEntry($bridgeEntry)
	if ($null -eq $bridge) {
		throw "$($package.Name) does not carry $bridgeEntry."
	}
	$bridgeStream = $bridge.Open()
	try {
		$packedBridgeSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bridgeStream)).ToLowerInvariant()
	}
	finally {
		$bridgeStream.Dispose()
	}
}
finally {
	$archive.Dispose()
}
$packageId = [string] $nuspec.package.metadata.id
$packageVersion = [string] $nuspec.package.metadata.version
if ($package.Name -cne "$packageId.$packageVersion.nupkg") {
	throw "$($package.Name) does not match its nuspec identity $packageId $packageVersion."
}

# Run identity.
$repository = Get-RequiredValue -Name GITHUB_REPOSITORY -Pattern '^[A-Za-z0-9-]+/[A-Za-z0-9._-]+$'
$commit = Get-RequiredValue -Name GITHUB_SHA -Pattern $objectIdPattern
$head = Invoke-Git -Arguments @('rev-parse', 'HEAD')
if ($head -cne $commit) {
	throw "The checkout HEAD is $head but GITHUB_SHA is ${commit}: build-info must describe the commit that was built."
}
$treeHash = Invoke-Git -Arguments @('rev-parse', 'HEAD^{tree}')
$eventName = Get-RequiredValue -Name GITHUB_EVENT_NAME -Pattern '^(pull_request|push|workflow_dispatch)$'
$runId = [long] (Get-RequiredValue -Name GITHUB_RUN_ID -Pattern '^[1-9][0-9]*$')
$runAttempt = [int] (Get-RequiredValue -Name GITHUB_RUN_ATTEMPT -Pattern '^[1-9][0-9]*$')
$serverUrl = Get-RequiredValue -Name GITHUB_SERVER_URL -Pattern '^https://'

$pullRequest = $null
if ($eventName -eq 'pull_request') {
	$pullRequest = [ordered]@{
		number = [int] (Get-RequiredValue -Name BUILD_INFO_PULL_REQUEST_NUMBER -Pattern '^[1-9][0-9]*$')
		headSha = Get-RequiredValue -Name BUILD_INFO_PULL_REQUEST_HEAD_SHA -Pattern $objectIdPattern
	}
}

# Toolchain: the SDK that ran must be the one global.json pins.
$globalJsonPath = Join-Path $repositoryRoot 'global.json'
$globalJsonText = [IO.File]::ReadAllText($globalJsonPath).Replace("`r`n", "`n")
$globalJsonSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($globalJsonText))).ToLowerInvariant()
$pinnedSdk = ($globalJsonText | ConvertFrom-Json).sdk.version
Push-Location -LiteralPath $repositoryRoot
try {
	$dotnetSdk = "$(& dotnet --version)".Trim()
	if ($LASTEXITCODE -ne 0) {
		throw "dotnet --version failed with exit code $LASTEXITCODE."
	}
}
finally {
	Pop-Location
}
if ($dotnetSdk -cne $pinnedSdk) {
	throw "The build ran .NET SDK $dotnetSdk but global.json pins $pinnedSdk."
}

$bridgeSha256 = Get-RequiredValue -Name BUILD_INFO_BRIDGE_SHA256 -Pattern $sha256Pattern
$checkedInSha256 = Get-RequiredValue -Name BUILD_INFO_CHECKED_IN_BRIDGE_SHA256 -Pattern $sha256Pattern
if ($packedBridgeSha256 -cne $bridgeSha256) {
	throw "$($package.Name) packs a bridge with SHA-256 $packedBridgeSha256, but the native job built $bridgeSha256."
}

$document = [ordered]@{
	schema = $schemaId
	repository = $repository
	commit = $commit
	treeHash = $treeHash
	ref = Get-RequiredValue -Name GITHUB_REF -Pattern '^refs/'
	event = $eventName
	runId = $runId
	runAttempt = $runAttempt
	runUrl = "$serverUrl/$repository/actions/runs/$runId"
	pullRequest = $pullRequest
	dotnetSdk = $dotnetSdk
	globalJsonSha256 = $globalJsonSha256
	runner = [ordered]@{
		label = Get-RequiredValue -Name BUILD_INFO_RUNNER_LABEL -Pattern '^(windows-2025|ubuntu-24\.04)$'
		imageOs = Get-RequiredValue -Name ImageOS
		imageVersion = Get-RequiredValue -Name ImageVersion
	}
	toolchain = [ordered]@{
		xmake = Get-RequiredValue -Name BUILD_INFO_XMAKE_VERSION -Pattern '^\d+\.\d+\.\d+$'
		msvcToolset = Get-RequiredValue -Name BUILD_INFO_MSVC_TOOLSET -Pattern '^\d+\.\d+\.\d+$'
		msvcVersion = Get-RequiredValue -Name BUILD_INFO_MSVC_VERSION -Pattern '^\d+\.\d+\.\d+(\.\d+)?$'
		windowsSdk = Get-RequiredValue -Name BUILD_INFO_WINDOWS_SDK_VERSION -Pattern '^\d+\.\d+\.\d+\.\d+$'
	}
	nativeBridge = [ordered]@{
		sha256 = $bridgeSha256
		checkedInSha256 = $checkedInSha256
		sourceFingerprint = Get-RequiredValue -Name BUILD_INFO_BRIDGE_FINGERPRINT -Pattern '^[0-9a-f]{64}:[0-9a-f]{64}$'
		driftFromCheckedIn = $bridgeSha256 -ne $checkedInSha256
	}
	packages = @(
		[ordered]@{
			id = $packageId
			version = $packageVersion
			file = $package.Name
			sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($packageBytes)).ToLowerInvariant()
			sha512 = [Convert]::ToBase64String([Security.Cryptography.SHA512]::HashData($packageBytes))
			sbomEntry = $sbomEntry
		}
	)
	createdUtc = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ', [Globalization.CultureInfo]::InvariantCulture)
}

$json = ConvertTo-Json -InputObject $document -Depth 6
if ($json -match '[A-Za-z]:\\\\|file://|\\\\Users\\\\') {
	throw 'build-info.json would contain an absolute local path.'
}

$output = [IO.Path]::GetFullPath($OutputPath, $PWD.Path)
New-Item -ItemType Directory -Path (Split-Path -Path $output -Parent) -Force | Out-Null
[IO.File]::WriteAllText($output, $json + "`n", [Text.UTF8Encoding]::new($false))

if ($env:GITHUB_STEP_SUMMARY) {
	@(
		'### Build info'
		''
		"``build-info.json`` ($schemaId): ``$packageId`` ``$packageVersion`` from ``$commit`` (tree ``$treeHash``), .NET SDK ``$dotnetSdk``, runner ``$($document.runner.label)`` image ``$($document.runner.imageVersion)``, bridge ``$bridgeSha256``."
	) | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}
Write-Information "Wrote $output."
