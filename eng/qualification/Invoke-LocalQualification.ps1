#Requires -Version 7.4
<#
.SYNOPSIS
    Runs exact-host (C3/C4) qualification scenarios on a sandbox copy of Cheat Engine 7.7 and writes redacted receipts.

.DESCRIPTION
    Local and operator-attended only: the script refuses to run in CI. It never writes to the Cheat Engine installation;
    it copies it into a sandbox under -WorkRoot, verifies every file against the installation and the host facts
    against docs/qualification/support-profile.json, builds the plugin harnesses from the exact CheatEngine.SDK package
    (-PackagePath) in clean folders with an isolated NuGet packages folder, starts the qualification target, drives
    Cheat Engine with a generated autorun Lua driver, restores HKCU\Software\Cheat Engine when a run changed it, and
    writes one receipt and one event log per scenario (schemas under docs/qualification/schemas).

    Stages: guard, preflight, Global\ce-lab mutex, sandbox, bundles, bridge identity, targets, HKCU export, driver,
    launch, cleanup, HKCU compare and restore, redaction and receipts, optional publication. See
    eng/qualification/README.md and docs/qualification/local-protocol.md.

    Exit codes: 0 success; 2 invalid arguments; 3 CI environment; 4 host or profile mismatch; 5 unsafe environment;
    6 Cheat Engine, build, driver or any other unhandled failure; 7 HKCU restore or verification failure (critical: the
    restore command and the backup path are printed).

.PARAMETER Scenario
    Scenario ids of eng/qualification/scenarios.json (for example Q04, Q09.a) or CheckpointB for every runnable one.

.PARAMETER PackagePath
    The exact CheatEngine.SDK .nupkg to qualify: the CI artifact (gh run download <runId> -n nuget-package) or the
    nuget.org file. Required unless -PreflightOnly.

.PARAMETER PackageSource
    CiArtifact (default; requires -CiRunUrl) or NuGetOrg. Local packs are never qualification inputs; use -Smoke.

.PARAMETER Smoke
    Plumbing run with a local pack: writes smoke-report.json and never a receipt. The repository may be dirty.

.EXAMPLE
    ./eng/qualification/Invoke-LocalQualification.ps1 -PreflightOnly -WhatIf

.EXAMPLE
    ./eng/qualification/Invoke-LocalQualification.ps1 -Scenario Q04,Q14 -PackagePath .\nuget-package\CheatEngine.SDK.2.0.0-alpha.0.42.nupkg -CiRunUrl https://github.com/CheatEngineNet/CheatEngine.SDK/actions/runs/1 -PullRequest 86 -HeadSha <sha> -Operator <handle>
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string[]] $Scenario = @('CheckpointB'),
    [string] $PackagePath = '',
    [ValidateSet('CiArtifact', 'NuGetOrg')] [string] $PackageSource = 'CiArtifact',
    [string] $CiRunUrl = '',
    [int] $PullRequest = 0,
    [string] $HeadSha = '',
    [string] $Operator = '',
    [string] $CheatEnginePath = '',
    [string] $WorkRoot = '',
    [ValidateRange(30, 7200)] [int] $CeTimeoutSeconds = 900,
    [ValidateRange(1, 1440)] [int] $MutexTimeoutMinutes = 30,
    [switch] $PreflightOnly,
    [switch] $Smoke,
    [switch] $AllowConcurrentLoad,
    [switch] $AllowOtherCheatEngineInstances,
    [string] $PublishReceiptsTo = ''
)

# Stage 1, guard: the first statement. Nothing above has a side effect; nothing below runs in CI.
foreach ($ciMarker in 'CI', 'GITHUB_ACTIONS', 'TF_BUILD') {
    if (-not [string]::IsNullOrEmpty([System.Environment]::GetEnvironmentVariable($ciMarker))) {
        [System.Console]::Error.WriteLine("Invoke-LocalQualification.ps1 never runs in CI ($ciMarker is set): it starts Cheat Engine on an operator's machine.")
        exit 3
    }
}

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$InformationPreference = 'Continue'

# Any error nothing handles (a failing build, a missing tool, a target that never gets ready) ends the run with the
# documented code 6 instead of PowerShell's generic 1. Every finally block still runs first: the target is stopped,
# the driver, manifest and fault switch are removed, HKCU is compared and restored, and Global\ce-lab is released.
trap {
    [System.Console]::Error.WriteLine("Invoke-LocalQualification.ps1: $($_.Exception.Message) (exit 6)")
    exit 6
}

Import-Module (Join-Path $PSScriptRoot 'QualificationRunner.psm1') -Force

$ProfileId = 'ce-7.7.0.10621-x64-managed-hostfxr'
$RegistryKey = 'HKCU\Software\Cheat Engine'
$DriverFileName = 'zz_cesdk_qualification.lua'
$LiveProbeAcknowledgement = 'I_AUTHORIZE_CE77_LIVE_PROBES_ON_A_DISPOSABLE_TARGET'
$Harnesses = [ordered]@{
    LiveProbe         = @{ Sources = @('tests/CheatEngine.SDK.LiveProbe'); Assembly = 'CheatEngine.SDK.LiveProbe'; Namespace = 'LiveProbe'; Constants = ''; GenerateEntryPoint = 'false' }
    LiveProbeNonAscii = @{ Sources = @('tests/CheatEngine.SDK.LiveProbe'); Assembly = 'CheatEngine.SDK.LiveProbe'; Namespace = 'LiveProbe'; Constants = 'LIVEPROBE_NON_ASCII_NAME'; GenerateEntryPoint = 'false' }
    LivePlugin        = @{ Sources = @('tests/CheatEngine.SDK.LivePlugin'); Assembly = 'CheatEngine.SDK.LivePlugin'; Namespace = 'LivePlugin'; Constants = ''; GenerateEntryPoint = 'true' }
    CoexistenceA      = @{ Sources = @('tests/CheatEngine.SDK.LivePlugin.Coexistence/PluginA', 'tests/CheatEngine.SDK.LivePlugin.Coexistence/CoexistenceDiagnostics.cs'); Assembly = 'CheatEngine.SDK.LivePlugin.Coexistence.PluginA'; Namespace = 'LivePlugin.Coexistence.PluginA'; Constants = ''; GenerateEntryPoint = 'true' }
    CoexistenceB      = @{ Sources = @('tests/CheatEngine.SDK.LivePlugin.Coexistence/PluginB', 'tests/CheatEngine.SDK.LivePlugin.Coexistence/CoexistenceDiagnostics.cs'); Assembly = 'CheatEngine.SDK.LivePlugin.Coexistence.PluginB'; Namespace = 'LivePlugin.Coexistence.PluginB'; Constants = ''; GenerateEntryPoint = 'true' }
}

function Exit-Qualification {
    <#
    .SYNOPSIS
        Writes the reason to standard error and exits with the documented code (finally blocks still run).
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)] [int] $Code, [Parameter(Mandatory)] [string] $Reason)

    [System.Console]::Error.WriteLine("Invoke-LocalQualification.ps1: $Reason (exit $Code)")
    exit $Code
}

