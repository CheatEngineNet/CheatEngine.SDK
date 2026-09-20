[CmdletBinding()]
param(
    [Parameter()]
    [string]$OutputDirectory = (Join-Path (Get-Location) 'artifacts/native-abi-fixture')
)

$ErrorActionPreference = 'Stop'

if (-not [Environment]::Is64BitOperatingSystem) {
    throw 'The CE 7.7 native ABI fixture requires a 64-bit Windows host.'
}

$compiler = Get-Command cl.exe -ErrorAction SilentlyContinue
if ($null -eq $compiler) {
    throw 'cl.exe was not found. Run this script from an x64 Visual Studio Developer PowerShell.'
}

$fixtureDirectory = Split-Path -Parent $PSCommandPath
$resolvedOutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($resolvedOutputDirectory) | Out-Null

$common = @('/nologo', '/std:c++20', '/W4', '/WX', '/EHsc', '/O2', '/I', $fixtureDirectory)
$dll = Join-Path $resolvedOutputDirectory 'ce77-native-abi-fixture.dll'
$probe = Join-Path $resolvedOutputDirectory 'ce77-native-abi-probe.exe'

& $compiler.Source @common '/LD' (Join-Path $fixtureDirectory 'ce77_native_abi_fixture.cpp') "/Fo$(Join-Path $resolvedOutputDirectory 'ce77_native_abi_fixture.obj')" "/Fe$dll" '/link' '/MACHINE:X64' "/DEF:$(Join-Path $fixtureDirectory 'ce77_native_abi_fixture.def')"
if ($LASTEXITCODE -ne 0) { throw "cl.exe failed while building $dll (exit code $LASTEXITCODE)." }

& $compiler.Source @common (Join-Path $fixtureDirectory 'ce77_native_abi_probe.cpp') "/Fo$(Join-Path $resolvedOutputDirectory 'ce77_native_abi_probe.obj')" "/Fe$probe" '/link' '/MACHINE:X64'
if ($LASTEXITCODE -ne 0) { throw "cl.exe failed while building $probe (exit code $LASTEXITCODE)." }

& $probe $dll
if ($LASTEXITCODE -ne 0) { throw "The native ABI probe failed (exit code $LASTEXITCODE)." }
