#Requires -Version 7.4
<#
.SYNOPSIS
Writes the release tuple (cheatengine-release-tuple/v0) of one CheatEngine.SDK package.

.DESCRIPTION
The tuple ties the exact attested package to its source, build, native bridge, Cheat Engine profile, qualification
evidence, SBOM, attestations and release assets (audit ch.21 "Le tuple a qualifier"; schema
eng/release/release-tuple.v0.schema.json). It is generated from the nupkg itself, the build-info.json of the CI run
that packed it, the SHA256SUMS of the release assets and the committed qualification documents, never from workflow
inputs. It fails when build-info describes another package or another bridge, and it reports, without failing, a
packed bridge whose bytes differ from the committed, audited one.

PrePublish (attest job, draft): the nuget.org identities are null. Published (finalize-release job): they are
required, with -RepositorySignatureVerified, both bundle names and -Tag.

.EXAMPLE
./eng/release/New-ReleaseTuple.ps1 -Stage PrePublish -PackagePath $pwd/artifacts/release/CheatEngine.SDK.2.0.0.nupkg `
  -BuildInfoPath $pwd/artifacts/build-info/build-info.json -AssetsDirectory $pwd/artifacts/release `
  -OutputPath $pwd/artifacts/release/CheatEngine.SDK.2.0.0.tuple.json -Tag v2.0.0
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)] [ValidateSet('PrePublish', 'Published')] [string] $Stage,
  [Parameter(Mandatory)] [string] $PackagePath,
  [Parameter(Mandatory)] [string] $BuildInfoPath,
  [Parameter(Mandatory)] [string] $AssetsDirectory,
  [Parameter(Mandatory)] [string] $OutputPath,
  [string] $Tag = '',
  [string] $ReleaseRunUrl = '',
  [string] $PullRequestNumber = '',
  [string] $PullRequestHeadSha = '',
  [string] $ProvenanceBundle = '',
  [string] $SbomBundle = '',
  [string] $NuGetOrgSignedSha256 = '',
  [string] $NuGetOrgSignedSha512 = '',
  [switch] $RepositorySignatureVerified,
  [string] $RepositoryRoot = (Join-Path $PSScriptRoot '..' '..'),
  [string] $CreatedUtc = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ReleaseTools.psm1') -Force

# One line per failure: readable in any console width and an annotation on the GitHub run.
trap {
  Write-Host "::error::$($_.Exception.Message)"
  exit 1
}

$PackageId = 'CheatEngine.SDK'
$BridgeEntry = 'build/native/cheatengine-sdk-lua-bridge.dll'
$SbomEntry = '_manifest/spdx_2.2/manifest.spdx.json'
$QualifiableFallbackProfile = 'ce-7.7.0.10621-x64-managed-hostfxr'

foreach ($path in @($PackagePath, $BuildInfoPath, $AssetsDirectory, $OutputPath)) {
  if (-not [IO.Path]::IsPathFullyQualified($path)) { throw "'$path' must be an absolute path." }
}
if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) { throw "The package '$PackagePath' does not exist." }
if (-not (Test-Path -LiteralPath $BuildInfoPath -PathType Leaf)) { throw "build-info '$BuildInfoPath' does not exist." }
if (-not (Test-Path -LiteralPath $AssetsDirectory -PathType Container)) { throw "The assets directory '$AssetsDirectory' does not exist." }
$RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)

# --- The package itself -------------------------------------------------------------------------------------------
$packageFile = [IO.Path]::GetFileName($PackagePath)
$packageSha256 = Get-Sha256Hex -Path $PackagePath
$packageSha512 = Get-Sha512Base64 -Path $PackagePath
$identity = Get-NuspecIdentity -PackagePath $PackagePath
if ($identity.Id -cne $PackageId) { throw "'$packageFile' declares package id '$($identity.Id)', not $PackageId." }
$version = $identity.Version
if ($packageFile -cne "$PackageId.$version.nupkg") { throw "'$packageFile' is not named $PackageId.$version.nupkg." }
if ($Tag -and $Tag -cne "v$version") { throw "Tag '$Tag' does not name version $version." }

$bridgeBytes = Read-ZipEntry -Path $PackagePath -EntryName $BridgeEntry
$bridgeSha256 = Get-Sha256Hex -Bytes $bridgeBytes
$bridgeFingerprint = Get-BridgeSourceFingerprint -Bytes $bridgeBytes