function Invoke-Native {
    <#
    .SYNOPSIS
        Runs a native command, captures its output and throws on a failing exit code (robocopy: 8 and above).
    #>
    [CmdletBinding()]
    [OutputType([string[]])]
    param(
        [Parameter(Mandatory)] [string] $FilePath,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $ArgumentList,
        [int] $FailureThreshold = 1
    )

    $output = @(& $FilePath @ArgumentList 2>&1 | Where-Object { $null -ne $_ } | ForEach-Object { "$_" })
    if ($LASTEXITCODE -ge $FailureThreshold) {
        throw "$FilePath $($ArgumentList -join ' ') failed with exit code $LASTEXITCODE`: $(($output | Select-Object -Last 20) -join [System.Environment]::NewLine)"
    }

    return $output
}

function Get-RepositoryRoot {
    [CmdletBinding()]
    [OutputType([string])]
    param()

    $root = Invoke-Native -FilePath 'git' -ArgumentList @('-C', $PSScriptRoot, 'rev-parse', '--show-toplevel')
    return [System.IO.Path]::GetFullPath(($root | Select-Object -First 1).Trim())
}

function Get-PeMachine {
    <#
    .SYNOPSIS
        The COFF machine of a PE file (AMD64, I386, ...), read without loading it.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)] [string] $Path)

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $reader = [System.Reflection.PortableExecutable.PEReader]::new($stream)
        try { return $reader.PEHeaders.CoffHeader.Machine.ToString().ToUpperInvariant() }
        finally { $reader.Dispose() }
    }
    finally {
        $stream.Dispose()
    }
}

function Get-CheatEngineProcess {
    [CmdletBinding()]
    [OutputType([System.Diagnostics.Process[]])]
    param()

    return @(Get-Process | Where-Object { Test-CheatEngineProcessName -Name $_.ProcessName })
}

function Invoke-Preflight {
    <#
    .SYNOPSIS
        Stage 2, read-only: hashes the installation, compares it with the support profile and checks the environment.
    #>
    [CmdletBinding()]
    [OutputType([System.Collections.Specialized.OrderedDictionary])]
    param(
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [object] $SupportProfile
    )

    $expectedHost = $SupportProfile.host
    $exe = Join-Path $CheatEnginePath $expectedHost.exeName
    foreach ($file in @($exe, (Join-Path $CheatEnginePath $SupportProfile.lua.module), (Join-Path $CheatEnginePath 'ce.runtimeconfig.json'), (Join-Path $CheatEnginePath 'celua.txt'))) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { Exit-Qualification -Code 4 -Reason "The profiled file '$file' is missing." }
    }

    $facts = [ordered]@{
        profileId           = $ProfileId
        ceExeName           = $expectedHost.exeName
        ceExeSha256         = Get-QualificationFileSha256 -Path $exe
        ceFileVersion       = (Get-Item -LiteralPath $exe).VersionInfo.FileVersion
        ceMachine           = Get-PeMachine -Path $exe
        luaDllSha256        = Get-QualificationFileSha256 -Path (Join-Path $CheatEnginePath $SupportProfile.lua.module)
        runtimeconfigSha256 = Get-QualificationFileSha256 -Path (Join-Path $CheatEnginePath 'ce.runtimeconfig.json')
        celuaSha256         = Get-QualificationFileSha256 -Path (Join-Path $CheatEnginePath 'celua.txt')
    }
    $expected = [ordered]@{
        ceExeSha256         = $expectedHost.exeSha256
        ceFileVersion       = $expectedHost.version
        ceMachine           = $expectedHost.machine
        luaDllSha256        = $SupportProfile.lua.sha256
        runtimeconfigSha256 = $SupportProfile.runtime.runtimeconfig.sha256
        celuaSha256         = $SupportProfile.celua.sha256
    }
    $mismatches = @(foreach ($name in $expected.Keys) { if ($facts[$name] -cne $expected[$name]) { "$name is $($facts[$name]), the profile says $($expected[$name])" } })
    $facts.matchesProfile = $mismatches.Count -eq 0
    $facts.mismatches = $mismatches

    $principal = [System.Security.Principal.WindowsPrincipal]::new([System.Security.Principal.WindowsIdentity]::GetCurrent())
    $facts.elevated = $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
    $facts.otherCheatEngineProcesses = @(Get-CheatEngineProcess | ForEach-Object { $_.ProcessName })
    $facts.buildProcesses = @(Get-Process -Name 'dotnet', 'MSBuild', 'VBCSCompiler' -ErrorAction SilentlyContinue).Count
    $facts.repositoryDirty = @(Invoke-Native -FilePath 'git' -ArgumentList @('-C', $RepositoryRoot, 'status', '--porcelain')).Count -gt 0
    $globalJson = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'global.json') -Raw | ConvertFrom-Json
    Push-Location $RepositoryRoot
    try { $facts.dotnetSdk = (Invoke-Native -FilePath 'dotnet' -ArgumentList @('--version') | Select-Object -First 1).Trim() }
    finally { Pop-Location }
    $facts.expectedDotnetSdk = $globalJson.sdk.version
    $facts.dotnetRuntimes = @(Invoke-Native -FilePath 'dotnet' -ArgumentList @('--list-runtimes') | ForEach-Object { ($_ -replace '\s*\[.*\]\s*$', '').Trim() } | Where-Object { $_ })
    $facts.osVersion = [System.Environment]::OSVersion.VersionString
    return $facts
}

function Test-PreflightSafety {
    <#
    .SYNOPSIS
        Turns the preflight facts into the refusals of stage 2.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Facts,
        [switch] $AllowOthers,
        [switch] $AllowLoad,
        [switch] $AllowDirty
    )

    if (-not $Facts.matchesProfile) { Exit-Qualification -Code 4 -Reason ('The installation is not the profiled host: ' + ($Facts.mismatches -join '; ')) }
    if ($Facts.elevated) { Exit-Qualification -Code 5 -Reason 'The runner is elevated; qualification runs Cheat Engine as the invoking user only.' }
    if ($Facts.otherCheatEngineProcesses.Count -gt 0 -and -not $AllowOthers) {
        Exit-Qualification -Code 5 -Reason "Another Cheat Engine instance runs ($($Facts.otherCheatEngineProcesses -join ', ')). Close it, or pass -AllowOtherCheatEngineInstances (HKCU is then never restored automatically)."
    }
    if ($Facts.buildProcesses -gt 0 -and -not $AllowLoad) {
        Exit-Qualification -Code 5 -Reason "$($Facts.buildProcesses) dotnet/MSBuild/VBCSCompiler process(es) run; timings would be distorted. Wait, or pass -AllowConcurrentLoad (timings are then marked indicative)."
    }
    if ($Facts.repositoryDirty -and -not $AllowDirty) { Exit-Qualification -Code 5 -Reason 'The repository tree is dirty; a receipt must name a committed tree.' }
    if ($Facts.dotnetSdk -ne $Facts.expectedDotnetSdk) { Exit-Qualification -Code 5 -Reason "dotnet --version is $($Facts.dotnetSdk), global.json pins $($Facts.expectedDotnetSdk)." }
}

