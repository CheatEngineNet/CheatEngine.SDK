#Requires -Version 7.0
<#
.SYNOPSIS
    Builds the native Lua protection bridge with a pinned MSVC toolset and Windows SDK, proves the build reproducible
    and path independent, and records the toolchain that produced it.

.DESCRIPTION
    The bridge (native/cheatengine-sdk-lua-bridge) is a mandatory asset of the CheatEngine.SDK package (ADR-03, audit
    ch.21). This script is the body of the CI `native` job and runs the same way on a developer machine that has xmake
    and the pinned MSVC components.

    Three builds, each configured with --vs_toolset/--vs_sdkver (the pins) and --ccache=n (every build compiles from
    source, so a cache hit cannot fake reproducibility):
      1. primary          artifacts/native/cheatengine-sdk-lua-bridge        the DLL later jobs consume
      2. reproducibility  artifacts/native/cheatengine-sdk-lua-bridge-repro  same tree, second output directory
      3. path check       the two build inputs copied under -PathCheckRoot and built from there, so a source or
                          output path embedded in the image would change its bytes
    All three DLLs must have the same SHA-256, and the primary DLL must export the source fingerprint
    `<sha256 of cheatengine_sdk_lua_bridge.c>:<sha256 of xmake.lua>`.

    xmake 3.0.9 mis-parses an absolute Windows -o path when the target uses $(builddir), so every xmake path is
    relative to the current directory: the path-check build runs from inside the copied tree.

    Facts recorded (step summary, GITHUB_OUTPUT, artifacts/native/native-toolchain.json): runner image, xmake version,
    the MSVC toolset and Windows SDK that xmake actually resolved (read from xmake's toolchain cache and checked
    against the pins), the compiler banner version, the linker version the PE header of the DLL records (checked
    against the resolved toolset), the CI-built and checked-in DLL SHA-256 and the fingerprint. The
    JSON file is a local record, not a contract document: the contract copy of these facts is build-info.json, written
    from this job's outputs. A CI-built DLL whose bytes differ from the checked-in DLL is drift, reported with a
    ::notice:: and never a failure; a checked-in DLL built from other sources is caught by the fingerprint check of
    the workflow and by NativeBridgePeAuditTests.

.PARAMETER VsToolset
    MSVC toolset prefix passed to xmake as --vs_toolset (for example 14.44: the newest installed 14.44.x is used).

.PARAMETER VsSdkVersion
    Exact Windows SDK version passed to xmake as --vs_sdkver (for example 10.0.26100.0).

.PARAMETER PathCheckRoot
    Directory outside the repository that receives the copied build inputs. Defaults to bridge-path-check under
    RUNNER_TEMP, or under the system temporary directory outside GitHub Actions.

.PARAMETER Xmake
    The xmake executable. Defaults to xmake on PATH.

.EXAMPLE
    ./eng/ci/Build-NativeBridge.ps1 -VsToolset 14.44 -VsSdkVersion 10.0.26100.0

    Builds, compares and prints the facts table. Never commit the resulting DLL by hand: the checked-in DLL is replaced
    only by the lua-protection-bridge artifact of a CI run.