$sbomBytes = Read-ZipEntry -Path $PackagePath -EntryName $SbomEntry
$sbomSha256 = Get-Sha256Hex -Bytes $sbomBytes
$spdxVersion = [string](Get-JsonValue -InputObject ([Text.Encoding]::UTF8.GetString($sbomBytes) | ConvertFrom-Json) -Name 'spdxVersion')
if ($spdxVersion -cne 'SPDX-2.2') { throw "The embedded SBOM declares '$spdxVersion', expected SPDX-2.2." }

# --- build-info.json of the CI run that packed it -----------------------------------------------------------------
$buildInfo = Get-Content -LiteralPath $BuildInfoPath -Raw | ConvertFrom-Json
$source = 'build-info.json'
$buildSchema = [string](Get-JsonValue -InputObject $buildInfo -Name 'schema' -Required -Source $source)
if ($buildSchema -cne 'cheatengine-build-info/v0') { throw "build-info.json has schema '$buildSchema', expected cheatengine-build-info/v0." }
$described = @(@(Get-JsonValue -InputObject $buildInfo -Name 'packages' -Required -Source $source) |
    Where-Object { [string](Get-JsonValue -InputObject $_ -Name 'id') -ceq $PackageId })
if ($described.Count -ne 1) { throw "build-info.json must describe exactly one $PackageId package, found $($described.Count)." }
$builtSha256 = [string](Get-JsonValue -InputObject $described[0] -Name 'sha256' -Required -Source $source)
$builtFile = [string](Get-JsonValue -InputObject $described[0] -Name 'file' -Required -Source $source)
if ($builtSha256 -cne $packageSha256 -or $builtFile -cne $packageFile) {
  throw "build-info names another package: $builtFile ($builtSha256), but '$packageFile' has SHA-256 $packageSha256."
}
$builtSha512 = [string](Get-JsonValue -InputObject $described[0] -Name 'sha512')
if ($builtSha512 -and $builtSha512 -cne $packageSha512) { throw "build-info records SHA-512 $builtSha512 for '$packageFile', which has $packageSha512." }

$commit = [string](Get-JsonValue -InputObject $buildInfo -Name 'commit' -Required -Source $source)
if ($identity.RepositoryCommit -and $identity.RepositoryCommit -cne $commit) {
  throw "The nuspec names commit $($identity.RepositoryCommit), but build-info names $commit."
}

$builtBridge = Get-JsonValue -InputObject $buildInfo -Name 'nativeBridge' -Required -Source $source
$builtFingerprint = [string](Get-JsonValue -InputObject $builtBridge -Name 'sourceFingerprint' -Required -Source $source)
$builtBridgeSha256 = [string](Get-JsonValue -InputObject $builtBridge -Name 'sha256' -Required -Source $source)
if ($builtFingerprint -cne $bridgeFingerprint) {
  throw "The packed bridge embeds fingerprint $bridgeFingerprint, but build-info records $builtFingerprint."
}
if ($builtBridgeSha256 -cne $bridgeSha256) {
  throw "The packed bridge has SHA-256 $bridgeSha256, but build-info records $builtBridgeSha256."
}

# Drift is a report, never a failure: CI rebuilds the bridge with its pinned toolset (native/cheatengine-sdk-lua-bridge).
$auditManifest = Join-Path $RepositoryRoot 'native/cheatengine-sdk-lua-bridge/bridge-audit-manifest.json'
if (Test-Path -LiteralPath $auditManifest -PathType Leaf) {
  $audited = Get-Content -LiteralPath $auditManifest -Raw | ConvertFrom-Json
  $auditedSha256 = [string](Get-JsonValue -InputObject $audited -Name 'nativeAsset.sha256')
  $auditedFingerprint = [string](Get-JsonValue -InputObject $audited -Name 'source.fingerprint')
  if ($auditedSha256 -and $auditedSha256 -cne $bridgeSha256) {
    $same = if ($auditedFingerprint -ceq $bridgeFingerprint) { 'source fingerprint identical' } else { "source fingerprint differs ($auditedFingerprint)" }
    $drift = "packed bridge $bridgeSha256 differs from the committed, audited bridge $auditedSha256; $same"
    Write-Host "::notice title=Native bridge drift::$drift"
    if ($env:GITHUB_STEP_SUMMARY) { "- Native bridge drift: $drift." | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8 }
  }
}