function Get-PackageIdentity {
    <#
    .SYNOPSIS
        Reads id and version from the package's .nuspec (never from the file name) and hashes the package and its bridge.
    #>
    [CmdletBinding()]
    [OutputType([System.Collections.Specialized.OrderedDictionary])]
    param([Parameter(Mandatory)] [string] $Path, [Parameter(Mandatory)] [string] $ExtractTo)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $nuspec = @($zip.Entries | Where-Object { $_.FullName -notmatch '/' -and $_.Name -like '*.nuspec' }) | Select-Object -First 1
        if ($null -eq $nuspec) { throw "$Path contains no .nuspec." }
        $reader = [System.IO.StreamReader]::new($nuspec.Open())
        try { [xml] $manifest = $reader.ReadToEnd() }
        finally { $reader.Dispose() }
        $bridgeEntry = $zip.GetEntry('build/native/cheatengine-sdk-lua-bridge.dll')
        if ($null -eq $bridgeEntry) { throw "$Path contains no build/native/cheatengine-sdk-lua-bridge.dll." }
        $null = New-Item -ItemType Directory -Force -Path $ExtractTo
        $bridgePath = Join-Path $ExtractTo 'cheatengine-sdk-lua-bridge.dll'
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($bridgeEntry, $bridgePath, $true)
    }
    finally {
        $zip.Dispose()
    }

    return [ordered]@{
        id          = $manifest.package.metadata.id
        version     = $manifest.package.metadata.version
        nupkgSha256 = Get-QualificationFileSha256 -Path $Path
        bridgePath  = $bridgePath
        bridgeSha256 = Get-QualificationFileSha256 -Path $bridgePath
    }
}

function Get-BridgeFingerprint {
    <#
    .SYNOPSIS
        The source fingerprint the bridge exports (the same read as the CI native job).
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)] [string] $BridgePath)

    $module = [System.Runtime.InteropServices.NativeLibrary]::Load($BridgePath)
    try {
        $address = [System.Runtime.InteropServices.NativeLibrary]::GetExport($module, 'cheatengine_sdk_lua_bridge_source_fingerprint')
        return [System.Runtime.InteropServices.Marshal]::PtrToStringAnsi($address)
    }
    finally {
        [System.Runtime.InteropServices.NativeLibrary]::Free($module)
    }
}

function Build-Harness {
    <#
    .SYNOPSIS
        Stage 5: builds one harness from the repository sources against the exact package in a throw-away consumer
        project with an isolated NuGet packages folder, publishes it into its bundle folder and checks its closure.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([System.Collections.Specialized.OrderedDictionary])]
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $RunDirectory,
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Package,
        [Parameter(Mandatory)] [string] $FeedDirectory
    )

    $definition = $Harnesses[$Name]
    $buildDirectory = Join-Path $RunDirectory "build\$Name"
    $bundleDirectory = Join-Path $RunDirectory "bundles\$Name"
    $packagesDirectory = Join-Path $RunDirectory 'nuget-packages'
    if (-not $PSCmdlet.ShouldProcess($bundleDirectory, "build harness $Name from $($Package.id) $($Package.version)")) { return $null }

    $null = New-Item -ItemType Directory -Force -Path $buildDirectory, $bundleDirectory, $packagesDirectory
    foreach ($stub in 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props') {
        Set-Content -LiteralPath (Join-Path $buildDirectory $stub) -Value '<Project />' -Encoding utf8
    }
    Copy-Item -LiteralPath (Join-Path $RepositoryRoot 'global.json') -Destination (Join-Path $buildDirectory 'global.json')

    $compile = foreach ($source in $definition.Sources) {
        $full = Join-Path $RepositoryRoot $source
        $pattern = if (Test-Path -LiteralPath $full -PathType Container) { Join-Path $full '*.cs' } else { $full }
        "    <Compile Include=`"$([System.Security.SecurityElement]::Escape($pattern))`" />"
    }
    $constants = if ($definition.Constants) { "    <DefineConstants>`$(DefineConstants);$($definition.Constants)</DefineConstants>`n" } else { '' }
    $project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>14.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>$($definition.Assembly)</AssemblyName>
    <RootNamespace>$($definition.Namespace)</RootNamespace>
    <Platforms>x64</Platforms>
    <PlatformTarget>x64</PlatformTarget>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <CheatEngineSdkGenerateEntryPoint>$($definition.GenerateEntryPoint)</CheatEngineSdkGenerateEntryPoint>
$constants  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="$($Package.id)" Version="[$($Package.version)]" />
$($compile -join "`n")
  </ItemGroup>
</Project>
"@
    $projectPath = Join-Path $buildDirectory "$($definition.Assembly).csproj"
    Set-Content -LiteralPath $projectPath -Value $project -Encoding utf8
    $packageId = [System.Security.SecurityElement]::Escape($Package.id)
    Set-Content -LiteralPath (Join-Path $buildDirectory 'NuGet.Config') -Encoding utf8 -Value @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="qualified-package" value="$([System.Security.SecurityElement]::Escape($FeedDirectory))" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="qualified-package"><package pattern="$packageId" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
"@

    $previousPackages = $env:NUGET_PACKAGES
    $env:NUGET_PACKAGES = $packagesDirectory
    Push-Location $buildDirectory
    try {
        $sdk = (Invoke-Native -FilePath 'dotnet' -ArgumentList @('--version') | Select-Object -First 1).Trim()
        $pinned = (Get-Content -LiteralPath (Join-Path $RepositoryRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
        if ($sdk -ne $pinned) { throw "The bundle build directory resolves SDK $sdk, global.json pins $pinned." }
        $null = Invoke-Native -FilePath 'dotnet' -ArgumentList @('restore', $projectPath, '--configfile', (Join-Path $buildDirectory 'NuGet.Config'), '--packages', $packagesDirectory, '--no-http-cache', '--force-evaluate', '--nologo')
        $buildOutput = Invoke-Native -FilePath 'dotnet' -ArgumentList @('publish', $projectPath, '-c', 'Release', '--no-restore', '--nologo', '-o', $bundleDirectory)
    }
    finally {
        Pop-Location
        $env:NUGET_PACKAGES = $previousPackages
    }

    $problems = @(Test-QualificationBundleClosure -BundleDirectory $bundleDirectory -PluginFileName "$($definition.Assembly).dll" -PackagedBridgeSha256 $Package.bridgeSha256)
    if ($problems.Count -gt 0) { throw "Bundle $Name is not closed over the package: $($problems -join ' ')" }

    return [ordered]@{
        name        = $Name
        directory   = $bundleDirectory
        plugin      = Join-Path $bundleDirectory "$($definition.Assembly).dll"
        warnings    = @($buildOutput | Where-Object { $_ -match ': warning ' } | Select-Object -Unique)
        contentHash = Get-RestoredPackageContentHash -PackagesDirectory $packagesDirectory -Id $Package.id -Version $Package.version
    }
}

function Join-SharedCoexistenceBundle {
    <#
    .SYNOPSIS
        Q09.a: copies the A and B bundles into one folder and refuses a same-named file with different bytes.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([string])]
    param([Parameter(Mandatory)] [string] $RunDirectory)

    $shared = Join-Path $RunDirectory 'bundles\CoexistenceShared'
    if (-not $PSCmdlet.ShouldProcess($shared, 'merge the Coexistence A and B bundles')) { return $shared }
    $null = New-Item -ItemType Directory -Force -Path $shared
    foreach ($source in 'CoexistenceA', 'CoexistenceB') {
        $sourceDirectory = Join-Path $RunDirectory "bundles\$source"
        foreach ($file in Get-ChildItem -LiteralPath $sourceDirectory -Recurse -File) {
            $relative = [System.IO.Path]::GetRelativePath($sourceDirectory, $file.FullName)
            $destination = Join-Path $shared $relative
            if (Test-Path -LiteralPath $destination) {
                if ((Get-QualificationFileSha256 -Path $destination) -ne (Get-QualificationFileSha256 -Path $file.FullName)) {
                    throw "The shared folder would hold two different '$relative'."
                }
                continue
            }
            $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination)
            Copy-Item -LiteralPath $file.FullName -Destination $destination
        }
    }

    return $shared
}

