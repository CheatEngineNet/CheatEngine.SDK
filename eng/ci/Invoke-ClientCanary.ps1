#Requires -Version 7.0
<#
.SYNOPSIS
    Builds CheatEngine.Client against a CheatEngine.SDK package from this repository and reports what breaks.

.DESCRIPTION
    The advisory client-canary job (audit Q48, ADR-10) answers one question before an SDK release: which Client code
    stops compiling against this SDK package? It never gates: CheatEngine.Client stays on CheatEngine.SDK 1.0.0 and
    moves to 2.x only through its documented migration (docs/migration/sdk-2.0.md of the Client).

      1. The Client is taken from -ClientDirectory (an actions/checkout of CheatEngineNet/CheatEngine.Client), or cloned
         shallowly from -ClientRepository at -ClientRef.
      2. A temporary NuGet.Config maps CheatEngine.SDK to a local folder feed that holds only -PackagePath, and every
         other package to nuget.org (packageSourceMapping). NUGET_PACKAGES points to an isolated folder, so the global
         package cache never receives the branch package.
      3. The Client solution is restored (re-evaluating its lock files, never in locked mode) and built in Release
         with the Client's canary switch: the SDK version property set to the package version, plus -CanaryProperty
         (defaults follow the Client's eng/CheatEngineSdk.props: a 3.0.0 upper bound, CheatEngineSdkCanary, unsupported
         SDK allowed).
      4. Every distinct `error <CODE>: <message>` line of restore and build becomes an entry of
         client-canary-report.json (cheatengine-client-canary-report/v0, eng/ci/client-canary-report.v0.schema.json)
         and client-canary-report.md, with paths relative to the Client root.

    The script exits 0 whether or not the Client compiles; it fails only when it cannot run the experiment (missing
    package, clone failure, unusable output directory).

.PARAMETER PackagePath
    The CheatEngine.SDK.<version>.nupkg to test (the nuget-package artifact of the same run).

.PARAMETER OutputDirectory
    Receives client-canary-report.json and client-canary-report.md.

.PARAMETER ClientDirectory
    An existing checkout of CheatEngine.Client. When empty, the script clones -ClientRepository at -ClientRef.

.PARAMETER ClientRepository
    The Client repository to clone.

.PARAMETER ClientRef
    The branch or tag to clone.

.PARAMETER SdkVersionProperty
    The MSBuild property that selects the consumed CheatEngine.SDK version in the Client.

.PARAMETER CanaryProperty
    Additional Name=Value MSBuild properties of the Client's canary switch.

.PARAMETER WorkDirectory
    Scratch directory for the feed, the configuration, the package folder and the clone.

.EXAMPLE
    ./eng/ci/Invoke-ClientCanary.ps1 -PackagePath artifacts/nuget/CheatEngine.SDK.2.0.0-alpha.0.62.nupkg -OutputDirectory artifacts/client-canary