# --- Build facts ------------------------------------------------------------------------------------------------
function Get-PropsValue([string] $RelativePath, [string] $Property) {
  $path = Join-Path $RepositoryRoot $RelativePath
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
  $xml = [xml](Get-Content -LiteralPath $path -Raw)
  $node = $xml.SelectSingleNode("//*[local-name()='$Property']")
  if ($null -eq $node) { return $null }
  $value = $node.InnerText.Trim()
  $indirection = [regex]::Match($value, '^\$\((?<name>[A-Za-z_][A-Za-z0-9_]*)\)$')
  if ($indirection.Success) {
    $inner = $xml.SelectSingleNode("//*[local-name()='$($indirection.Groups['name'].Value)']")
    $value = if ($null -eq $inner) { $null } else { $inner.InnerText.Trim() }
  }
  return $value
}

$roslynFloor = [string](Get-JsonValue -InputObject $buildInfo -Name 'roslynFloor')
if (-not $roslynFloor) { $roslynFloor = Get-PropsValue 'eng/RoslynComponent.props' 'RoslynComponentFloor' }
$analysisLevel = [string](Get-JsonValue -InputObject $buildInfo -Name 'analysisLevel')
if (-not $analysisLevel) { $analysisLevel = Get-PropsValue 'Directory.Build.props' 'AnalysisLevel' }
foreach ($fact in @(@('roslynFloor', $roslynFloor), @('analysisLevel', $analysisLevel))) {
  if (-not $fact[1]) { Write-Host "::warning::The release tuple records build.$($fact[0]) as null: neither build-info nor the repository names it." }
}
$runner = Get-JsonValue -InputObject $buildInfo -Name 'runner' -Required -Source $source
$toolchain = Get-JsonValue -InputObject $buildInfo -Name 'toolchain' -Required -Source $source

# --- Cheat Engine profile and qualification evidence (committed files only, PR-SEQ-06 tolerates their absence) ----
$qualificationRoot = Join-Path $RepositoryRoot 'docs/qualification'
$supportProfile = Join-Path $qualificationRoot 'support-profile.json'
if (Test-Path -LiteralPath $supportProfile -PathType Leaf) {
  $profiles = @(@(Get-JsonValue -InputObject (Get-Content -LiteralPath $supportProfile -Raw | ConvertFrom-Json) -Name 'profiles') |
      Where-Object { $null -ne $_ -and [string](Get-JsonValue -InputObject $_ -Name 'kind') -ceq 'Qualifiable' })
  if ($profiles.Count -ne 1) { throw "docs/qualification/support-profile.json must declare exactly one Qualifiable profile, found $($profiles.Count)." }
  $profileId = [string](Get-JsonValue -InputObject $profiles[0] -Name 'id' -Required -Source 'support-profile.json')
  $supportProfileSha256 = Get-NormalizedTextSha256 -Path $supportProfile
}
else {
  $profileId = $QualifiableFallbackProfile
  $supportProfileSha256 = $null
  Write-Host "::warning::docs/qualification/support-profile.json is absent: the tuple names $profileId without its hash."
}

$matrix = Join-Path $qualificationRoot 'matrix.json'
$matrixSha256 = if (Test-Path -LiteralPath $matrix -PathType Leaf) { Get-NormalizedTextSha256 -Path $matrix } else { $null }
$receipts = [Collections.Generic.List[object]]::new()
$receiptRoot = Join-Path $qualificationRoot 'receipts'
if (Test-Path -LiteralPath $receiptRoot -PathType Container) {
  $files = @(Get-ChildItem -LiteralPath $receiptRoot -Recurse -File -Filter 'R-*.json' |
      Where-Object { -not $_.Name.EndsWith('.events.json', [StringComparison]::Ordinal) })
  foreach ($file in $files) {
    $receipt = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    $origin = "receipt $($file.Name)"
    $receiptId = [string](Get-JsonValue -InputObject $receipt -Name 'receiptId' -Required -Source $origin)
    if ("$receiptId.json" -cne $file.Name) { throw "The receipt file $($file.Name) declares receiptId $receiptId." }
    $receipts.Add([ordered]@{
        receiptId       = $receiptId
        qualificationId = [string](Get-JsonValue -InputObject $receipt -Name 'qualificationId' -Required -Source $origin)
        level           = [string](Get-JsonValue -InputObject $receipt -Name 'level' -Required -Source $origin)
        status          = [string](Get-JsonValue -InputObject $receipt -Name 'status' -Required -Source $origin)
        sha256          = Get-NormalizedTextSha256 -Path $file.FullName
      })
  }
}
$receiptIds = [string[]]@($receipts | ForEach-Object { $_.receiptId })
$receiptArray = $receipts.ToArray()
[Array]::Sort($receiptIds, $receiptArray, [StringComparer]::Ordinal)
$sortedReceipts = @($receiptArray)

