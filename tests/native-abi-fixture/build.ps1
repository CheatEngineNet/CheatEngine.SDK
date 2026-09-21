[CmdletBinding()]
param(
    [Parameter()]
    [string]$OutputDirectory = (Join-Path (Get-Location) 'artifacts/native-abi-fixture')
)

$ErrorActionPreference = 'Stop'

if (-not [Environment]::Is64BitOperatingSystem) {
    throw 'The CE 7.7 native ABI fixture requires a 64-bit Windows host.'
}

function Import-X64VisualStudioEnvironment {
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

    $command = '"{0}" -no_logo -arch=x64 -host_arch=x64 && set' -f $developerCommand
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
    Import-X64VisualStudioEnvironment
    $compiler = Get-Command cl.exe -ErrorAction SilentlyContinue
}

if ($null -eq $compiler -or $env:VSCMD_ARG_TGT_ARCH -ne 'x64') {
    throw 'The fixture requires cl.exe from an x64 Visual Studio Developer environment.'
}

$fixtureDirectory = Split-Path -Parent $PSCommandPath
$resolvedOutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($resolvedOutputDirectory) | Out-Null

$common = @('/nologo', '/std:c++20', '/W4', '/WX', '/EHsc', '/O2', '/I', $fixtureDirectory)
$dll = Join-Path $resolvedOutputDirectory 'ce77-native-abi-fixture.dll'
$probe = Join-Path $resolvedOutputDirectory 'ce77-native-abi-probe.exe'
$facts = Join-Path $resolvedOutputDirectory 'ce77-native-abi-facts.txt'

& $compiler.Source @common '/LD' (Join-Path $fixtureDirectory 'ce77_native_abi_fixture.cpp') "/Fo$(Join-Path $resolvedOutputDirectory 'ce77_native_abi_fixture.obj')" "/Fe$dll" '/link' '/MACHINE:X64' "/DEF:$(Join-Path $fixtureDirectory 'ce77_native_abi_fixture.def')"
if ($LASTEXITCODE -ne 0) { throw "cl.exe failed while building $dll (exit code $LASTEXITCODE)." }

& $compiler.Source @common (Join-Path $fixtureDirectory 'ce77_native_abi_probe.cpp') "/Fo$(Join-Path $resolvedOutputDirectory 'ce77_native_abi_probe.obj')" "/Fe$probe" '/link' '/MACHINE:X64'
if ($LASTEXITCODE -ne 0) { throw "cl.exe failed while building $probe (exit code $LASTEXITCODE)." }

$factLines = @(& $probe $dll)
if ($LASTEXITCODE -ne 0) { throw "The native ABI probe failed (exit code $LASTEXITCODE)." }

$factLines | Set-Content -LiteralPath $facts -Encoding utf8NoBOM
& (Join-Path $fixtureDirectory 'Validate-Facts.ps1') -FactsPath $facts
if ($LASTEXITCODE -ne 0) { throw "The native ABI facts validator failed (exit code $LASTEXITCODE)." }

$factLines