function Copy-Sandbox {
    <#
    .SYNOPSIS
        Stage 4: mirrors the installation into the sandbox (robocopy /MIR) and compares every file by SHA-256.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    param([Parameter(Mandatory)] [string] $Sandbox)

    if (-not $PSCmdlet.ShouldProcess($Sandbox, "mirror $CheatEnginePath")) { return }
    $null = New-Item -ItemType Directory -Force -Path $Sandbox
    $null = Invoke-Native -FilePath 'robocopy.exe' -ArgumentList @($CheatEnginePath, $Sandbox, '/MIR', '/R:1', '/W:1', '/NFL', '/NDL', '/NP', '/NJH', '/NJS') -FailureThreshold 8
    $problems = [System.Collections.Generic.List[string]]::new()
    $sourceRoot = [System.IO.Path]::GetFullPath($CheatEnginePath)
    foreach ($file in Get-ChildItem -LiteralPath $CheatEnginePath -Recurse -File) {
        $relative = [System.IO.Path]::GetRelativePath($sourceRoot, $file.FullName)
        $copy = Join-Path $Sandbox $relative
        if (-not (Test-Path -LiteralPath $copy -PathType Leaf)) { $problems.Add("missing $relative"); continue }
        if ((Get-QualificationFileSha256 -Path $copy) -ne (Get-QualificationFileSha256 -Path $file.FullName)) { $problems.Add("differs $relative") }
    }

    if ($problems.Count -gt 0) { Exit-Qualification -Code 5 -Reason ('The sandbox is not an exact copy: ' + ($problems -join ', ')) }
}

function Publish-QualificationTarget {
    <#
    .SYNOPSIS
        Stage 7: publishes the Native AOT qualification target for one architecture into the run directory.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([string])]
    param([Parameter(Mandatory)] [string] $RepositoryRoot, [Parameter(Mandatory)] [string] $RunDirectory, [Parameter(Mandatory)] [ValidateSet('x64', 'x86')] [string] $Architecture)

    $output = Join-Path $RunDirectory "target-$Architecture"
    if ($PSCmdlet.ShouldProcess($output, "publish the qualification target for win-$Architecture")) {
        $null = Invoke-Native -FilePath 'dotnet' -ArgumentList @('publish', (Join-Path $RepositoryRoot 'tests/CheatEngine.SDK.QualificationTarget/CheatEngine.SDK.QualificationTarget.csproj'), '-c', 'Release', '-r', "win-$Architecture", '-o', $output, '--nologo')
    }

    return Join-Path $output 'CheatEngine.SDK.QualificationTarget.exe'
}

function Start-QualificationTarget {
    <#
    .SYNOPSIS
        Starts the qualification target and reads its one-line ready record.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([System.Collections.Specialized.OrderedDictionary])]
    param([Parameter(Mandatory)] [string] $Executable)

    if (-not $PSCmdlet.ShouldProcess($Executable, 'start the qualification target')) { return $null }
    $start = [System.Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.WorkingDirectory = Split-Path -Parent $Executable
    $process = [System.Diagnostics.Process]::Start($start)
    $readLine = $process.StandardOutput.ReadLineAsync()
    if (-not $readLine.Wait([System.TimeSpan]::FromSeconds(30))) { throw 'The qualification target printed no ready record within 30 seconds.' }
    $ready = $readLine.Result | ConvertFrom-Json
    return [ordered]@{ process = $process; ready = $ready; sha256 = Get-QualificationFileSha256 -Path $Executable }
}

function Stop-QualificationTarget {
    [CmdletBinding(SupportsShouldProcess)]
    param([AllowNull()] [System.Collections.IDictionary] $Target)

    if ($null -eq $Target -or $Target.process.HasExited) { return }
    if (-not $PSCmdlet.ShouldProcess("target $($Target.process.Id)", 'stop')) { return }
    try {
        $Target.process.StandardInput.WriteLine('exit')
        if (-not $Target.process.WaitForExit(5000)) { $Target.process.Kill() }
    }
    catch [System.InvalidOperationException] {
        Write-Verbose "The target already exited: $($_.Exception.Message)"
    }
}

function Export-CheatEngineRegistry {
    <#
    .SYNOPSIS
        reg export of HKCU\Software\Cheat Engine; returns $false when the key does not exist.
    #>
    [CmdletBinding()]
    [OutputType([bool])]
    param([Parameter(Mandatory)] [string] $Path)

    $null = & reg.exe query $RegistryKey 2>&1
    if ($LASTEXITCODE -ne 0) { return $false }
    $null = Invoke-Native -FilePath 'reg.exe' -ArgumentList @('export', $RegistryKey, $Path, '/y')
    return $true
}

function Restore-CheatEngineRegistry {
    <#
    .SYNOPSIS
        Stage 12: restores the exported key (or removes a key the run created) and verifies the result.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([bool])]
    param([Parameter(Mandatory)] [string] $Before, [Parameter(Mandatory)] [bool] $ExistedBefore, [Parameter(Mandatory)] [string] $Verify)

    if (-not $PSCmdlet.ShouldProcess($RegistryKey, 'restore the exported key')) { return $false }
    $null = & reg.exe delete $RegistryKey /f 2>&1
    if ($ExistedBefore) { $null = Invoke-Native -FilePath 'reg.exe' -ArgumentList @('import', $Before) }
    $existsAfter = Export-CheatEngineRegistry -Path $Verify
    if (-not $ExistedBefore) { return -not $existsAfter }
    if (-not $existsAfter) { return $false }
    $diff = Compare-RegistrySnapshot -Before (Read-RegistryExport -Path $Before) -After (Read-RegistryExport -Path $Verify) -RootKey 'HKEY_CURRENT_USER\Software\Cheat Engine'
    return ($diff.added + $diff.removed + $diff.changed) -eq 0
}

function Read-DriverEvent {
    <#
    .SYNOPSIS
        Reads the event lines the driver appended since the given offset.
    #>
    [CmdletBinding()]
    [OutputType([System.Collections.Specialized.OrderedDictionary])]
    param([Parameter(Mandatory)] [string] $Path, [Parameter(Mandatory)] [long] $Offset)

    $lines = [System.Collections.Generic.List[object]]::new()
    if (-not (Test-Path -LiteralPath $Path)) { return [ordered]@{ offset = $Offset; lines = $lines } }
    $stream = [System.IO.FileStream]::new($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    try {
        $null = $stream.Seek($Offset, [System.IO.SeekOrigin]::Begin)
        $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::UTF8)
        $text = $reader.ReadToEnd()
        $complete = $text.LastIndexOf("`n")
        if ($complete -ge 0) {
            foreach ($line in $text.Substring(0, $complete).Split("`n")) {
                if ($line.Trim()) { $lines.Add(($line.Trim() | ConvertFrom-Json -Depth 64)) }
            }
            $Offset += [System.Text.Encoding]::UTF8.GetByteCount($text.Substring(0, $complete + 1))
        }
    }
    finally {
        $stream.Dispose()
    }

    return [ordered]@{ offset = $Offset; lines = $lines }
}

