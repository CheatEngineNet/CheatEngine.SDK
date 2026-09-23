#Requires -Version 7.4
<#
.SYNOPSIS
Verifies that nuget.org serves the attested package, repository-signed and otherwise unchanged.

.DESCRIPTION
Resolves the PackageBaseAddress/3.0.0 resource from the service index (never a hard-coded base URL), polls the flat
container until the version is listed (nuget.org validates and indexes a push for several minutes), downloads the
repository-signed file, runs 'dotnet nuget verify --all' on it (exit 0 required; on .NET 10 it prints the package
content hash, whose value must be the SHA-512 of the attested unsigned file), and compares the two archives: the
signed copy must hold exactly the attested entries plus .signature.p7s, every common entry byte-identical.

It never runs 'dotnet nuget verify' on the unsigned CI package, which has no signature to verify (NU3004).

Outputs signed-sha256, signed-sha512 and content-hash to GITHUB_OUTPUT when that file is set, and the same facts as
JSON to -OutputPath when given.

.EXAMPLE
./eng/release/Test-PublishedPackage.ps1 -AttestedPackagePath $pwd/CheatEngine.SDK.1.0.0.nupkg -PackageId CheatEngine.SDK `
  -Version 1.0.0 -TimeoutMinutes 2
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)] [string] $AttestedPackagePath,
  [Parameter(Mandatory)] [string] $PackageId,
  [Parameter(Mandatory)] [string] $Version,
  [string] $ServiceIndex = 'https://api.nuget.org/v3/index.json',
  [ValidateRange(1, 120)] [int] $TimeoutMinutes = 35,
  [ValidateRange(1, 300)] [int] $PollSeconds = 30,
  [string] $OutputPath = '',
  [string] $DownloadDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ReleaseTools.psm1') -Force

# One line per failure: readable in any console width and an annotation on the GitHub run.
trap {
  Write-Host "::error::$($_.Exception.Message)"
  exit 1
}

if (-not [IO.Path]::IsPathFullyQualified($AttestedPackagePath)) { throw "'$AttestedPackagePath' must be an absolute path." }
if (-not (Test-Path -LiteralPath $AttestedPackagePath -PathType Leaf)) { throw "The attested package '$AttestedPackagePath' does not exist." }
if ($OutputPath -and -not [IO.Path]::IsPathFullyQualified($OutputPath)) { throw "'$OutputPath' must be an absolute path." }

$attestedSha256 = Get-Sha256Hex -Path $AttestedPackagePath
$contentHash = Get-Sha512Base64 -Path $AttestedPackagePath
$lowerId = $PackageId.ToLowerInvariant()
$lowerVersion = $Version.Split('+')[0].ToLowerInvariant()
$deadline = [DateTime]::UtcNow.AddMinutes($TimeoutMinutes)

# PackageBaseAddress/3.0.0 is the flat container: https://learn.microsoft.com/nuget/api/package-base-address-resource
$index = Invoke-RestMethod -Uri $ServiceIndex -MaximumRetryCount 5 -RetryIntervalSec 10
$resource = @($index.resources | Where-Object { $_.'@type' -ceq 'PackageBaseAddress/3.0.0' }) | Select-Object -First 1
if ($null -eq $resource) { throw "$ServiceIndex has no PackageBaseAddress/3.0.0 resource." }
$base = ([string]$resource.'@id').TrimEnd('/') + '/'
Write-Host "Package base address: $base"

# A 404 (no version of a new id yet) and transient errors are retried until the deadline.
$listed = $false
while (-not $listed) {
  $state = 'not listed yet'
  try {
    $versions = @((Invoke-RestMethod -Uri "$base$lowerId/index.json").versions)
    $listed = $versions -ccontains $lowerVersion
  }
  catch {
    $state = "not readable yet ($($_.Exception.Message))"
  }
  if (-not $listed) {
    if ([DateTime]::UtcNow -ge $deadline) {
      throw "$PackageId $Version is $state at $base after $TimeoutMinutes minutes. nuget.org may still be validating it; re-run this job."
    }
    Write-Host "$PackageId $Version is $state; next poll in $PollSeconds s."
    Start-Sleep -Seconds $PollSeconds
  }
}

if (-not $DownloadDirectory) { $DownloadDirectory = (New-Item -ItemType Directory -Path (Join-Path ([IO.Path]::GetTempPath()) ([IO.Path]::GetRandomFileName()))).FullName }
[void][IO.Directory]::CreateDirectory($DownloadDirectory)
$signedPath = Join-Path $DownloadDirectory "$lowerId.$lowerVersion.nupkg"
$downloadUrl = "$base$lowerId/$lowerVersion/$lowerId.$lowerVersion.nupkg"
while ($true) {
  try {
    Invoke-WebRequest -Uri $downloadUrl -OutFile $signedPath
    break
  }
  catch {
    if ([DateTime]::UtcNow -ge $deadline) { throw "Downloading $downloadUrl failed until the deadline: $($_.Exception.Message)" }
    Write-Host "Download not available yet ($($_.Exception.Message)); next attempt in $PollSeconds s."
    Start-Sleep -Seconds $PollSeconds
  }
}

# English output for the log; the checks below do not depend on the UI language.
$previousLanguage = $env:DOTNET_CLI_UI_LANGUAGE
$env:DOTNET_CLI_UI_LANGUAGE = 'en'
try {
  $verifyOutput = (& dotnet nuget verify --all -v normal $signedPath 2>&1 | Out-String)
  $verifyExit = $LASTEXITCODE
}
finally {
  $env:DOTNET_CLI_UI_LANGUAGE = $previousLanguage
}
Write-Host $verifyOutput
if ($verifyExit -ne 0) { throw "'dotnet nuget verify --all' rejected the nuget.org package (exit code $verifyExit)." }
if (-not (Test-VerifyOutputContainsContentHash -Output $verifyOutput -ContentHash $contentHash)) {
  throw "'dotnet nuget verify' did not report the content hash $contentHash of the attested package."
}
if (-not $verifyOutput.Contains($ServiceIndex, [StringComparison]::OrdinalIgnoreCase)) {
  throw "'dotnet nuget verify' did not report a repository signature from $ServiceIndex."
}

$comparison = Compare-SignedPackageContent -AttestedPath $AttestedPackagePath -SignedPath $signedPath
if (-not $comparison.IsMatch) {
  throw ("The nuget.org package is not the attested package plus its signature: missing [{0}], changed [{1}], " +
    "unexpected [{2}], signature present: {3}." -f ($comparison.Missing -join ', '), ($comparison.Changed -join ', '),
    ($comparison.Unexpected -join ', '), $comparison.HasSignature)
}

$signedSha256 = Get-Sha256Hex -Path $signedPath
$signedSha512 = Get-Sha512Base64 -Path $signedPath
$result = [ordered]@{
  packageId                   = $PackageId
  version                     = $Version
  serviceIndex                = $ServiceIndex
  attestedAssetSha256         = $attestedSha256
  contentHashSha512           = $contentHash
  nugetOrgSignedSha256        = $signedSha256
  nugetOrgSignedSha512        = $signedSha512
  repositorySignatureVerified = $true
  entriesCompared             = $comparison.Compared
}
Write-Host "nuget.org serves $PackageId $Version repository-signed: SHA-256 $signedSha256; content hash $contentHash; $($comparison.Compared) entries identical to the attested package."

if ($OutputPath) { Write-Utf8File -Path $OutputPath -Content (ConvertTo-ReleaseJson -InputObject $result) }
if ($env:GITHUB_OUTPUT) {
  @("signed-sha256=$signedSha256", "signed-sha512=$signedSha512", "content-hash=$contentHash") |
    Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}
if ($env:GITHUB_STEP_SUMMARY) {
  @(
    "### nuget.org publication of $PackageId $Version",
    '',
    '| Identity | Value |',
    '| --- | --- |',
    "| Attested asset SHA-256 | ``$attestedSha256`` |",
    "| NuGet content hash (SHA-512) | ``$contentHash`` |",
    "| nuget.org repository-signed SHA-256 | ``$signedSha256`` |",
    "| Entries identical to the attested package | $($comparison.Compared) (plus ``.signature.p7s``) |",
    ''
  ) | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}
exit 0
