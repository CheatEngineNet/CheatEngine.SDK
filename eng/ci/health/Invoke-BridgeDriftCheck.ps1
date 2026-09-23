#Requires -Version 7.0
<#
.SYNOPSIS
    Rebuilds the native protection bridge of the latest release tag with today's toolchain and compares it with the
    DLL that release shipped (audit register A21-25).

.DESCRIPTION
    A release is trusted because its artifacts can be rebuilt from the tagged sources (audit ch.21, "exit criteria"). The
    bridge is the one native binary of the package, so this check rebuilds it on a schedule and tells whether today's
    toolchain still produces the released bytes:

    1. Select the tag: -Tag, or the newest v<major>.<minor>.<patch> tag reachable from origin/main (else main) whose tree
       contains native/cheatengine-sdk-lua-bridge/xmake.lua (the v0.x tags of the former CESDK package have no bridge).
    2. Extract the two build inputs of that tag, cheatengine_sdk_lua_bridge.c and xmake.lua, as raw committed bytes (LF,
       their SHA-256 is the source fingerprint), into two directories of different depth under a temporary folder
       outside the checkout. Nothing is ever written inside the checkout except the report under -OutputDirectory.
    3. Build each copy with xmake (Windows x64 release, --ccache=n so a cache hit cannot fake reproducibility, plus
       -XmakeConfigArgument: the MSVC toolset and Windows SDK pins of the ci.yml native job). Every xmake path is
       relative to the current directory: xmake 3.0.9 mis-parses an absolute Windows -o path.
    4. Compare the two builds (path independence), read the exported cheatengine_sdk_lua_bridge_source_fingerprint of
       the rebuild, and hash the DLL committed at the tag and build/native/cheatengine-sdk-lua-bridge.dll inside the
       released nupkg on nuget.org.
    5. Classify with Get-BridgeDriftClassification (HealthCheck.psm1): Reproduced, ToolchainDrift (a ::warning::, not a
       failure) or Failed (throws). Write bridge-drift.json, the rebuilt DLL and the job summary to -OutputDirectory, and
       drift=true|false to GITHUB_OUTPUT.

    bridge-drift.json is a report, not a contract document.

.PARAMETER Tag
    The release tag to rebuild (for example v1.0.0). Defaults to the newest release tag that ships the bridge.

.PARAMETER XmakePath
    The xmake executable. Defaults to xmake on PATH.

.PARAMETER XmakeConfigArgument
    Extra `xmake f` arguments, for example --vs_toolset=14.44 and --vs_sdkver=10.0.26100.0.

.PARAMETER OutputDirectory
    Where bridge-drift.json, summary.md and the rebuilt DLL are written.

.EXAMPLE
    ./eng/ci/health/Invoke-BridgeDriftCheck.ps1 -Tag v1.0.0 -OutputDirectory "$env:TEMP/drift"