function Invoke-CheatEngineSession {
    <#
    .SYNOPSIS
        Stages 9-10: launches the sandbox Cheat Engine with the generated driver, serves the operator steps and
        enforces the watchdog. Returns the raw driver events and the session facts.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([System.Collections.Specialized.OrderedDictionary])]
    param(
        [Parameter(Mandatory)] [string] $Sandbox,
        [Parameter(Mandatory)] [string] $SessionDirectory,
        [Parameter(Mandatory)] [object] $Definition,
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Bundles,
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Targets,
        [AllowNull()] [string] $FaultFile
    )

    $eventsPath = Join-Path $SessionDirectory 'driver-events.jsonl'
    $handshakeDirectory = Join-Path $SessionDirectory 'handshake'
    $null = New-Item -ItemType Directory -Force -Path $handshakeDirectory
    $template = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'driver\zz_cesdk_qualification.template.lua') -Raw
    $driver = Expand-QualificationDriver -Template $template -Values ([ordered]@{
            RUN_ID         = Split-Path -Leaf $SessionDirectory
            EVENTS_PATH    = $eventsPath.Replace('\', '/')
            PROGRESS_PATH  = (Join-Path $SessionDirectory 'progress.txt').Replace('\', '/')
            HANDSHAKE_DIR  = $handshakeDirectory.Replace('\', '/')
            DONE_PATH      = (Join-Path $SessionDirectory 'done.txt').Replace('\', '/')
            STEPS          = @($Definition.steps)
            BUNDLES        = $Bundles
            TARGETS        = $Targets
        })
    $driverPath = Join-Path $Sandbox "autorun\$DriverFileName"
    $session = [ordered]@{ events = [System.Collections.Generic.List[object]]::new(); answers = [ordered]@{}; exitCode = $null; killed = $false; ceStartMs = 0; driverSha256 = $null }
    if (-not $PSCmdlet.ShouldProcess($driverPath, 'install the driver and start Cheat Engine')) { return $session }

    [System.IO.File]::WriteAllText($driverPath, $driver, [System.Text.UTF8Encoding]::new($false))
    $session.driverSha256 = Get-QualificationFileSha256 -Path $driverPath
    $start = [System.Diagnostics.ProcessStartInfo]::new((Join-Path $Sandbox 'cheatengine-x86_64.exe'))
    $start.UseShellExecute = $false
    $start.WorkingDirectory = $Sandbox
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $ce = [System.Diagnostics.Process]::Start($start)
    $offset = 0L
    try {
        while (-not $ce.HasExited) {
            if ($clock.Elapsed.TotalSeconds -gt $CeTimeoutSeconds) {
                $ce.Kill($true)
                $session.killed = $true
                break
            }
            $read = Read-DriverEvent -Path $eventsPath -Offset $offset
            $offset = $read.offset
            foreach ($line in $read.lines) {
                $session.events.Add($line)
                if ($line.kind -eq 'AwaitOperator') { Invoke-OperatorStep -Request ($line.message | ConvertFrom-Json) -Definition $Definition -HandshakeDirectory $handshakeDirectory -Answers $session.answers -FaultFile $FaultFile -Events $session.events }
                elseif ($line.kind -eq 'StepResult') { Write-Information "  step $($line.message)" }
            }
            Start-Sleep -Milliseconds 200
        }
        $null = $ce.WaitForExit(15000)
        $session.exitCode = if ($ce.HasExited) { $ce.ExitCode } else { $null }
        foreach ($line in (Read-DriverEvent -Path $eventsPath -Offset $offset).lines) { $session.events.Add($line) }
    }
    finally {
        if (-not $ce.HasExited) { $ce.Kill($true) }
        if (Test-Path -LiteralPath $driverPath) { Remove-Item -LiteralPath $driverPath -Force }
    }

    return $session
}

function Invoke-OperatorStep {
    <#
    .SYNOPSIS
        Shows an operator instruction, records the answer and releases the driver through its handshake file.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [object] $Request,
        [Parameter(Mandatory)] [object] $Definition,
        [Parameter(Mandatory)] [string] $HandshakeDirectory,
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Answers,
        [AllowNull()] [string] $FaultFile,
        [Parameter(Mandatory)] [System.Collections.Generic.List[object]] $Events
    )

    $step = @($Definition.steps)[$Request.step - 1]
    if ((Test-HasProperty -InputObject $step -Name 'removeFaultFile') -and $step.removeFaultFile -and $FaultFile -and (Test-Path -LiteralPath $FaultFile)) {
        Remove-Item -LiteralPath $FaultFile -Force
        $Events.Add([pscustomobject]@{ tMs = -1; source = 'Runner'; kind = 'FaultFileRemoved'; message = 'liveprobe.fault.json removed before the operator step.' })
    }

    Write-Information ''
    Write-Information "OPERATOR STEP $($Request.step) ($($Request.id)): $($Request.instruction)"
    $answer = Read-Host -Prompt 'Your observation (Enter to continue)'
    $Answers[[string] $Request.id] = $answer
    [System.IO.File]::WriteAllText((Join-Path $HandshakeDirectory "step-$($Request.step).txt"), $answer, [System.Text.UTF8Encoding]::new($false))
}

# ---------------------------------------------------------------------------------------------------- main
$RepositoryRoot = Get-RepositoryRoot
if (-not $CheatEnginePath) { $CheatEnginePath = Join-Path $env:ProgramFiles 'Cheat Engine' }
if (-not $WorkRoot) { $WorkRoot = Join-Path $env:LOCALAPPDATA 'CheatEngineNet\qualification' }
$supportProfile = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'docs/qualification/support-profile.json') -Raw | ConvertFrom-Json -Depth 64
$qualifiable = @($supportProfile.profiles | Where-Object { $_.id -eq $ProfileId }) | Select-Object -First 1
$matrix = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'docs/qualification/matrix.json') -Raw | ConvertFrom-Json -Depth 64
$plan = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'scenarios.json') -Raw | ConvertFrom-Json -Depth 64

$workRootProblem = Test-QualificationWorkRoot -WorkRoot $WorkRoot -RepositoryRoot $RepositoryRoot
if ($workRootProblem) { Exit-Qualification -Code 5 -Reason $workRootProblem }

$selected = [System.Collections.Generic.List[object]]::new()
foreach ($id in $Scenario) {
    if ($id -eq 'CheckpointB') {
        foreach ($definition in $plan.scenarios) { if ($definition.support -notin 'Manual', 'NotApplicable') { $selected.Add($definition) } }
        continue
    }
    $definition = @($plan.scenarios | Where-Object { $_.id -ceq $id }) | Select-Object -First 1
    if ($null -eq $definition) { Exit-Qualification -Code 2 -Reason "Unknown scenario '$id'." }
    if ($definition.support -in 'Manual', 'NotApplicable') { Exit-Qualification -Code 2 -Reason "Scenario $id is $($definition.support): $($definition.reason)" }
    $selected.Add($definition)
}