#>
[CmdletBinding()]
param(
	[Parameter(Mandatory)]
	[string] $PackagePath,

	[Parameter(Mandatory)]
	[string] $OutputDirectory,

	[string] $ClientDirectory = '',

	[string] $ClientRepository = 'https://github.com/CheatEngineNet/CheatEngine.Client.git',

	[string] $ClientRef = 'main',

	[string] $SdkVersionProperty = 'CheatEngineSdkVersion',

	# RestorePackagesWithLockFile=false is deliberately absent: NuGet refuses it while lock files exist (NU1005). The
	# restore instead re-evaluates the committed lock files (--force-evaluate) in this throw-away checkout.
	[string[]] $CanaryProperty = @(
		'CheatEngineSdkUpperBound=3.0.0'
		'CheatEngineSdkCanary=true'
		'RestoreLockedMode=false'
		'CheatEngineClientAllowUnsupportedSdk=true'
	),

	[string] $WorkDirectory = (Join-Path ($env:RUNNER_TEMP ?? [IO.Path]::GetTempPath()) 'client-canary')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$InformationPreference = 'Continue'

$reportSchema = 'cheatengine-client-canary-report/v0'
$solutionName = 'CheatEngine.Client.slnx'
$errorLine = '\berror (?<code>[A-Z][A-Za-z]*\d+)\s*:\s*(?<message>.+?)(?:\s+\[[^\]]+\])?\s*$'
# `File.cs(12,5): error CS0246: ...` or `Project.csproj : error NU1102: ...`; the line part is optional.
$locationPrefix = '^\s*(?<file>[^(]+?)\s*(?:\((?<line>\d+)(?:,\d+)?\))?\s*:\s*$'

function Invoke-Logged {
	param(
		[Parameter(Mandatory)] [string] $Label,
		[Parameter(Mandatory)] [string[]] $Arguments,
		[Parameter(Mandatory)] [string] $LogPath
	)

	Write-Information "dotnet $($Arguments -join ' ')"
	& dotnet @Arguments 2>&1 | ForEach-Object { "$_" } | Tee-Object -FilePath $LogPath | Out-Host
	$exitCode = $LASTEXITCODE
	Write-Information "$Label exited with code $exitCode."
	return $exitCode
}

# One entry per distinct (code, file, line, message); MSBuild repeats every error in its final summary.
function Get-Diagnostic {
	param(
		[Parameter(Mandatory)] [string] $LogPath,
		[Parameter(Mandatory)] [string] $Root
	)

	$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
	$diagnostics = [Collections.Generic.List[object]]::new()
	$rootPrefix = $Root.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
	foreach ($line in [IO.File]::ReadAllLines($LogPath)) {
		if ($line -notmatch $errorLine) {
			continue
		}
		$code = $Matches['code']
		$message = $Matches['message'].Trim()
		$prefix = $line.Substring(0, $line.IndexOf(" error $code", [StringComparison]::Ordinal) + 1)
		$file = $null
		$lineNumber = $null
		if ($prefix -match $locationPrefix) {
			$file = $Matches['file'].Trim()
			$lineNumber = if ($Matches.ContainsKey('line')) { [int] $Matches['line'] } else { $null }
			if ($file.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
				$file = $file.Substring($rootPrefix.Length)
			}
			$file = $file.Replace('\', '/')
			if ([IO.Path]::IsPathRooted($file)) {
				$file = [IO.Path]::GetFileName($file)
			}
		}
		$message = $message.Replace($rootPrefix, '').Replace($Root, '.')
		if ($seen.Add("$code|$file|$lineNumber|$message")) {
			$diagnostics.Add([ordered]@{ code = $code; file = $file; line = $lineNumber; message = $message })
		}
	}
	return , $diagnostics
}

$package = Get-Item -LiteralPath ([IO.Path]::GetFullPath($PackagePath, $PWD.Path))
if ($package.Name -notmatch '^CheatEngine\.SDK\.(?<version>\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)\.nupkg$') {
	throw "'$($package.Name)' is not a CheatEngine.SDK.<version>.nupkg."
}
$packageVersion = $Matches['version']
$packageSha256 = (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash.ToLowerInvariant()

$work = [IO.Path]::GetFullPath($WorkDirectory, $PWD.Path)
if (Test-Path -LiteralPath $work) {
	Remove-Item -LiteralPath $work -Recurse -Force
}
$feed = Join-Path $work 'feed'
$packages = Join-Path $work 'packages'
$logs = Join-Path $work 'logs'
New-Item -ItemType Directory -Path $feed, $packages, $logs -Force | Out-Null
Copy-Item -LiteralPath $package.FullName -Destination $feed

if ($ClientDirectory) {
	$client = [IO.Path]::GetFullPath($ClientDirectory, $PWD.Path)
}
else {
	$client = Join-Path $work 'client'
	& git clone --quiet --depth 1 --single-branch --branch $ClientRef $ClientRepository $client
	if ($LASTEXITCODE -ne 0) {
		throw "Cloning $ClientRepository at '$ClientRef' failed with exit code $LASTEXITCODE."
	}
}
$solution = Join-Path $client $solutionName
if (-not (Test-Path -LiteralPath $solution -PathType Leaf)) {
	throw "'$client' holds no $solutionName."
}
$clientCommit = "$(& git -C $client rev-parse HEAD)".Trim()
if ($LASTEXITCODE -ne 0 -or $clientCommit -notmatch '^[0-9a-f]{40}$') {
	throw "Could not read the commit of the Client checkout '$client'."
}

# CheatEngine.SDK resolves only from the local feed; everything else only from nuget.org.
$configuration = Join-Path $work 'NuGet.Config'
$feedUri = [Security.SecurityElement]::Escape($feed)
@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="canary-sdk" value="$feedUri" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="canary-sdk">
      <package pattern="CheatEngine.SDK" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@ | Set-Content -LiteralPath $configuration -Encoding utf8NoBOM

$properties = @("-p:$SdkVersionProperty=$packageVersion") + @($CanaryProperty | ForEach-Object { "-p:$_" })
$previousPackages = $env:NUGET_PACKAGES
$env:NUGET_PACKAGES = $packages
Push-Location -LiteralPath $client
try {
	$restoreLog = Join-Path $logs 'restore.log'
	$restoreExit = Invoke-Logged -Label 'Restore' -LogPath $restoreLog -Arguments (@('restore', $solution, '--configfile', $configuration, '--force-evaluate') + $properties)
	$buildExit = $null
	$buildLog = Join-Path $logs 'build.log'
	if ($restoreExit -eq 0) {
		$buildExit = Invoke-Logged -Label 'Build' -LogPath $buildLog -Arguments (@('build', $solution, '-c', 'Release', '--no-restore') + $properties)
	}
}
finally {
	Pop-Location
	$env:NUGET_PACKAGES = $previousPackages
}

$diagnostics = Get-Diagnostic -LogPath $restoreLog -Root $client
if ($null -ne $buildExit) {
	$diagnostics.AddRange((Get-Diagnostic -LogPath $buildLog -Root $client))
}
$outcome = if ($restoreExit -ne 0) { 'RestoreFailed' } elseif ($buildExit -ne 0) { 'BuildFailed' } else { 'Compatible' }

$report = [ordered]@{
	schema = $reportSchema
	sdkPackage = [ordered]@{ id = 'CheatEngine.SDK'; version = $packageVersion; sha256 = $packageSha256 }
	client = [ordered]@{ repository = $ClientRepository; ref = $ClientRef; commit = $clientCommit }
	properties = @($properties | ForEach-Object { $_.Substring(3) })
	restoreExitCode = $restoreExit
	buildExitCode = $buildExit
	outcome = $outcome
	errorCount = $diagnostics.Count
	errors = @($diagnostics)
	createdUtc = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ', [Globalization.CultureInfo]::InvariantCulture)
}

$output = [IO.Path]::GetFullPath($OutputDirectory, $PWD.Path)
New-Item -ItemType Directory -Path $output -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $output 'client-canary-report.json'), (ConvertTo-Json -InputObject $report -Depth 5) + "`n", [Text.UTF8Encoding]::new($false))

$byCode = @($diagnostics | Group-Object -Property { $_.code } | Sort-Object -Property Count -Descending)
$markdown = [Collections.Generic.List[string]]::new()
$markdown.Add('### Client canary (advisory)')
$markdown.Add('')
$markdown.Add("CheatEngine.Client ``$ClientRef`` (``$clientCommit``) against CheatEngine.SDK ``$packageVersion`` (SHA-256 ``$packageSha256``): **$outcome**, $($diagnostics.Count) distinct error(s). This job never gates.")
if ($byCode.Count -gt 0) {
	$markdown.Add('')
	$markdown.Add('| Code | Count | First occurrence |')
	$markdown.Add('| --- | ---: | --- |')
	foreach ($group in $byCode) {
		$first = $group.Group[0]
		$where = if (-not $first.file) { '' } elseif ($null -eq $first.line) { "``$($first.file)`` " } else { "``$($first.file):$($first.line)`` " }
		$markdown.Add("| $($group.Name) | $($group.Count) | $where$($first.message.Replace('|', '\|')) |")
	}
}
[IO.File]::WriteAllLines((Join-Path $output 'client-canary-report.md'), [string[]] $markdown, [Text.UTF8Encoding]::new($false))
if ($env:GITHUB_STEP_SUMMARY) {
	$markdown | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}
$markdown | ForEach-Object { Write-Information $_ }
exit 0