#>
[CmdletBinding()]
param(
    [string] $Tag,
    [string] $XmakePath = 'xmake',
    [string[]] $XmakeConfigArgument = @(),
    [string] $OutputDirectory = 'artifacts/health/bridge-drift'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module -Name (Join-Path -Path $PSScriptRoot -ChildPath 'HealthCheck.psm1') -Force
Import-Module -Name (Join-Path -Path $PSScriptRoot -ChildPath 'HealthCommand.psm1') -Force

if (-not $IsWindows) {
    throw 'The bridge is a Windows x64 DLL built with MSVC: run the drift check on Windows.'
}

Add-Type -AssemblyName System.IO.Compression.ZipFile

$bridgeDirectory = 'native/cheatengine-sdk-lua-bridge'
$projectName = 'cheatengine-sdk-lua-bridge'
$bridgeFile = 'cheatengine-sdk-lua-bridge.dll'
$buildInputs = @('cheatengine_sdk_lua_bridge.c', 'xmake.lua')
$checkedInPath = "$bridgeDirectory/runtimes/win-x64/native/$bridgeFile"
$packageEntry = "build/native/$bridgeFile"
$fingerprintExport = 'cheatengine_sdk_lua_bridge_source_fingerprint'

$root = Get-HealthRepositoryRoot
$output = New-HealthOutputDirectory -Path $OutputDirectory

function Test-GitObject {
    param(
        [Parameter(Mandatory)] [string] $Object
    )

    # A query whose non-zero exit code is an answer (the object does not exist), not a failure.
    & git -C $root cat-file -e $Object 2>$null
    return $LASTEXITCODE -eq 0
}

function Get-Sha256 {
    param(
        [Parameter(Mandatory)] [string] $Path
    )

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-ExportedFingerprint {
    param(
        [Parameter(Mandatory)] [string] $Path
    )

    $module = [System.Runtime.InteropServices.NativeLibrary]::Load($Path)
    try {
        $address = [System.Runtime.InteropServices.NativeLibrary]::GetExport($module, $fingerprintExport)
        return [System.Runtime.InteropServices.Marshal]::PtrToStringAnsi($address)
    }
    finally {
        [System.Runtime.InteropServices.NativeLibrary]::Free($module)
    }
}

# The toolchain xmake resolved, read from its toolchain cache (format internal to the pinned xmake 3.0.9), best effort:
# the classification depends on bytes, the toolchain facts only explain drift.
function Get-ResolvedToolchain {
    param(
        [Parameter(Mandatory)] [string] $BuildDirectory
    )

    $facts = [ordered]@{ msvcToolset = $null; windowsSdk = $null; clVersion = $null }
    $cache = @(
        (Join-Path -Path $BuildDirectory -ChildPath '.xmake/windows/x64/cache/toolchain'),
        (Join-Path -Path $BuildDirectory -ChildPath "$projectName/.xmake/windows/x64/cache/toolchain")
    ) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if (-not $cache) {
        Write-Host '::notice title=Bridge drift::xmake wrote no toolchain cache; the MSVC and Windows SDK versions are not recorded.'
        return $facts
    }

    $text = Get-Content -Raw -LiteralPath $cache
    $read = {
        param([string] $name)
        $values = @([regex]::Matches($text, "\b$name\s*=\s*(?:`"(?<value>[^`"]+)`"|\[\[(?<value>[^\]]+)\]\])") |
                ForEach-Object { $_.Groups['value'].Value.Trim().TrimEnd('\') } | Sort-Object -Unique)
        if ($values.Count -eq 1) { return $values[0] }
        return $null
    }

    $facts.msvcToolset = & $read 'VCToolsVersion'
    $facts.windowsSdk = & $read 'WindowsSDKVersion'
    $toolsDirectory = & $read 'VCToolsInstallDir'
    $compiler = if ($toolsDirectory) { Join-Path -Path $toolsDirectory -ChildPath 'bin/HostX64/x64/cl.exe' } else { $null }
    if ($compiler -and (Test-Path -LiteralPath $compiler -PathType Leaf)) {
        # Without arguments cl.exe prints its banner and usage and exits 0; VSLANG=1033 keeps the banner in English.
        $previousLanguage = $env:VSLANG
        $env:VSLANG = '1033'
        try {
            $banner = @(& $compiler 2>&1 | ForEach-Object { "$_" })
            if ($LASTEXITCODE -ne 0) {
                throw "cl.exe exited with code $LASTEXITCODE while printing its banner."
            }
        }
        finally {
            $env:VSLANG = $previousLanguage
        }

        foreach ($line in $banner) {
            if ($line -match 'C/C\+\+.*?\b(?<version>\d+\.\d+\.\d+(?:\.\d+)?)\b') {
                $facts.clVersion = $Matches['version']
                break
            }
        }
    }

    return $facts
}

function Invoke-BridgeBuild {
    param(
        [Parameter(Mandatory)] [string] $BuildDirectory,
        [Parameter(Mandatory)] [string] $Label
    )

    Push-Location -LiteralPath $BuildDirectory
    try {
        Invoke-NativeCommand -FilePath $XmakePath -Description "xmake configuration of the $Label rebuild" -ArgumentList (@(
                'f', '-P', $projectName, '-o', 'out', '-p', 'windows', '-a', 'x64', '-m', 'release', '-y', '--ccache=n') + $XmakeConfigArgument)
        Invoke-NativeCommand -FilePath $XmakePath -ArgumentList @('-P', $projectName, '-y') -Description "xmake build of the $Label rebuild"
    }
    finally {
        Pop-Location
    }

    $dll = Join-Path -Path $BuildDirectory -ChildPath "out/$bridgeFile"
    if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) {
        throw "xmake did not produce '$dll' for the $Label rebuild."
    }

    return $dll
}

# 1. The tag.
if (-not $Tag) {
    $base = $null
    foreach ($candidate in @('refs/remotes/origin/main', 'refs/heads/main')) {
        & git -C $root rev-parse --verify --quiet $candidate *> $null
        if ($LASTEXITCODE -eq 0) {
            $base = $candidate
            break
        }
    }

    if (-not $base) {
        throw 'Neither origin/main nor main exists: check out with fetch-depth: 0, or pass -Tag.'
    }

    $candidates = @(Get-GitOutput -ArgumentList @('tag', '--merged', $base, '--list', 'v*') |
            Where-Object { Test-GitObject -Object "refs/tags/${_}:$bridgeDirectory/xmake.lua" })
    $Tag = Select-ReleaseTag -Tag $candidates
    if (-not $Tag) {
        throw "No release tag reachable from $base contains $bridgeDirectory/xmake.lua."
    }
}

$tagMatch = [regex]::Match($Tag, '^v(?<version>\d+\.\d+\.\d+)$')
if (-not $tagMatch.Success) {
    throw "'$Tag' is not a release tag of the form v<major>.<minor>.<patch>."
}

$packageVersion = $tagMatch.Groups['version'].Value
$commit = @(Get-GitOutput -ArgumentList @('rev-parse', "refs/tags/$Tag^{commit}"))[0]
$runner = [ordered]@{
    imageOs      = if ($env:ImageOS) { $env:ImageOS } else { 'local' }
    imageVersion = if ($env:ImageVersion) { $env:ImageVersion } else { 'local' }
}
$xmakeVersion = $null
$xmakeBanner = @(Get-NativeCommandOutput -FilePath $XmakePath -ArgumentList @('--version') -Description 'xmake --version' |
        ForEach-Object { $_ -replace '\x1b\[[0-9;]*m', '' })
foreach ($line in $xmakeBanner) {
    if ($line -match '\bxmake v(?<version>\d+\.\d+\.\d+)') {
        $xmakeVersion = $Matches['version']
        break
    }
}

# 2-4. Rebuild twice outside the checkout, then measure.
$temporaryRoot = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [System.IO.Path]::GetTempPath() }
$work = [System.IO.Path]::GetFullPath((Join-Path -Path $temporaryRoot -ChildPath "bridge-drift-$([guid]::NewGuid().ToString('N'))"))
if ("$work$([System.IO.Path]::DirectorySeparatorChar)".StartsWith("$root$([System.IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase)) {
    throw "The work directory '$work' must be outside the checkout '$root'."
}

$report = [ordered]@{
    tag               = $Tag
    commit            = $commit
    packageVersion    = $packageVersion
    sourceFingerprint = [ordered]@{ expected = $null; exported = $null }
    sha256            = [ordered]@{ rebuilt = $null; rebuiltSecondDirectory = $null; committedAtTag = $null; releasedPackage = $null }
    toolchain         = [ordered]@{ xmake = $xmakeVersion; xmakeConfigArguments = @($XmakeConfigArgument); msvcToolset = $null; windowsSdk = $null; clVersion = $null }
    runner            = $runner
    classification    = $null
    reasons           = @()
    createdUtc        = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ', [System.Globalization.CultureInfo]::InvariantCulture)
}
$buildError = $null
try {
    $buildDirectories = @((Join-Path -Path $work -ChildPath 'a'), (Join-Path -Path $work -ChildPath 'path-check/deeper'))
    $inputHashes = @()
    foreach ($buildDirectory in $buildDirectories) {
        $project = Join-Path -Path $buildDirectory -ChildPath $projectName
        New-Item -ItemType Directory -Force -Path $project | Out-Null
        foreach ($inputFile in $buildInputs) {
            Save-GitBlob -Object "refs/tags/${Tag}:$bridgeDirectory/$inputFile" -Destination (Join-Path -Path $project -ChildPath $inputFile)
        }
    }

    foreach ($inputFile in $buildInputs) {
        $inputHashes += Get-Sha256 -Path (Join-Path -Path $buildDirectories[0] -ChildPath "$projectName/$inputFile")
    }

    $report.sourceFingerprint.expected = $inputHashes -join ':'
    $committedAtTag = Join-Path -Path $work -ChildPath "committed-$bridgeFile"
    Save-GitBlob -Object "refs/tags/${Tag}:$checkedInPath" -Destination $committedAtTag
    $report.sha256.committedAtTag = Get-Sha256 -Path $committedAtTag

    $package = Join-Path -Path $work -ChildPath "cheatengine.sdk.$packageVersion.nupkg"
    $packageUrl = "https://api.nuget.org/v3-flatcontainer/cheatengine.sdk/$packageVersion/cheatengine.sdk.$packageVersion.nupkg"
    Write-Host "Downloading $packageUrl."
    $response = Invoke-WebRequest -Uri $packageUrl -OutFile $package -PassThru -SkipHttpErrorCheck -MaximumRetryCount 3 -RetryIntervalSec 5 -UseBasicParsing
    if ($response.StatusCode -eq 200) {
        $archive = [System.IO.Compression.ZipFile]::OpenRead($package)
        try {
            $entry = @($archive.Entries | Where-Object { $_.FullName -ceq $packageEntry }) | Select-Object -First 1
            if ($entry) {
                $stream = $entry.Open()
                try {
                    $report.sha256.releasedPackage = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant()
                }
                finally {
                    $stream.Dispose()
                }
            }
            else {
                Write-Host "::warning title=Bridge drift::CheatEngine.SDK $packageVersion has no $packageEntry."
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    elseif ($response.StatusCode -eq 404) {
        Write-Host "::warning title=Bridge drift::CheatEngine.SDK $packageVersion is not on nuget.org ($packageUrl returned 404)."
    }
    else {
        throw "Downloading $packageUrl returned HTTP $($response.StatusCode)."
    }

    $first = Invoke-BridgeBuild -BuildDirectory $buildDirectories[0] -Label 'first'
    $toolchain = Get-ResolvedToolchain -BuildDirectory $buildDirectories[0]
    foreach ($name in @($toolchain.Keys)) {
        $report.toolchain[$name] = $toolchain[$name]
    }

    $second = Invoke-BridgeBuild -BuildDirectory $buildDirectories[1] -Label 'second (other directory)'
    $report.sha256.rebuilt = Get-Sha256 -Path $first
    $report.sha256.rebuiltSecondDirectory = Get-Sha256 -Path $second
    $report.sourceFingerprint.exported = Get-ExportedFingerprint -Path $first
    Copy-Item -LiteralPath $first -Destination (Join-Path -Path $output -ChildPath $bridgeFile) -Force
}
catch {
    $buildError = $_.Exception.Message
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

# 5. Classify and report.
if ($buildError) {
    $report.classification = 'Failed'
    $report.reasons = @("The rebuild could not complete: $buildError")
}
else {
    $verdict = Get-BridgeDriftClassification -RebuiltSha256 $report.sha256.rebuilt -SecondRebuiltSha256 $report.sha256.rebuiltSecondDirectory `
        -ExpectedFingerprint $report.sourceFingerprint.expected -ExportedFingerprint $report.sourceFingerprint.exported `
        -CheckedInSha256 $report.sha256.committedAtTag -ReleasedSha256 $report.sha256.releasedPackage
    $report.classification = $verdict.Classification
    $report.reasons = @($verdict.Reasons)
}

[System.IO.File]::WriteAllText((Join-Path -Path $output -ChildPath 'bridge-drift.json'), ($report | ConvertTo-Json -Depth 5),
    [System.Text.UTF8Encoding]::new($false))

$cell = { param($value) if ($null -eq $value -or "$value" -eq '') { '(not available)' } else { "``$(ConvertTo-HealthTableCell -Text ([string] $value))``" } }
$summary = @(
    '## Release bridge rebuild',
    '',
    '| Fact | Value |',
    '| --- | --- |',
    "| Tag | $(& $cell $Tag) at $(& $cell $commit) |",
    "| Source fingerprint of the tag | $(& $cell $report.sourceFingerprint.expected) |",
    "| Fingerprint exported by the rebuild | $(& $cell $report.sourceFingerprint.exported) |",
    "| Rebuilt SHA-256 | $(& $cell $report.sha256.rebuilt) |",
    "| Rebuilt SHA-256, second directory | $(& $cell $report.sha256.rebuiltSecondDirectory) |",
    "| Committed at the tag | $(& $cell $report.sha256.committedAtTag) |",
    "| Released package (nuget.org) | $(& $cell $report.sha256.releasedPackage) |",
    "| xmake | $(& $cell $report.toolchain.xmake) |",
    "| xmake configuration arguments | $(& $cell ($XmakeConfigArgument -join ' ')) |",
    "| MSVC toolset / cl.exe | $(& $cell $report.toolchain.msvcToolset) / $(& $cell $report.toolchain.clVersion) |",
    "| Windows SDK | $(& $cell $report.toolchain.windowsSdk) |",
    "| Runner image | $(& $cell "$($runner.imageOs) $($runner.imageVersion)") |",
    "| Classification | **$($report.classification)** |",
    ''
)
$summary += @($report.reasons | ForEach-Object { "- $(ConvertTo-HealthTableCell -Text $_)" })
Write-HealthSummary -Line $summary
[System.IO.File]::WriteAllLines((Join-Path -Path $output -ChildPath 'summary.md'), [string[]] $summary, [System.Text.UTF8Encoding]::new($false))
Write-HealthOutput -Value ([ordered]@{ drift = $(if ($report.classification -ceq 'Reproduced') { 'false' } else { 'true' }) })

switch ($report.classification) {
    'ToolchainDrift' {
        Write-Host "::warning title=Bridge drift::Today's toolchain does not rebuild the $Tag bridge byte for byte (sources unchanged). See the health-bridge-drift artifact."
    }
    'Failed' {
        throw "The $Tag bridge check failed: $($report.reasons -join ' ')"
    }
}
