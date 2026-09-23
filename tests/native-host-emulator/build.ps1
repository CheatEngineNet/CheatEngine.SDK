[CmdletBinding()]
param(
	[Parameter()]
	[string]$OutputDirectory = (Join-Path (Get-Location) 'artifacts/native-host-emulator'),

	[Parameter()]
	[string]$VcToolsVersion,

	[Parameter()]
	[string]$DotNetRoot = (Split-Path -Parent (Get-Command dotnet.exe -ErrorAction Stop).Source)
)

$ErrorActionPreference = 'Stop'

if (-not [Environment]::Is64BitOperatingSystem) {
	throw 'The native host emulator requires a 64-bit Windows host.'
}

function Import-X64VisualStudioEnvironment {
	param([string]$RequestedVcToolsVersion)

	$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
	if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
		throw "cl.exe was not found and '$vswhere' is unavailable to locate the Visual Studio build tools."
	}

	$installationPath = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
		-property installationPath
	if ([string]::IsNullOrWhiteSpace($installationPath)) {
		throw 'No Visual Studio installation with the x64 C++ build tools was found.'
	}

	$developerCommand = Join-Path $installationPath.Trim() 'Common7\Tools\VsDevCmd.bat'
	if (-not (Test-Path -LiteralPath $developerCommand -PathType Leaf)) {
		throw "Visual Studio did not contain '$developerCommand'."
	}

	# Pin the toolset when the caller supplies one (CI: the 'native' job's MSVC version output). Left unset locally,
	# vswhere picks whatever toolset is installed, which can be a newer major Visual Studio version than CI's pinned
	# runner image and can therefore accept or reject different /W4 warnings.
	$vcToolsArgument = if ($RequestedVcToolsVersion) { "-vcvars_ver=$RequestedVcToolsVersion" } else { '' }
	$command = '"{0}" -no_logo -arch=x64 -host_arch=x64 {1} && set' -f $developerCommand, $vcToolsArgument
	$environment = @(& $env:ComSpec /d /s /c $command)
	if ($LASTEXITCODE -ne 0) { throw "VsDevCmd.bat failed with exit code $LASTEXITCODE." }

	foreach ($line in $environment) {
		$separator = $line.IndexOf('=')
		if ($separator -le 0) { continue }

		$name = $line.Substring(0, $separator)
		$value = $line.Substring($separator + 1)
		Set-Item -LiteralPath "Env:$name" -Value $value
	}
}

$compiler = Get-Command cl.exe -ErrorAction SilentlyContinue
if ($null -eq $compiler -or $env:VSCMD_ARG_TGT_ARCH -ne 'x64') {
	Import-X64VisualStudioEnvironment -RequestedVcToolsVersion $VcToolsVersion
	$compiler = Get-Command cl.exe -ErrorAction SilentlyContinue
}

if ($null -eq $compiler -or $env:VSCMD_ARG_TGT_ARCH -ne 'x64') {
	throw 'The native host emulator requires cl.exe from an x64 Visual Studio Developer environment.'
}

if (-not (Test-Path -LiteralPath $DotNetRoot -PathType Container)) {
	throw "-DotNetRoot '$DotNetRoot' does not exist."
}

# Locate the newest installed Microsoft.NETCore.App.Host.win-x64 pack: it carries nethost.h, nethost.lib, nethost.dll,
# hostfxr.h and coreclr_delegates.h. Never read an installed Cheat Engine; this pack ships with the .NET SDK.
$hostPackRoot = Join-Path $DotNetRoot 'packs\Microsoft.NETCore.App.Host.win-x64'
if (-not (Test-Path -LiteralPath $hostPackRoot -PathType Container)) {
	throw "'$hostPackRoot' does not exist: install the Microsoft.NETCore.App.Host.win-x64 pack (it ships with the .NET SDK)."
}