#>
[CmdletBinding()]
param(
	[Parameter(Mandatory)]
	[ValidatePattern('^\d+\.\d+$')]
	[string] $VsToolset,

	[Parameter(Mandatory)]
	[ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
	[string] $VsSdkVersion,

	[string] $PathCheckRoot = (Join-Path ($env:RUNNER_TEMP ?? [IO.Path]::GetTempPath()) 'bridge-path-check'),

	[string] $Xmake = 'xmake'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$InformationPreference = 'Continue'

# English compiler banners, whatever the host language.
$env:VSLANG = '1033'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$projectDirectory = 'native/cheatengine-sdk-lua-bridge'
$bridgeFileName = 'cheatengine-sdk-lua-bridge.dll'
$buildInputs = @('cheatengine_sdk_lua_bridge.c', 'xmake.lua')
$checkedInBridge = "$projectDirectory/runtimes/win-x64/native/$bridgeFileName"
$primaryOutput = 'artifacts/native/cheatengine-sdk-lua-bridge'
$reproducibilityOutput = 'artifacts/native/cheatengine-sdk-lua-bridge-repro'
$pathCheckOutput = 'artifacts/native/path-check'
$factsFile = 'artifacts/native/native-toolchain.json'
$inGitHubActions = $env:GITHUB_ACTIONS -eq 'true'

function Get-Sha256 {
	param([Parameter(Mandatory)] [string] $Path)

	return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Initialize-EmptyDirectory {
	param(
		[Parameter(Mandatory)] [string] $Path,
		[Parameter(Mandatory)] [string] $AllowedRoot
	)

	$resolved = [IO.Path]::GetFullPath($Path)
	$prefix = [IO.Path]::GetFullPath($AllowedRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
	if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
		throw "Refusing to clean '$resolved', which is outside '$prefix'."
	}
	if (Test-Path -LiteralPath $resolved) {
		Remove-Item -LiteralPath $resolved -Recurse -Force
	}
	New-Item -ItemType Directory -Path $resolved -Force | Out-Null
}

# Configures and builds the bridge from the current directory into a relative output directory, and returns the DLL.
function Invoke-BridgeBuild {
	param(
		[Parameter(Mandatory)] [string] $OutputDirectory,
		[Parameter(Mandatory)] [string] $Label
	)

	# .NET resolves relative paths against the process directory, not the PowerShell location: anchor them explicitly.
	$outputPath = [IO.Path]::GetFullPath((Join-Path $PWD.Path $OutputDirectory))
	Initialize-EmptyDirectory -Path $outputPath -AllowedRoot $PWD.Path
	# Out-Host keeps the build log visible without turning it into the function's return value.
	& $Xmake f -P $projectDirectory -o $OutputDirectory -p windows -a x64 -m release -y --ccache=n "--vs_toolset=$VsToolset" "--vs_sdkver=$VsSdkVersion" | Out-Host
	if ($LASTEXITCODE -ne 0) {
		throw "xmake configuration for the $Label bridge output failed with exit code $LASTEXITCODE."
	}
	& $Xmake -P $projectDirectory -y | Out-Host
	if ($LASTEXITCODE -ne 0) {
		throw "xmake build for the $Label bridge output failed with exit code $LASTEXITCODE."
	}

	$bridge = Join-Path $outputPath $bridgeFileName
	if (-not (Test-Path -LiteralPath $bridge -PathType Leaf)) {
		throw "xmake did not produce '$bridge'."
	}
	return $bridge
}

function Get-ExportedFingerprint {
	param([Parameter(Mandatory)] [string] $Path)

	$module = [Runtime.InteropServices.NativeLibrary]::Load($Path)
	try {
		$address = [Runtime.InteropServices.NativeLibrary]::GetExport($module, 'cheatengine_sdk_lua_bridge_source_fingerprint')
		return [Runtime.InteropServices.Marshal]::PtrToStringAnsi($address)
	}
	finally {
		[Runtime.InteropServices.NativeLibrary]::Free($module)
	}
}

# xmake 3.0.9 caches the environment of the vcvars call it made for the msvc toolchain; that is the toolset and SDK the
# build actually used. The format is internal to xmake, which is why the xmake version is pinned with this script.
function Get-ResolvedToolchain {
	$cache = Join-Path $PWD.Path '.xmake/windows/x64/cache/toolchain'
	if (-not (Test-Path -LiteralPath $cache -PathType Leaf)) {
		throw "xmake did not write its toolchain cache '$cache'; update Build-NativeBridge.ps1 together with the xmake pin."
	}
	$text = Get-Content -LiteralPath $cache -Raw

	$facts = [ordered]@{}
	foreach ($name in @('VCToolsVersion', 'WindowsSDKVersion', 'VCToolsInstallDir')) {
		$values = @([regex]::Matches($text, "\b$name\s*=\s*(?:`"(?<value>[^`"]+)`"|\[\[(?<value>[^\]]+)\]\])") |
				ForEach-Object { $_.Groups['value'].Value.Trim().TrimEnd('\') } | Sort-Object -Unique)
		if ($values.Count -ne 1) {
			throw "xmake's toolchain cache holds $($values.Count) values for $name ($($values -join ', ')); expected exactly one."
		}
		$facts[$name] = $values[0]
	}
	return $facts
}

# The PE optional header records the major.minor version of the linker that produced the image: byte-level evidence of
# the toolset, independent of what xmake reports.
function Get-LinkerVersion {
	param([Parameter(Mandatory)] [string] $Path)

	$stream = [IO.File]::OpenRead($Path)
	try {
		$reader = [Reflection.PortableExecutable.PEReader]::new($stream)
		try {
			$header = $reader.PEHeaders.PEHeader
			return "$($header.MajorLinkerVersion).$($header.MinorLinkerVersion)"
		}
		finally {
			$reader.Dispose()
		}
	}
	finally {
		$stream.Dispose()
	}
}

function Get-CompilerVersion {
	param([Parameter(Mandatory)] [string] $ToolsInstallDirectory)

	$compiler = Join-Path $ToolsInstallDirectory 'bin/HostX64/x64/cl.exe'
	if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
		throw "The resolved toolset has no x64 compiler at '$compiler'."
	}
	# Without arguments cl.exe prints its banner and usage, and exits 0.
	$banner = @(& $compiler 2>&1 | ForEach-Object { "$_" })
	if ($LASTEXITCODE -ne 0) {
		throw "'$compiler' exited with code $LASTEXITCODE while printing its banner."
	}
	foreach ($line in $banner) {
		if ($line -match 'C/C\+\+.*?\b(?<version>\d+\.\d+\.\d+(?:\.\d+)?)\b') {
			return $Matches['version']
		}
	}
	throw "Could not read the compiler version from the banner of '$compiler': $($banner -join ' | ')"
}

function Write-GitHubFile {
	param(
		[Parameter(Mandatory)] [AllowEmptyString()] [string] $Path,
		[Parameter(Mandatory)] [AllowEmptyString()] [string[]] $Line
	)

	if ($Path) {
		$Line | Out-File -FilePath $Path -Append -Encoding utf8
	}
}

Push-Location -LiteralPath $repositoryRoot
try {
	# Record the checked-in DLL before anything could replace it.
	$checkedInSha256 = Get-Sha256 -Path $checkedInBridge
	$checkedInLinkerVersion = Get-LinkerVersion -Path (Join-Path $repositoryRoot $checkedInBridge)
	$sourceHashes = foreach ($inputFile in $buildInputs) { Get-Sha256 -Path "$projectDirectory/$inputFile" }
	$sourceFingerprint = $sourceHashes -join ':'

	$xmakeBanner = @(& $Xmake --version 2>&1 | ForEach-Object { "$_" -replace '\x1b\[[0-9;]*m', '' })
	if ($LASTEXITCODE -ne 0) {
		throw "xmake --version failed with exit code $LASTEXITCODE."
	}
	$xmakeVersionLine = $xmakeBanner | Where-Object { $_ -match '\bxmake v\d+\.\d+\.\d+' } | Select-Object -First 1
	if (-not $xmakeVersionLine -or -not ($xmakeVersionLine -match '\bxmake v(?<version>\d+\.\d+\.\d+)')) {
		throw "Could not read the xmake version from '$($xmakeBanner -join ' | ')'."
	}
	$xmakeVersion = $Matches['version']

	$primaryOutputPath = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $primaryOutput))
	$reproducibilityOutputPath = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $reproducibilityOutput))
	if ([string]::Equals($primaryOutputPath, $reproducibilityOutputPath, [StringComparison]::OrdinalIgnoreCase)) {
		throw 'The primary and reproducibility bridge output directories must be distinct.'
	}

	$primaryBridge = Invoke-BridgeBuild -OutputDirectory $primaryOutput -Label 'primary'
	$toolchain = Get-ResolvedToolchain
	$reproducibilityBridge = Invoke-BridgeBuild -OutputDirectory $reproducibilityOutput -Label 'reproducibility'

	$primaryHash = Get-Sha256 -Path $primaryBridge
	$reproducibilityHash = Get-Sha256 -Path $reproducibilityBridge
	if (-not [string]::Equals($primaryHash, $reproducibilityHash, [StringComparison]::OrdinalIgnoreCase)) {
		throw "The native bridge is not reproducible: '$primaryBridge' SHA-256 is $primaryHash but '$reproducibilityBridge' is $reproducibilityHash."
	}
}
finally {
	Pop-Location
}

# Path independence: the same two inputs, byte for byte, in a different directory at a different depth.
$resolvedPathCheckRoot = [IO.Path]::GetFullPath($PathCheckRoot, $PWD.Path)
$repositoryPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if ("$resolvedPathCheckRoot$([IO.Path]::DirectorySeparatorChar)".StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
	throw "The path-check root '$resolvedPathCheckRoot' must be outside the repository '$repositoryRoot'."
}
Initialize-EmptyDirectory -Path $resolvedPathCheckRoot -AllowedRoot (Split-Path -Path $resolvedPathCheckRoot -Parent)
$copiedProject = Join-Path $resolvedPathCheckRoot $projectDirectory
New-Item -ItemType Directory -Path $copiedProject -Force | Out-Null
foreach ($inputFile in $buildInputs) {
	Copy-Item -LiteralPath (Join-Path $repositoryRoot "$projectDirectory/$inputFile") -Destination $copiedProject
}

Push-Location -LiteralPath $resolvedPathCheckRoot
try {
	$pathCheckBridge = Invoke-BridgeBuild -OutputDirectory $pathCheckOutput -Label 'path-check'
}
finally {
	Pop-Location
}
$pathCheckHash = Get-Sha256 -Path $pathCheckBridge
if (-not [string]::Equals($primaryHash, $pathCheckHash, [StringComparison]::OrdinalIgnoreCase)) {
	throw "The native bridge depends on its build path: '$primaryBridge' SHA-256 is $primaryHash but the copy built under '$resolvedPathCheckRoot' is $pathCheckHash. Remove the embedded path (for example a /pathmap: flag in xmake.lua, which changes the fingerprint and needs a CI-built DLL)."
}

$exportedFingerprint = Get-ExportedFingerprint -Path $primaryBridge
if ($exportedFingerprint -cne $sourceFingerprint) {
	throw "The CI-built bridge exports fingerprint '$exportedFingerprint', expected '$sourceFingerprint' from its build inputs."
}

$msvcToolset = $toolchain['VCToolsVersion']
$windowsSdk = $toolchain['WindowsSDKVersion']
if (-not $msvcToolset.StartsWith("$VsToolset.", [StringComparison]::Ordinal)) {
	throw "xmake resolved MSVC toolset $msvcToolset although --vs_toolset=$VsToolset was requested."
}
if ($windowsSdk -cne $VsSdkVersion) {
	throw "xmake resolved Windows SDK $windowsSdk although --vs_sdkver=$VsSdkVersion was requested."
}
$msvcVersion = Get-CompilerVersion -ToolsInstallDirectory $toolchain['VCToolsInstallDir']
$linkerVersion = Get-LinkerVersion -Path $primaryBridge
$expectedLinkerVersion = ($msvcToolset -split '\.')[0..1] -join '.'
if ($linkerVersion -cne $expectedLinkerVersion) {
	throw "The CI-built bridge records linker $linkerVersion in its PE header, but xmake resolved MSVC toolset $msvcToolset."
}

$imageOs = $env:ImageOS
$imageVersion = $env:ImageVersion
if ($inGitHubActions -and (-not $imageOs -or -not $imageVersion)) {
	throw 'GitHub Actions did not provide ImageOS and ImageVersion; the runner image cannot be recorded.'
}
$imageOs = $imageOs ? $imageOs : 'local'
$imageVersion = $imageVersion ? $imageVersion : 'local'
$drift = $primaryHash -ne $checkedInSha256

$facts = [ordered]@{
	imageOs = $imageOs
	imageVersion = $imageVersion
	xmake = $xmakeVersion
	vsToolsetPin = $VsToolset
	vsSdkVersionPin = $VsSdkVersion
	msvcToolset = $msvcToolset
	msvcVersion = $msvcVersion
	windowsSdk = $windowsSdk
	bridge = [ordered]@{
		sha256 = $primaryHash
		reproducibilitySha256 = $reproducibilityHash
		pathCheckSha256 = $pathCheckHash
		linkerVersion = $linkerVersion
		checkedInSha256 = $checkedInSha256
		checkedInLinkerVersion = $checkedInLinkerVersion
		sourceFingerprint = $sourceFingerprint
		driftFromCheckedIn = $drift
	}
}
$factsPath = Join-Path $repositoryRoot $factsFile
$facts | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $factsPath -Encoding utf8NoBOM

Write-GitHubFile -Path ($env:GITHUB_OUTPUT ?? '') -Line @(
	"image-version=$imageVersion"
	"xmake-version=$xmakeVersion"
	"msvc-toolset=$msvcToolset"
	"msvc-version=$msvcVersion"
	"windows-sdk-version=$windowsSdk"
	"bridge-sha256=$primaryHash"
	"bridge-fingerprint=$sourceFingerprint"
	"checked-in-bridge-sha256=$checkedInSha256"
)

$summary = @(
	'### Native bridge toolchain'
	''
	'| Fact | Value |'
	'| --- | --- |'
	"| Runner image | ``$imageOs`` ``$imageVersion`` |"
	"| xmake | ``$xmakeVersion`` |"
	"| MSVC toolset (pin ``$VsToolset``) | ``$msvcToolset`` |"
	"| MSVC compiler | ``$msvcVersion`` |"
	"| Windows SDK (pin ``$VsSdkVersion``) | ``$windowsSdk`` |"
	"| CI-built bridge SHA-256 (primary = reproducibility = path check) | ``$primaryHash`` |"
	"| CI-built bridge linker version (PE header) | ``$linkerVersion`` |"
	"| Checked-in bridge SHA-256 | ``$checkedInSha256`` |"
	"| Checked-in bridge linker version (PE header) | ``$checkedInLinkerVersion`` |"
	"| Source fingerprint | ``$sourceFingerprint`` |"
	"| Drift from the checked-in DLL | $(if ($drift) { 'yes (notice)' } else { 'no' }) |"
)
Write-GitHubFile -Path ($env:GITHUB_STEP_SUMMARY ?? '') -Line $summary
$summary | ForEach-Object { Write-Information $_ }

if ($drift) {
	$message = "The CI-built bridge ($primaryHash) differs from the checked-in DLL ($checkedInSha256). This is drift, not a failure: download the lua-protection-bridge artifact of this run to refresh the checked-in DLL deliberately."
	if ($inGitHubActions) {
		Write-Host "::notice title=Native bridge drift::$message"
	}
	else {
		Write-Information $message
	}
}
