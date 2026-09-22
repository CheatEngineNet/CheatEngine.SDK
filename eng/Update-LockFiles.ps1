#Requires -Version 7.0
<#
.SYNOPSIS
    Regenerates, or verifies, the NuGet lock file (packages.lock.json) of every project in the repository.

.DESCRIPTION
    Lock files pin every direct and transitive package, including the packages the .NET SDK adds implicitly
    (Microsoft.NET.ILLink.Tasks, the ILCompiler packages of Native AOT projects), so they are regenerated only by this
    script, never by hand or by an IDE (eng/api/README.md#lock-files).

    Sequence:
      1. Preconditions: a Windows host (the win-x64 sections of Native AOT projects record host-specific ILCompiler
         packages) and `dotnet --version` equal to global.json sdk.version.
      2. Every project is enumerated from git (tracked and untracked, non-ignored *.csproj); the <Project> entries of
         CheatEngine.SDK.slnx tell which ones restore through the solution. No project name is hard-coded.
      3. `dotnet restore CheatEngine.SDK.slnx --force-evaluate`, then each project outside the solution the same way.
      4. A lock whose JSON did not change gets its committed bytes back, so line endings never produce a diff; a
         changed lock keeps NuGet's JSON with the committed final-newline state.
      5. Verification restores with `--locked-mode` (never combined with --force-evaluate, which fails with NU1005).
      6. Structural checks: a lock per project; format version 2 for Central Package Management projects; no
         CentralTransitive entry in a version 1 lock; a <tfm>/<rid> section for every evaluated runtime identifier, with
         runtime.<rid>.Microsoft.DotNet.ILCompiler when PublishAot is true; no CheatEngine.* package resolved from a
         feed (the SDK never consumes its own package; the ApiCompat baseline is a PackageDownload, not locked).

    Default mode writes the files and lists the changed ones. -Verify (the CI `lock-files` job) runs the same
    regeneration, then fails when a lock file differs from the commit or is untracked.

.PARAMETER Verify
    Fail, naming the files, when the regenerated lock files differ from the committed ones or a lock file is untracked.

.EXAMPLE
    ./eng/Update-LockFiles.ps1

    Regenerates every lock file after a dependency, global.json or project change; commit the listed files.

.EXAMPLE
    ./eng/Update-LockFiles.ps1 -Verify

    Checks that the committed lock files are exactly what a fresh restore produces.
#>
[CmdletBinding()]
param(
	[switch] $Verify
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$InformationPreference = 'Continue'

$repositoryRoot = Split-Path -Path $PSScriptRoot -Parent
$solutionFile = 'CheatEngine.SDK.slnx'
$lockFileName = 'packages.lock.json'
$regenerateCommand = './eng/Update-LockFiles.ps1'

# Repository-relative project paths that deliberately have no lock file, each with its reason. Empty in the SDK: no
# project opts out of lock files or of Central Package Management (CESDK9005).
$excludedProjects = @{}

function Invoke-Dotnet {
	param(
		[Parameter(Mandatory)] [string[]] $Arguments
	)

	& dotnet @Arguments
	if ($LASTEXITCODE -ne 0) {
		throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
	}
}

function Invoke-Git {
	param(
		[Parameter(Mandatory)] [string[]] $Arguments
	)

	$output = & git -C $repositoryRoot @Arguments
	if ($LASTEXITCODE -ne 0) {
		throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
	}

	return @($output | Where-Object { $_ })
}

function Confirm-Precondition {
	if (-not $IsWindows) {
		throw 'Lock files are regenerated on Windows only: the win-x64 sections of the Native AOT projects record host-specific ILCompiler packages.'
	}

	$globalJsonPath = Join-Path -Path $repositoryRoot -ChildPath 'global.json'
	$pinned = (Get-Content -Raw -LiteralPath $globalJsonPath | ConvertFrom-Json).sdk.version
	$installCommand = "winget install Microsoft.DotNet.SDK.10 --version $pinned"
	Push-Location -LiteralPath $repositoryRoot
	try {
		$actual = "$(& dotnet --version 2>&1)".Trim()
		$exitCode = $LASTEXITCODE
	}
	finally {
		Pop-Location
	}

	if ($exitCode -ne 0) {
		throw "dotnet --version failed ($actual). The repository needs .NET SDK $pinned exactly: $installCommand"
	}

	if ($actual -ne $pinned) {
		throw "dotnet --version is '$actual', global.json pins '$pinned'. Lock files record SDK-implicit packages, so only the pinned SDK regenerates them: $installCommand"
	}
}

function Get-RepositoryProject {
	$tracked = @(Invoke-Git -Arguments @('ls-files', '--', '*.csproj'))
	$untracked = @(Invoke-Git -Arguments @('ls-files', '--others', '--exclude-standard', '--', '*.csproj'))
	return @($tracked + $untracked |
		ForEach-Object { $_.Replace('\', '/') } |
		Where-Object { -not $excludedProjects.ContainsKey($_) } |
		Sort-Object -Unique)
}

function Get-SolutionProject {
	[xml] $solution = Get-Content -Raw -LiteralPath (Join-Path -Path $repositoryRoot -ChildPath $solutionFile)
	return @($solution.SelectNodes('//Project') | ForEach-Object { $_.GetAttribute('Path').Replace('\', '/') })
}

function Get-LockPath {
	param(
		[Parameter(Mandatory)] [string] $Project
	)

	$projectDirectory = Split-Path -Path (Join-Path -Path $repositoryRoot -ChildPath $Project) -Parent
	return Join-Path -Path $projectDirectory -ChildPath $lockFileName
}

function Get-EvaluatedProperty {
	param(
		[Parameter(Mandatory)] [string] $Project
	)

	$names = @('TargetFramework', 'TargetFrameworks', 'RuntimeIdentifier', 'RuntimeIdentifiers', 'PublishAot',
		'ManagePackageVersionsCentrally', 'RestorePackagesWithLockFile')
	$arguments = @('msbuild', (Join-Path -Path $repositoryRoot -ChildPath $Project), '-nologo') +
		@($names | ForEach-Object { "-getProperty:$_" })
	$json = & dotnet @arguments
	if ($LASTEXITCODE -ne 0) {
		throw "Evaluating $Project failed with exit code $LASTEXITCODE."
	}

	return ($json -join "`n" | ConvertFrom-Json).Properties
}

function ConvertTo-ItemList {
	param(
		[AllowEmptyString()] [string] $Value
	)

	return @($Value.Split(';', [StringSplitOptions]::RemoveEmptyEntries -bor [StringSplitOptions]::TrimEntries))
}

function Confirm-LockStructure {
	param(
		[Parameter(Mandatory)] [string[]] $Projects
	)

	$problems = [System.Collections.Generic.List[string]]::new()
	foreach ($project in $Projects) {
		$lockPath = Get-LockPath -Project $project
		if (-not (Test-Path -LiteralPath $lockPath)) {
			$problems.Add("$project has no $lockFileName.")
			continue
		}

		$lock = Get-Content -Raw -LiteralPath $lockPath | ConvertFrom-Json -AsHashtable
		$version = $lock['version']
		if ($version -notin 1, 2) {
			$problems.Add("${project}: unsupported lock file version '$version'.")
			continue
		}

		$properties = Get-EvaluatedProperty -Project $project
		if ($properties.RestorePackagesWithLockFile -ne 'true') {
			$problems.Add("$project does not set RestorePackagesWithLockFile (CESDK9005).")
		}

		if ($properties.ManagePackageVersionsCentrally -eq 'true' -and $version -ne 2) {
			$problems.Add("$project uses Central Package Management but its lock file is version $version.")
		}

		$sections = $lock['dependencies']
		foreach ($sectionName in $sections.Keys) {
			foreach ($packageId in $sections[$sectionName].Keys) {
				$type = $sections[$sectionName][$packageId]['type']
				if ($version -eq 1 -and $type -eq 'CentralTransitive') {
					$problems.Add("${project}: the version 1 lock holds the CentralTransitive entry $packageId.")
				}

				if ($type -ne 'Project' -and $packageId -like 'CheatEngine.*') {
					$problems.Add("${project}: $sectionName resolves the package $packageId from a feed; the SDK never consumes its own package.")
				}
			}
		}

		$frameworks = @(@(ConvertTo-ItemList -Value $properties.TargetFramework) +
			@(ConvertTo-ItemList -Value $properties.TargetFrameworks) | Sort-Object -Unique)
		$runtimes = @(@(ConvertTo-ItemList -Value $properties.RuntimeIdentifier) +
			@(ConvertTo-ItemList -Value $properties.RuntimeIdentifiers) | Sort-Object -Unique)
		foreach ($framework in $frameworks) {
			foreach ($runtime in $runtimes) {
				$section = "$framework/$runtime"
				if (-not $sections.ContainsKey($section)) {
					$problems.Add("$project evaluates RuntimeIdentifier $runtime but its lock has no '$section' section (dotnet publish --no-restore would fail).")
					continue
				}

				$compiler = "runtime.$runtime.Microsoft.DotNet.ILCompiler"
				if ($properties.PublishAot -eq 'true' -and -not $sections[$section].ContainsKey($compiler)) {
					$problems.Add("$project publishes Native AOT but '$section' does not lock $compiler.")
				}
			}
		}
	}

	if ($problems.Count -ne 0) {
		throw "Lock file structure check failed:`n  $($problems -join "`n  ")"
	}
}

function Test-JsonEqual {
	param(
		[Parameter(Mandatory)] [byte[]] $Left,
		[Parameter(Mandatory)] [byte[]] $Right
	)

	$encoding = [System.Text.UTF8Encoding]::new($false)
	$leftJson = $encoding.GetString($Left) | ConvertFrom-Json | ConvertTo-Json -Depth 100 -Compress
	$rightJson = $encoding.GetString($Right) | ConvertFrom-Json | ConvertTo-Json -Depth 100 -Compress
	return $leftJson -ceq $rightJson
}

function Get-FinalNewlineLength {
	param(
		[Parameter(Mandatory)] [byte[]] $Content
	)

	if ($Content.Length -ge 2 -and $Content[-2] -eq 13 -and $Content[-1] -eq 10) {
		return 2
	}

	if ($Content.Length -ge 1 -and $Content[-1] -eq 10) {
		return 1
	}

	return 0
}

function Write-LockContent {
	param(
		[Parameter(Mandatory)] [string] $Path,
		[Parameter(Mandatory)] [byte[]] $Committed,
		[Parameter(Mandatory)] [byte[]] $Regenerated
	)

	if (Test-JsonEqual -Left $Committed -Right $Regenerated) {
		[System.IO.File]::WriteAllBytes($Path, $Committed)
		return
	}

	# Changed content: NuGet's JSON with the committed final-newline state.
	$stream = [System.IO.MemoryStream]::new()
	try {
		$stream.Write($Regenerated, 0, $Regenerated.Length - (Get-FinalNewlineLength -Content $Regenerated))
		$committedNewline = Get-FinalNewlineLength -Content $Committed
		$stream.Write($Committed, $Committed.Length - $committedNewline, $committedNewline)
		[System.IO.File]::WriteAllBytes($Path, $stream.ToArray())
	}
	finally {
		$stream.Dispose()
	}
}

Confirm-Precondition

$projects = @(Get-RepositoryProject)
$inSolution = @(Get-SolutionProject)
$outOfSolution = @($projects | Where-Object { $_ -notin $inSolution })
Write-Information "Projects: $($projects.Count), outside ${solutionFile}: $(if ($outOfSolution.Count) { $outOfSolution -join ', ' } else { 'none' })."

# Committed bytes, so an unchanged lock keeps its exact line endings and final-newline state.
$snapshots = @{}
foreach ($project in $projects) {
	$lockPath = Get-LockPath -Project $project
	if (Test-Path -LiteralPath $lockPath) {
		$snapshots[$lockPath] = [System.IO.File]::ReadAllBytes($lockPath)
	}
}

Push-Location -LiteralPath $repositoryRoot
try {
	Write-Information "Regenerating: dotnet restore $solutionFile --force-evaluate"
	Invoke-Dotnet -Arguments @('restore', $solutionFile, '--force-evaluate')
	foreach ($project in $outOfSolution) {
		Write-Information "Regenerating: dotnet restore $project --force-evaluate"
		Invoke-Dotnet -Arguments @('restore', $project, '--force-evaluate')
	}

	foreach ($lockPath in $snapshots.Keys) {
		if (Test-Path -LiteralPath $lockPath) {
			Write-LockContent -Path $lockPath -Committed $snapshots[$lockPath] -Regenerated ([System.IO.File]::ReadAllBytes($lockPath))
		}
	}

	Write-Information "Verifying: dotnet restore $solutionFile --locked-mode"
	Invoke-Dotnet -Arguments @('restore', $solutionFile, '--locked-mode')
	foreach ($project in $outOfSolution) {
		Write-Information "Verifying: dotnet restore $project --locked-mode"
		Invoke-Dotnet -Arguments @('restore', $project, '--locked-mode')
	}
}
finally {
	Pop-Location
}

Write-Information 'Checking the lock file structure.'
Confirm-LockStructure -Projects $projects

if (-not $Verify) {
	$changed = @(Invoke-Git -Arguments @('status', '--porcelain', '--', "*$lockFileName"))
	if ($changed.Count -eq 0) {
		Write-Information 'Lock files are up to date.'
	}
	else {
		Write-Information "Changed lock files (commit them):`n  $($changed -join "`n  ")"
	}

	return
}

$modified = @(Invoke-Git -Arguments @('diff', '--name-only', '--', "*$lockFileName"))
$untracked = @(Invoke-Git -Arguments @('ls-files', '--others', '--exclude-standard', '--', "*$lockFileName"))
if ($modified.Count -ne 0 -or $untracked.Count -ne 0) {
	$files = @($modified) + @($untracked | ForEach-Object { "$_ (untracked)" })
	throw "Lock files are out of date:`n  $($files -join "`n  ")`nRun $regenerateCommand on Windows with the pinned .NET SDK and commit the result."
}

Write-Information 'Lock files match a fresh restore.'