$hostPackVersionDirectory = Get-ChildItem -LiteralPath $hostPackRoot -Directory |
	Sort-Object { [version] ($_.Name -replace '-.*$', '') } -Descending |
	Select-Object -First 1
if ($null -eq $hostPackVersionDirectory) {
	throw "No version directory was found under '$hostPackRoot'."
}

$nativePackDirectory = Join-Path $hostPackVersionDirectory.FullName 'runtimes\win-x64\native'
foreach ($requiredFile in 'nethost.h', 'nethost.lib', 'nethost.dll', 'hostfxr.h', 'coreclr_delegates.h') {
	if (-not (Test-Path -LiteralPath (Join-Path $nativePackDirectory $requiredFile) -PathType Leaf)) {
		throw "'$requiredFile' was not found under '$nativePackDirectory'."
	}
}

$emulatorDirectory = Split-Path -Parent $PSCommandPath
# Resolve against the PowerShell location, not the process working directory (see tests/native-abi-fixture/build.ps1
# for the same rationale): Set-Location does not move the latter.
$resolvedOutputDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($resolvedOutputDirectory) | Out-Null

$exe = Join-Path $resolvedOutputDirectory 'ce-host-emulator.exe'
$objectFile = Join-Path $resolvedOutputDirectory 'ce_host_emulator.obj'
$sourceFile = Join-Path $emulatorDirectory 'ce_host_emulator.cpp'
$headerFile = Join-Path $emulatorDirectory 'ce_host_emulator_abi.h'
$runtimeConfigSource = Join-Path $emulatorDirectory 'ce-like.runtimeconfig.json'

$common = @('/nologo', '/std:c++20', '/W4', '/WX', '/EHsc', '/O2', '/DUNICODE', '/I', $emulatorDirectory, '/I', $nativePackDirectory)
& $compiler.Source @common $sourceFile "/Fo$objectFile" "/Fe$exe" '/link' (Join-Path $nativePackDirectory 'nethost.lib') '/MACHINE:X64'
if ($LASTEXITCODE -ne 0) { throw "cl.exe failed while building $exe (exit code $LASTEXITCODE)." }

$nethostDllDestination = Join-Path $resolvedOutputDirectory 'nethost.dll'
Copy-Item -LiteralPath (Join-Path $nativePackDirectory 'nethost.dll') -Destination $nethostDllDestination -Force

$runtimeConfigDestination = Join-Path $resolvedOutputDirectory 'ce-like.runtimeconfig.json'
Copy-Item -LiteralPath $runtimeConfigSource -Destination $runtimeConfigDestination -Force

function Get-FileSha256Hex {
	param([Parameter(Mandatory)][string]$LiteralPath)

	return (Get-FileHash -LiteralPath $LiteralPath -Algorithm SHA256).Hash.ToLowerInvariant()
}

$manifestLines = [System.Collections.Generic.List[string]]::new()
$manifestLines.Add("schema=1")
$manifestLines.Add("exe.sha256=$(Get-FileSha256Hex -LiteralPath $exe)")
$manifestLines.Add("nethost.dll.sha256=$(Get-FileSha256Hex -LiteralPath $nethostDllDestination)")
$manifestLines.Add("runtimeconfig.sha256=$(Get-FileSha256Hex -LiteralPath $runtimeConfigDestination)")
$manifestLines.Add("source.ce_host_emulator_cpp.sha256=$(Get-FileSha256Hex -LiteralPath $sourceFile)")
$manifestLines.Add("source.ce_host_emulator_abi_h.sha256=$(Get-FileSha256Hex -LiteralPath $headerFile)")
$manifestLines.Add("msvc.toolset=$env:VCToolsVersion")
$manifestLines.Add("windows.sdk=$env:WindowsSDKVersion")
$manifestLines.Add("nethost.pack=$($hostPackVersionDirectory.Name)")

$manifestPath = Join-Path $resolvedOutputDirectory 'native-host-emulator.manifest.txt'
$manifestLines | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM

$manifestLines