if (-not $PreflightOnly) {
    if (-not $PackagePath -or -not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) { Exit-Qualification -Code 2 -Reason '-PackagePath must name the exact CheatEngine.SDK .nupkg.' }
    if (-not $Smoke) {
        if ($PackageSource -eq 'CiArtifact' -and $CiRunUrl -notmatch '^https://github\.com/CheatEngineNet/CheatEngine\.SDK/actions/runs/\d+(/attempts/\d+)?$') { Exit-Qualification -Code 2 -Reason '-CiRunUrl must name the CI run that produced the artifact.' }
        if ($Operator -notmatch '^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})$') { Exit-Qualification -Code 2 -Reason '-Operator must be the GitHub handle recorded in the receipts.' }
        if (($PullRequest -gt 0) -xor ($HeadSha -match '^[0-9a-f]{40}$')) { Exit-Qualification -Code 2 -Reason '-PullRequest and -HeadSha go together.' }
    }
}

# Stage 2, preflight (read-only).
$facts = Invoke-Preflight -RepositoryRoot $RepositoryRoot -SupportProfile $qualifiable
Write-Information ($facts | ConvertTo-Json -Depth 8)
if ($PreflightOnly) {
    if (-not $facts.matchesProfile) { Exit-Qualification -Code 4 -Reason ('The installation is not the profiled host: ' + ($facts.mismatches -join '; ')) }
    Write-Information 'Preflight only: the installation matches the support profile; nothing was started or written.'
    exit 0
}

Test-PreflightSafety -Facts $facts -AllowOthers:$AllowOtherCheatEngineInstances -AllowLoad:$AllowConcurrentLoad -AllowDirty:$Smoke
if (-not $PSCmdlet.ShouldProcess("Cheat Engine sandbox under $WorkRoot", "run $($selected.Count) scenario(s): $(@($selected | ForEach-Object id) -join ', ')")) { exit 0 }