# --- Release assets -------------------------------------------------------------------------------------------------
$sumsPath = Join-Path $AssetsDirectory 'SHA256SUMS'
if (-not (Test-Path -LiteralPath $sumsPath -PathType Leaf)) { throw "'$AssetsDirectory' has no SHA256SUMS." }
$sums = ConvertFrom-Sha256SumsText -Text ([IO.File]::ReadAllText($sumsPath))
foreach ($name in $sums.Keys) {
  $assetPath = Join-Path $AssetsDirectory $name
  if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) { throw "SHA256SUMS lists '$name', which is not in the assets directory." }
  $actual = Get-Sha256Hex -Path $assetPath
  if ($actual -cne $sums[$name]) { throw "SHA256SUMS records $($sums[$name]) for '$name', which has $actual." }
}
if (-not $sums.Contains($packageFile) -or $sums[$packageFile] -cne $packageSha256) {
  throw "SHA256SUMS does not list '$packageFile' with SHA-256 $packageSha256."
}
$sbomAsset = "$PackageId.$version.spdx.json"
if (-not $sums.Contains($sbomAsset) -or $sums[$sbomAsset] -cne $sbomSha256) {
  throw "SHA256SUMS does not list '$sbomAsset' with the SHA-256 of the embedded SBOM ($sbomSha256)."
}
if ([bool]$ProvenanceBundle -ne [bool]$SbomBundle) { throw 'Pass both -ProvenanceBundle and -SbomBundle, or neither.' }
foreach ($bundle in @($ProvenanceBundle, $SbomBundle)) {
  if ($bundle -and -not $sums.Contains($bundle)) { throw "SHA256SUMS does not list the attestation bundle '$bundle'." }
}
$assets = @(foreach ($name in $sums.Keys) { [ordered]@{ name = $name; sha256 = $sums[$name] } })

# --- Stage rules ---------------------------------------------------------------------------------------------------
if ($PullRequestNumber -and $PullRequestNumber -notmatch '^[1-9][0-9]*$') { throw "-PullRequestNumber '$PullRequestNumber' is not a pull request number." }
if ($PullRequestHeadSha -and $PullRequestHeadSha -cnotmatch '^[0-9a-f]{40}$') { throw "-PullRequestHeadSha '$PullRequestHeadSha' is not a 40-hex commit." }
if ([bool]$PullRequestNumber -ne [bool]$PullRequestHeadSha) { throw 'Pass both -PullRequestNumber and -PullRequestHeadSha, or neither.' }
if ($Stage -eq 'Published') {
  $missing = @()
  if ($NuGetOrgSignedSha256 -cnotmatch '^[0-9a-f]{64}$') { $missing += '-NuGetOrgSignedSha256 (64 lowercase hex)' }
  if ($NuGetOrgSignedSha512 -cnotmatch '^[A-Za-z0-9+/]{86}==$') { $missing += '-NuGetOrgSignedSha512 (base64 SHA-512)' }
  if (-not $RepositorySignatureVerified) { $missing += '-RepositorySignatureVerified' }
  if (-not $ProvenanceBundle) { $missing += '-ProvenanceBundle and -SbomBundle' }
  if (-not $Tag) { $missing += '-Tag' }
  if ($missing.Count -gt 0) { throw "A Published tuple requires $($missing -join ', ')." }
}
elseif ($NuGetOrgSignedSha256 -or $NuGetOrgSignedSha512 -or $RepositorySignatureVerified) {
  throw 'A PrePublish tuple carries no nuget.org identity.'
}

