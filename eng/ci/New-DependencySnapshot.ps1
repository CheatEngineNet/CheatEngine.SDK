#Requires -Version 7.0
<#
.SYNOPSIS
    Detects the NuGet dependency graph of the locked restore with a pinned Component Detection binary and writes a
    GitHub dependency snapshot. It never submits it.

.DESCRIPTION
    GitHub's dependency graph cannot read the Central Package Management layout (it lists NuGet packages as ">= 0"), and
    automatic dependency submission needs an organization-level setting. The "Dependency submission" workflow therefore
    submits a snapshot built here (https://docs.github.com/en/rest/dependency-graph/dependency-submission):

    1. Download component-detection-win-x64.exe of microsoft/component-detection at a pinned release into a temporary
       folder outside the repository and verify its SHA-256 before running it. The official submission action is not
       used: it downloads the latest release at run time and runs it next to a contents: write token.
    2. Restore CheatEngine.SDK.slnx, then every project outside it (enumerated from git, like eng/Update-LockFiles.ps1),
       with --locked-mode and one binary log each under artifacts/logs. The default-on MSBuildBinaryLog detector reads
       project.assets.json (under artifacts/obj because of ArtifactsPath) and uses the binary logs to mark the packages
       of test projects (IsTestProject) as development dependencies. Binary logs can hold environment variables: they
       stay on the machine and are never uploaded.
    3. Scan the repository with the NuGet detectors.
    4. Convert the scan manifest into the snapshot body: one manifest per project file (repository-relative, forward
       slashes), "direct" for the packages the project references explicitly, "development" scope for the detector's
       development dependencies, and the dependency edges of each project graph. Non-NuGet components are skipped.
    5. Sanity gate: at least $MinimumPackageCount distinct packages, and Microsoft.CodeAnalysis.CSharp at the version
       Directory.Packages.props pins (the Roslyn floor every analyzer and generator builds against).

.PARAMETER OutputPath
    Where the snapshot JSON is written (relative to the repository root, or absolute).

.PARAMETER Sha
    The commit the snapshot describes (40 or 64 lowercase hex). Defaults to $env:SNAPSHOT_SHA, else the local HEAD.

.PARAMETER Ref
    The Git ref of the snapshot (refs/...). Defaults to $env:SNAPSHOT_REF, else the local symbolic HEAD.

.PARAMETER Correlator
    Groups the snapshots of this detection over time. Defaults to $env:SNAPSHOT_CORRELATOR, else 'local-nuget'.

.PARAMETER JobId
    Run identifier. Defaults to $env:SNAPSHOT_JOB_ID, else 'local'.

.PARAMETER JobUrl
    Run URL (optional). Defaults to $env:SNAPSHOT_JOB_URL.

.EXAMPLE
    ./eng/ci/New-DependencySnapshot.ps1 -OutputPath "$env:TEMP/snapshot.json"

    Builds a snapshot of the current checkout. Nothing is sent to GitHub.
#>
[CmdletBinding()]
param(
    [string] $OutputPath = 'artifacts/dependency-snapshot/snapshot.json',
    [string] $Sha = $env:SNAPSHOT_SHA,
    [string] $Ref = $env:SNAPSHOT_REF,
    [string] $Correlator = $env:SNAPSHOT_CORRELATOR,
    [string] $JobId = $env:SNAPSHOT_JOB_ID,
    [string] $JobUrl = $env:SNAPSHOT_JOB_URL
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Pinned detector. Bump the three values together after reading the release and its asset digest:
#   gh api repos/microsoft/component-detection/releases/tags/v<version> --jq '.assets[] | select(.name == "component-detection-win-x64.exe") | .digest'
$DetectorVersion = '8.0.1'
$DetectorAsset = 'component-detection-win-x64.exe'
$DetectorSha256 = '9539f792cd2ae7d719922db45df763ec4454cc380fbfcbe685da9a90c1b39cf2'
$DetectorUrl = "https://github.com/microsoft/component-detection/releases/download/v$DetectorVersion/$DetectorAsset"
$MinimumPackageCount = 40
$SentinelPackage = 'Microsoft.CodeAnalysis.CSharp'

$RepositoryRoot = Split-Path -Path $PSScriptRoot -Parent | Split-Path -Parent
$SolutionFile = 'CheatEngine.SDK.slnx'
$InvariantCulture = [System.Globalization.CultureInfo]::InvariantCulture

function Invoke-Native {
    param(
        [Parameter(Mandatory)] [string] $FilePath,
        [Parameter(Mandatory)] [string[]] $ArgumentList,
        [Parameter(Mandatory)] [string] $Description
    )

    Write-Host "> $([System.IO.Path]::GetFileName($FilePath)) $($ArgumentList -join ' ')"
    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Get-GitOutput {
    param(
        [Parameter(Mandatory)] [string[]] $ArgumentList
    )

    $output = & git -C $RepositoryRoot @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "git $($ArgumentList -join ' ') failed with exit code $LASTEXITCODE."
    }

    return @($output | Where-Object { $_ })
}

function Get-OutOfSolutionProject {
    [xml] $solution = Get-Content -Raw -LiteralPath (Join-Path -Path $RepositoryRoot -ChildPath $SolutionFile)
    $inSolution = @($solution.SelectNodes('//Project') | ForEach-Object { $_.GetAttribute('Path').Replace('\', '/') })
    return @(Get-GitOutput -ArgumentList @('ls-files', '--', '*.csproj') |
            Where-Object { $_ -notin $inSolution } |
            Sort-Object)
}

function Get-PinnedVersion {
    param(
        [Parameter(Mandatory)] [string] $PackageId
    )

    [xml] $packages = Get-Content -Raw -LiteralPath (Join-Path -Path $RepositoryRoot -ChildPath 'Directory.Packages.props')
    foreach ($version in $packages.SelectNodes('//PackageVersion')) {
        if ($version.GetAttribute('Include') -ceq $PackageId) {
            return $version.GetAttribute('Version')
        }
    }

    throw "Directory.Packages.props pins no version of $PackageId."
}

function ConvertTo-OrdinalSet {
    param(
        [AllowNull()] [object] $Item
    )

    $values = [string[]] @($Item | Where-Object { $null -ne $_ })
    $set = [System.Collections.Generic.HashSet[string]]::new($values, [System.StringComparer]::Ordinal)
    # The unary comma keeps the set whole: PowerShell would otherwise enumerate it (an empty set would become $null).
    return , $set
}

function Get-PackageUrl {
    param(
        [Parameter(Mandatory)] [object] $Component
    )

    # Package URL spec: the NuGet type keeps the name's case; '@' in a name must be percent-encoded.
    return "pkg:nuget/$($Component.Name.Replace('@', '%40'))@$($Component.Version)"
}

function ConvertTo-RepositoryPath {
    param(
        [Parameter(Mandatory)] [string] $Location
    )

    $absolute = if ([System.IO.Path]::IsPathRooted($Location)) { $Location } else { Join-Path -Path $RepositoryRoot -ChildPath $Location }
    $relative = [System.IO.Path]::GetRelativePath($RepositoryRoot, $absolute).Replace('\', '/')
    if ($relative.StartsWith('../', [StringComparison]::Ordinal) -or [System.IO.Path]::IsPathRooted($relative)) {
        return $null
    }

    return $relative
}

if (-not $Sha) { $Sha = @(Get-GitOutput -ArgumentList @('rev-parse', 'HEAD'))[0] }
if (-not $Ref) { $Ref = @(Get-GitOutput -ArgumentList @('symbolic-ref', '--quiet', 'HEAD'))[0] }
if (-not $Correlator) { $Correlator = 'local-nuget' }
if (-not $JobId) { $JobId = 'local' }
if (-not [regex]::IsMatch([string] $Sha, '^[0-9a-f]{40}([0-9a-f]{24})?$')) {
    throw "The snapshot commit must be a full lowercase commit id; got '$Sha'."
}

if (-not ([string] $Ref).StartsWith('refs/', [StringComparison]::Ordinal)) {
    throw "The snapshot ref must start with refs/; got '$Ref'."
}

$work = Join-Path -Path ([System.IO.Path]::GetTempPath()) -ChildPath "cheatengine-sdk-snapshot-$([guid]::NewGuid().ToString('N'))"
$detectorLogs = Join-Path -Path $work -ChildPath 'logs'
New-Item -ItemType Directory -Path $detectorLogs | Out-Null
try {
    $detector = Join-Path -Path $work -ChildPath $DetectorAsset
    $scanManifest = Join-Path -Path $work -ChildPath 'component-detection-manifest.json'

    Write-Host "Downloading Component Detection v$DetectorVersion."
    Invoke-WebRequest -Uri $DetectorUrl -OutFile $detector -MaximumRetryCount 3 -RetryIntervalSec 5
    $actualSha256 = (Get-FileHash -LiteralPath $detector -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualSha256 -cne $DetectorSha256) {
        throw "$DetectorAsset v$DetectorVersion has SHA-256 $actualSha256, expected $DetectorSha256. Refusing to run it."
    }

    $logDirectory = Join-Path -Path $RepositoryRoot -ChildPath 'artifacts/logs'
    New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
    $targets = @($SolutionFile) + @(Get-OutOfSolutionProject)
    Push-Location -LiteralPath $RepositoryRoot
    try {
        foreach ($target in $targets) {
            $binaryLog = Join-Path -Path $logDirectory -ChildPath "dependency-restore-$([System.IO.Path]::GetFileNameWithoutExtension($target)).binlog"
            Invoke-Native -FilePath 'dotnet' -ArgumentList @('restore', $target, '--locked-mode', "-bl:$binaryLog") `
                -Description "Locked restore of $target"
        }
    }
    finally {
        Pop-Location
    }

    Invoke-Native -FilePath $detector -ArgumentList @(
        'scan', '--SourceDirectory', $RepositoryRoot, '--ManifestFile', $scanManifest,
        '--DetectorCategories', 'NuGet', '--Output', $detectorLogs, '--LogLevel', 'Warning'
    ) -Description 'Component Detection'

    $scan = Get-Content -LiteralPath $scanManifest -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable
    $components = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)
    foreach ($found in @($scan['componentsFound'])) {
        $component = $found['component']
        if ($component['type'] -ceq 'NuGet') {
            $components[[string] $component['id']] = [pscustomobject]@{
                Name    = [string] $component['name']
                Version = [string] $component['version']
            }
        }
    }

    $manifests = [ordered]@{}
    foreach ($location in @($scan['dependencyGraphs'].Keys | Sort-Object)) {
        $relative = ConvertTo-RepositoryPath -Location $location
        if ($null -eq $relative) {
            continue
        }

        $graph = $scan['dependencyGraphs'][$location]
        $explicit = ConvertTo-OrdinalSet -Item $graph['explicitlyReferencedComponentIds']
        $development = ConvertTo-OrdinalSet -Item $graph['developmentDependencies']
        $resolved = [ordered]@{}
        foreach ($id in @($graph['graph'].Keys | Sort-Object)) {
            if (-not $components.ContainsKey($id)) {
                continue
            }

            $children = @(@($graph['graph'][$id]) |
                    Where-Object { $_ -and $components.ContainsKey($_) } |
                    ForEach-Object { Get-PackageUrl -Component $components[$_] } |
                    Sort-Object -Unique)
            $packageUrl = Get-PackageUrl -Component $components[$id]
            $resolved[$packageUrl] = [ordered]@{
                package_url  = $packageUrl
                relationship = if ($explicit.Contains($id)) { 'direct' } else { 'indirect' }
                scope        = if ($development.Contains($id)) { 'development' } else { 'runtime' }
                dependencies = [string[]] $children
            }
        }

        if ($resolved.Count -gt 0) {
            $manifests[$relative] = [ordered]@{
                name     = $relative
                file     = [ordered]@{ source_location = $relative }
                resolved = $resolved
            }
        }
    }
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

$job = [ordered]@{ correlator = $Correlator; id = $JobId }
if ($JobUrl) {
    $job['html_url'] = $JobUrl
}

$snapshot = [ordered]@{
    version   = 0
    sha       = $Sha
    ref       = $Ref
    job       = $job
    detector  = [ordered]@{
        name    = 'Microsoft Component Detection'
        version = $DetectorVersion
        url     = 'https://github.com/microsoft/component-detection'
    }
    scanned   = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ', $InvariantCulture)
    manifests = $manifests
}

$packageUrls = @($manifests.Values | ForEach-Object { $_['resolved'].Keys } | Sort-Object -Unique)
$entries = @($manifests.Values | ForEach-Object { $_['resolved'].Values })
$direct = @($entries | Where-Object { $_['relationship'] -ceq 'direct' })
$developmentEntries = @($entries | Where-Object { $_['scope'] -ceq 'development' })
$sentinelUrl = "pkg:nuget/$SentinelPackage@$(Get-PinnedVersion -PackageId $SentinelPackage)"
$lines = @(
    '## Dependency snapshot',
    '',
    "Component Detection v$DetectorVersion (SHA-256 verified). Commit ``$Sha``, ref ``$Ref``, correlator ``$Correlator``.",
    '',
    '| Manifests | Distinct NuGet packages | Direct references | Development entries |',
    '| --- | --- | --- | --- |',
    "| $($manifests.Count) | $($packageUrls.Count) | $($direct.Count) | $($developmentEntries.Count) |"
)
$lines | ForEach-Object { Write-Host $_ }
if ($env:GITHUB_STEP_SUMMARY) {
    $lines | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}

if ($packageUrls.Count -lt $MinimumPackageCount) {
    throw "Only $($packageUrls.Count) NuGet packages detected (at least $MinimumPackageCount expected): the scan missed the restore."
}

if ($packageUrls -cnotcontains $sentinelUrl) {
    throw "The snapshot lacks $sentinelUrl, the Roslyn pin of Directory.Packages.props: the scan missed the analyzers and generators."
}

$output = if ([System.IO.Path]::IsPathRooted($OutputPath)) { $OutputPath } else { Join-Path -Path $RepositoryRoot -ChildPath $OutputPath }
New-Item -ItemType Directory -Force -Path (Split-Path -Path $output -Parent) | Out-Null
[System.IO.File]::WriteAllText($output, ($snapshot | ConvertTo-Json -Depth 20), [System.Text.UTF8Encoding]::new($false))
Write-Host "Snapshot written to $output."