# Stage 3, mutex.
$createdNew = $false
$mutex = [System.Threading.Mutex]::new($false, 'Global\ce-lab', [ref] $createdNew)
$acquired = $false
try {
    try { $acquired = $mutex.WaitOne([System.TimeSpan]::FromMinutes($MutexTimeoutMinutes)) }
    catch [System.Threading.AbandonedMutexException] { $acquired = $true }
    if (-not $acquired) { Exit-Qualification -Code 5 -Reason "Global\ce-lab was not released within $MutexTimeoutMinutes minutes." }
    Write-Information "Global\ce-lab acquired (created by this run: $createdNew)."

    $runId = [System.DateTimeOffset]::UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", [System.Globalization.CultureInfo]::InvariantCulture)
    $runDirectory = Join-Path $WorkRoot "runs\$runId"
    $sandbox = Join-Path $WorkRoot 'sandbox'
    $null = New-Item -ItemType Directory -Force -Path $runDirectory
    Write-Information "Run $runId in $runDirectory"

    # Stage 4, sandbox.
    Copy-Sandbox -Sandbox $sandbox
    $staleDriver = Join-Path $sandbox "autorun\$DriverFileName"
    if (Test-Path -LiteralPath $staleDriver) { Remove-Item -LiteralPath $staleDriver -Force }
    if (@(Get-Process | Where-Object { $_.Path -and $_.Path.StartsWith($sandbox, [System.StringComparison]::OrdinalIgnoreCase) }).Count -gt 0) { Exit-Qualification -Code 5 -Reason 'A process from the sandbox is already running.' }

    # Stage 5-6, package, bundles, bridge identity.
    $feed = Join-Path $runDirectory 'package\feed'
    $null = New-Item -ItemType Directory -Force -Path $feed
    Copy-Item -LiteralPath $PackagePath -Destination $feed
    $package = Get-PackageIdentity -Path $PackagePath -ExtractTo (Join-Path $runDirectory 'package')
    $fingerprint = Get-BridgeFingerprint -BridgePath $package.bridgePath
    $bundles = [ordered]@{}
    $needed = @($selected | ForEach-Object { @($_.harnesses) } | Select-Object -Unique)
    if ($needed -contains 'CoexistenceShared') { $needed = @($needed | Where-Object { $_ -ne 'CoexistenceShared' }) + @('CoexistenceA', 'CoexistenceB') | Select-Object -Unique }
    foreach ($name in $needed) { $bundles[$name] = Build-Harness -Name $name -RepositoryRoot $RepositoryRoot -RunDirectory $runDirectory -Package $package -FeedDirectory $feed }
    $package.contentHashSha512 = @($bundles.Values | ForEach-Object contentHash | Where-Object { $_ }) | Select-Object -First 1
    if (-not $package.contentHashSha512) { Exit-Qualification -Code 6 -Reason "The isolated restore recorded no NuGet content hash for $($package.id) $($package.version)." }
    $driverBundles = [ordered]@{}
    foreach ($name in $bundles.Keys) { $driverBundles[$name] = $bundles[$name].plugin.Replace('\', '/') }
    if ($selected | Where-Object { @($_.harnesses) -contains 'CoexistenceShared' }) {
        $shared = Join-SharedCoexistenceBundle -RunDirectory $runDirectory
        $driverBundles.CoexistenceSharedA = (Join-Path $shared 'CheatEngine.SDK.LivePlugin.Coexistence.PluginA.dll').Replace('\', '/')
        $driverBundles.CoexistenceSharedB = (Join-Path $shared 'CheatEngine.SDK.LivePlugin.Coexistence.PluginB.dll').Replace('\', '/')
        $bundles.CoexistenceShared = [ordered]@{ name = 'CoexistenceShared'; directory = $shared; warnings = @() }
    }
    foreach ($bundle in $bundles.Values) {
        $manifest = [ordered]@{ schema = 'cheatengine-qualification-bundle/v0'; name = $bundle.name; package = "$($package.id) $($package.version)"; nupkgSha256 = $package.nupkgSha256; warnings = @($bundle.warnings); files = @(Get-BundleFileManifest -BundleDirectory $bundle.directory) }
        $manifestText = ConvertTo-QualificationJson -InputObject $manifest
        [System.IO.File]::WriteAllText((Join-Path $runDirectory "bundle-manifest.$($bundle.name).json"), $manifestText, [System.Text.UTF8Encoding]::new($false))
        $bundle.manifestSha256 = Get-QualificationTextSha256 -Text $manifestText
        $bundle.files = $manifest.files
    }

    $targetExecutables = [ordered]@{}
    foreach ($architecture in @($selected | ForEach-Object target | Where-Object { $_ -in 'x64', 'x86' } | Select-Object -Unique)) {
        $targetExecutables[$architecture] = Publish-QualificationTarget -RepositoryRoot $RepositoryRoot -RunDirectory $runDirectory -Architecture $architecture
    }

    # The builds above leave compiler and MSBuild servers behind; stop them so they do not load the machine during the runs.
    $null = Invoke-Native -FilePath 'dotnet' -ArgumentList @('build-server', 'shutdown')

    $tree = (Invoke-Native -FilePath 'git' -ArgumentList @('-C', $RepositoryRoot, 'rev-parse', 'HEAD^{tree}') | Select-Object -First 1).Trim()
    $commit = (Invoke-Native -FilePath 'git' -ArgumentList @('-C', $RepositoryRoot, 'rev-parse', 'HEAD') | Select-Object -First 1).Trim()
    $scriptSha256 = Get-QualificationTextSha256 -Text ([System.IO.File]::ReadAllText($PSCommandPath))
    $outcomes = [System.Collections.Generic.List[object]]::new()
    $exitCode = 0

    foreach ($definition in $selected) {
        $sessionDirectory = Join-Path $runDirectory "sessions\$($definition.id)"
        $null = New-Item -ItemType Directory -Force -Path $sessionDirectory
        $runnerEvents = [System.Collections.Generic.List[object]]::new()
        $started = [System.DateTimeOffset]::UtcNow
        $sessionClock = [System.Diagnostics.Stopwatch]::StartNew()
        $target = $null
        $manifestPath = $null
        $faultFile = $null
        $registryBefore = Join-Path $sessionDirectory 'hkcu-before.reg'
        $registryAfter = Join-Path $sessionDirectory 'hkcu-after.reg'
        $session = $null
        $registry = $null
        Write-Information "== $($definition.id) ($($definition.level))"
        try {
            # Stage 7, targets and authorization.
            $driverTargets = [ordered]@{}
            if ($definition.target -in 'x64', 'x86') {
                $target = Start-QualificationTarget -Executable $targetExecutables[$definition.target]
                $driverTargets[$definition.target] = [long] $target.ready.pid
                $runnerEvents.Add([pscustomobject]@{ tMs = $sessionClock.ElapsedMilliseconds; source = 'Runner'; kind = 'TargetReady'; message = ($target.ready | ConvertTo-Json -Compress -Depth 8) })
            }
            if (@($definition.harnesses) | Where-Object { $_ -like 'LiveProbe*' }) {
                $manifestPath = Join-Path $sessionDirectory 'live-probe-authorization.json'
                $authorization = [ordered]@{ schema = 'ce77-live-probe-v1'; acknowledgement = $LiveProbeAcknowledgement; hostSha256 = $facts.ceExeSha256.ToUpperInvariant(); targetProcessId = [int] $target.ready.pid; targetSha256 = $target.sha256.ToUpperInvariant(); disposable = $true; expiresUtc = [System.DateTimeOffset]::UtcNow.AddMinutes(30).ToString('o') }
                [System.IO.File]::WriteAllText($manifestPath, ($authorization | ConvertTo-Json), [System.Text.UTF8Encoding]::new($false))
                $env:CE_SDK_LIVE_PROBE_ACKNOWLEDGEMENT = $LiveProbeAcknowledgement
                $env:CE_SDK_LIVE_PROBE_AUTHORIZATION_FILE = $manifestPath
            }
            if ((Test-HasProperty -InputObject $definition -Name 'faultStage') -and $definition.faultStage) {
                $faultFile = Join-Path $bundles[@($definition.harnesses)[0]].directory 'liveprobe.fault.json'
                [System.IO.File]::WriteAllText($faultFile, (@{ schema = 'ce77-live-probe-fault-v1'; throwIn = $definition.faultStage } | ConvertTo-Json -Compress), [System.Text.UTF8Encoding]::new($false))
                $runnerEvents.Add([pscustomobject]@{ tMs = $sessionClock.ElapsedMilliseconds; source = 'Runner'; kind = 'FaultFileWritten'; message = "throwIn $($definition.faultStage)" })
            }

            # Stage 8, HKCU before.
            $existedBefore = Export-CheatEngineRegistry -Path $registryBefore
            if (-not $existedBefore) { [System.IO.File]::WriteAllText($registryBefore, '', [System.Text.Encoding]::Unicode) }

            # Stages 9-10, driver and launch.
            $ceStart = $sessionClock.ElapsedMilliseconds
            $session = Invoke-CheatEngineSession -Sandbox $sandbox -SessionDirectory $sessionDirectory -Definition $definition -Bundles $driverBundles -Targets $driverTargets -FaultFile $faultFile
            $session.ceStartMs = $ceStart
            $runnerEvents.Add([pscustomobject]@{ tMs = $sessionClock.ElapsedMilliseconds; source = 'Runner'; kind = 'CheatEngineExited'; message = "exit code $($session.exitCode); killed by watchdog: $($session.killed)" })
        }
        finally {
            # Stage 11, cleanup.
            Stop-QualificationTarget -Target $target
            foreach ($stray in @(Get-Process | Where-Object { $_.Path -and $_.Path.StartsWith($sandbox, [System.StringComparison]::OrdinalIgnoreCase) })) { Stop-Process -Id $stray.Id -Force }
            foreach ($file in @($manifestPath, $faultFile)) { if ($file -and (Test-Path -LiteralPath $file)) { Remove-Item -LiteralPath $file -Force } }
            Remove-Item Env:CE_SDK_LIVE_PROBE_ACKNOWLEDGEMENT -ErrorAction SilentlyContinue
            Remove-Item Env:CE_SDK_LIVE_PROBE_AUTHORIZATION_FILE -ErrorAction SilentlyContinue

            # Stage 12, HKCU after, compare, restore only a non-empty difference.
            $existsAfter = Export-CheatEngineRegistry -Path $registryAfter
            if (-not $existsAfter) { [System.IO.File]::WriteAllText($registryAfter, '', [System.Text.Encoding]::Unicode) }
            $diff = Compare-RegistrySnapshot -Before (Read-RegistryExport -Path $registryBefore) -After (Read-RegistryExport -Path $registryAfter) -RootKey 'HKEY_CURRENT_USER\Software\Cheat Engine'
            $registry = [ordered]@{ key = $RegistryKey; exportBeforeSha256 = Get-QualificationFileSha256 -Path $registryBefore; exportAfterSha256 = Get-QualificationFileSha256 -Path $registryAfter; restored = $false; diff = $diff }
            if (($diff.added + $diff.removed + $diff.changed) -gt 0) {
                if ((Get-CheatEngineProcess).Count -gt 0) {
                    Exit-Qualification -Code 7 -Reason "HKCU changed ($($diff.valueNames -join ', ')) while another Cheat Engine instance runs; restore it by hand after closing it: reg delete `"$RegistryKey`" /f; reg import `"$registryBefore`""
                }
                $registry.restored = Restore-CheatEngineRegistry -Before $registryBefore -ExistedBefore $existedBefore -Verify (Join-Path $sessionDirectory 'hkcu-restored.reg')
                if (-not $registry.restored) { Exit-Qualification -Code 7 -Reason "HKCU restore could not be verified. Backup: $registryBefore; command: reg delete `"$RegistryKey`" /f; reg import `"$registryBefore`"" }
            }
        }

        # Stage 13, redaction, outcome, receipt.
        $redactionPaths = [ordered]@{ '<sandbox>' = $sandbox; '<workRoot>' = $WorkRoot; '<repo>' = $RepositoryRoot; '<ceInstall>' = $CheatEnginePath }
        foreach ($name in $bundles.Keys) { $redactionPaths["<bundle:$name>"] = $bundles[$name].directory }
        foreach ($architecture in $targetExecutables.Keys) { $redactionPaths["<target:$architecture>"] = Split-Path -Parent $targetExecutables[$architecture] }
        $map = Get-RedactionMap -Paths $redactionPaths
        $raw = @($runnerEvents) + @($session.events)
        $redacted = ConvertTo-RedactedSessionEvent -Raw $raw -Map $map -OffsetMs $session.ceStartMs
        $outcome = Resolve-QualificationOutcome -Scenario $definition -Steps $redacted.steps -Answers $session.answers -PackagedBridgeSha256 $package.bridgeSha256
        if ($session.killed -or $null -eq $session.exitCode) {
            $outcome.status = 'Failed'
            $outcome.passKind = $null
            $outcome.justification = "Cheat Engine did not finish the driver within $CeTimeoutSeconds seconds and was stopped by the watchdog."
            $exitCode = 6
        }
        Write-Information "   $($definition.id): $($outcome.status) $($outcome.passKind) $($outcome.justification)"
        $finished = [System.DateTimeOffset]::UtcNow
        $row = @($matrix.rows | Where-Object { $_.id -ceq $definition.id }) | Select-Object -First 1
        $limited = Limit-QualificationEventLog -Events $redacted.entries
        $outcomes.Add([ordered]@{ id = $definition.id; status = $outcome.status; passKind = $outcome.passKind; events = $redacted.entries.Count; registryDiff = $registry.diff; restored = $registry.restored; ceExitCode = $session.exitCode; killed = $session.killed; unredactedUserPath = [bool] (($limited.events | ConvertTo-Json -Depth 8) -match '(?i)[A-Za-z]:(\\\\|\\|/)Users') })
        if ($Smoke -or $outcome.status -eq 'Inconclusive') { continue }

        $receiptId = Get-QualificationReceiptId -StartedUtc $started -QualificationId $definition.id -NupkgSha256 $package.nupkgSha256
        $eventLog = [ordered]@{ schema = 'cheatengine-qualification-events/v0'; receiptId = $receiptId; events = @($limited.events) }
        if ($null -ne $limited.summarized) { $eventLog.summarized = $limited.summarized }
        $eventText = ConvertTo-QualificationJson -InputObject $eventLog
        $targetRecord = if ($null -ne $target) { [ordered]@{ kind = 'QualificationTarget'; arch = $definition.target; sha256 = $target.sha256 } } else { [ordered]@{ kind = 'None'; arch = $null; sha256 = $null } }
        $usedBundles = foreach ($name in @($definition.harnesses)) { [ordered]@{ name = $name; manifestSha256 = $bundles[$name].manifestSha256; files = @($bundles[$name].files) } }
        $receipt = ConvertTo-QualificationReceipt -Context ([ordered]@{
                QualificationId = $definition.id; Level = $definition.level; ProfileId = $ProfileId; Operator = $Operator; LoadRoute = $definition.loadRoute
                StartedUtc = $started; FinishedUtc = $finished; CreatedUtc = [System.DateTimeOffset]::UtcNow; CeStartMs = $session.ceStartMs; Indicative = [bool] $AllowConcurrentLoad
                Repository = [ordered]@{ name = 'CheatEngineNet/CheatEngine.SDK'; treeHash = $tree; commit = $commit; pullRequest = $(if ($PullRequest -gt 0) { [ordered]@{ number = $PullRequest; headSha = $HeadSha } } else { $null }) }
                Runner = [ordered]@{ script = 'eng/qualification/Invoke-LocalQualification.ps1'; scriptSha256 = $scriptSha256; sourceRepository = 'CheatEngineNet/CheatEngine.SDK'; sourceCommit = $commit; mutex = 'Global\ce-lab' }
                Package = [ordered]@{ id = $package.id; version = $package.version; nupkgSha256 = $package.nupkgSha256; contentHashSha512 = $package.contentHashSha512; source = $PackageSource; ciRunUrl = $(if ($PackageSource -eq 'CiArtifact') { $CiRunUrl } else { $null }) }
                Host = [ordered]@{ ceExeName = $facts.ceExeName; ceExeSha256 = Get-QualificationFileSha256 -Path (Join-Path $sandbox $facts.ceExeName); ceFileVersion = $facts.ceFileVersion; luaDllSha256 = $facts.luaDllSha256; runtimeconfigSha256 = $facts.runtimeconfigSha256; autorunSha256 = $session.driverSha256; sandboxCopy = $true; osVersion = $facts.osVersion; dotnetRuntimes = @($facts.dotnetRuntimes) }
                Bridge = [ordered]@{ sha256 = $package.bridgeSha256; sourceFingerprint = $fingerprint }
                Bundles = @($usedBundles); Target = $targetRecord; Registry = $registry
                Preconditions = @($row.scenario.preconditions); Operation = $row.scenario.operation; Expected = $row.scenario.expected
                Outcome = $outcome; EventLogSha256 = Get-QualificationTextSha256 -Text $eventText; Redactions = @(@($redactionPaths.Keys) + @('<user>', '<machine>', '<userProfile>'))
            })
        $receiptDirectory = Join-Path $runDirectory "receipts\$($definition.id)"
        $null = New-Item -ItemType Directory -Force -Path $receiptDirectory
        [System.IO.File]::WriteAllText((Join-Path $receiptDirectory "$receiptId.json"), (ConvertTo-QualificationJson -InputObject $receipt), [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText((Join-Path $receiptDirectory "$receiptId.events.json"), $eventText, [System.Text.UTF8Encoding]::new($false))
        Write-Information "   receipt $receiptId"
    }

    if ($Smoke) {
        $report = [ordered]@{ schema = 'cheatengine-qualification-smoke/v0'; runId = $runId; package = "$($package.id) $($package.version)"; nupkgSha256 = $package.nupkgSha256; bridgeFingerprint = $fingerprint; scenarios = @($outcomes) }
        [System.IO.File]::WriteAllText((Join-Path $runDirectory 'smoke-report.json'), (ConvertTo-QualificationJson -InputObject $report), [System.Text.UTF8Encoding]::new($false))
        Write-Information "Smoke report: $(Join-Path $runDirectory 'smoke-report.json') (never a receipt)."
    }
    elseif ($PublishReceiptsTo -and $PSCmdlet.ShouldProcess($PublishReceiptsTo, 'copy the receipts into docs/qualification/receipts')) {
        foreach ($receiptFile in Get-ChildItem -LiteralPath (Join-Path $runDirectory 'receipts') -Recurse -File -Filter '*.json' -ErrorAction SilentlyContinue) {
            $destination = Join-Path $PublishReceiptsTo "docs/qualification/receipts/$($receiptFile.Directory.Name)"
            $null = New-Item -ItemType Directory -Force -Path $destination
            Copy-Item -LiteralPath $receiptFile.FullName -Destination $destination
            if ($receiptFile.Name -notlike '*.events.json') {
                $published = Get-Content -LiteralPath $receiptFile.FullName -Raw | ConvertFrom-Json
                Write-Information "Update matrix.json $($published.qualificationId) $($published.level): status $($published.status), evidence Receipt $($published.receiptId) path docs/qualification/receipts/$($published.qualificationId)/$($receiptFile.Name) sha256 $(Get-QualificationTextSha256 -Text (Get-Content -LiteralPath $receiptFile.FullName -Raw)), treeHash $tree, nupkgSha256 $($package.nupkgSha256)."
            }
        }
    }

    Write-Information ($outcomes | ConvertTo-Json -Depth 6)
    exit $exitCode
}
finally {
    if ($acquired) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