if (-not $ReleaseRunUrl) { $ReleaseRunUrl = [string](Get-JsonValue -InputObject $buildInfo -Name 'runUrl' -Required -Source $source) }
if (-not $CreatedUtc) { $CreatedUtc = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ', [Globalization.CultureInfo]::InvariantCulture) }

$published = $Stage -eq 'Published'
$tuple = [ordered]@{
  schema        = 'cheatengine-release-tuple/v0'
  stage         = $Stage
  package       = [ordered]@{
    id                          = $PackageId
    version                     = $version
    attestedAssetSha256         = $packageSha256
    contentHashSha512           = $packageSha512
    nugetOrgSignedSha256        = if ($published) { $NuGetOrgSignedSha256 } else { $null }
    nugetOrgSignedSha512        = if ($published) { $NuGetOrgSignedSha512 } else { $null }
    repositorySignatureVerified = if ($published) { $true } else { $null }
  }
  source        = [ordered]@{
    repository    = [string](Get-JsonValue -InputObject $buildInfo -Name 'repository' -Required -Source $source)
    tag           = if ($Tag) { $Tag } else { $null }
    commit        = $commit
    treeHash      = [string](Get-JsonValue -InputObject $buildInfo -Name 'treeHash' -Required -Source $source)
    pullRequest   = if ($PullRequestNumber) { [ordered]@{ number = [int]$PullRequestNumber; headSha = $PullRequestHeadSha } } else { $null }
    releaseRunUrl = $ReleaseRunUrl
    ciRunUrl      = [string](Get-JsonValue -InputObject $buildInfo -Name 'runUrl' -Required -Source $source)
  }
  build         = [ordered]@{
    dotnetSdk     = [string](Get-JsonValue -InputObject $buildInfo -Name 'dotnetSdk' -Required -Source $source)
    runner        = [ordered]@{
      label        = [string](Get-JsonValue -InputObject $runner -Name 'label' -Required -Source $source)
      imageOs      = [string](Get-JsonValue -InputObject $runner -Name 'imageOs' -Required -Source $source)
      imageVersion = [string](Get-JsonValue -InputObject $runner -Name 'imageVersion' -Required -Source $source)
    }
    toolchain     = [ordered]@{
      xmake        = [string](Get-JsonValue -InputObject $toolchain -Name 'xmake' -Required -Source $source)
      msvcToolset  = [string](Get-JsonValue -InputObject $toolchain -Name 'msvcToolset' -Required -Source $source)
      msvcVersion  = [string](Get-JsonValue -InputObject $toolchain -Name 'msvcVersion' -Required -Source $source)
      windowsSdk   = [string](Get-JsonValue -InputObject $toolchain -Name 'windowsSdk' -Required -Source $source)
    }
    roslynFloor   = if ($roslynFloor) { $roslynFloor } else { $null }
    analysisLevel = if ($analysisLevel) { $analysisLevel } else { $null }
  }
  nativeBridge  = [ordered]@{
    packagePath       = $BridgeEntry
    sha256            = $bridgeSha256
    sourceFingerprint = $bridgeFingerprint
  }
  ceProfile     = [ordered]@{
    profileId            = $profileId
    supportProfileSha256 = $supportProfileSha256
  }
  qualification = [ordered]@{
    matrixSha256 = $matrixSha256
    receipts     = $sortedReceipts
  }
  sbom          = [ordered]@{
    entry       = $SbomEntry
    sha256      = $sbomSha256
    spdxVersion = $spdxVersion
    attested    = [bool]$SbomBundle
  }
  attestations  = [ordered]@{
    provenanceBundle = if ($ProvenanceBundle) { $ProvenanceBundle } else { $null }
    sbomBundle       = if ($SbomBundle) { $SbomBundle } else { $null }
  }
  assets        = $assets
  createdUtc    = $CreatedUtc
}

$json = ConvertTo-ReleaseJson -InputObject $tuple
if (Test-AbsoluteLocalPath -Text $json) { throw 'The release tuple would contain an absolute local path.' }
Write-Utf8File -Path $OutputPath -Content $json

Write-Host "Wrote the $Stage release tuple of $packageFile ($packageSha256) to $([IO.Path]::GetFileName($OutputPath))."
if ($env:GITHUB_STEP_SUMMARY) {
  $signedCell = if ($published) { '`' + $NuGetOrgSignedSha256 + '`' } else { 'not published yet' }
  @(
    "### Release tuple ($Stage)",
    '',
    '| Identity | Value |',
    '| --- | --- |',
    "| Package | ``$packageFile`` |",
    "| Attested asset SHA-256 | ``$packageSha256`` |",
    "| NuGet content hash (SHA-512) | ``$packageSha512`` |",
    "| nuget.org signed SHA-256 | $signedCell |",
    "| Packed bridge SHA-256 | ``$bridgeSha256`` |",
    "| Qualification receipts | $($sortedReceipts.Count) |",
    ''
  ) | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}
