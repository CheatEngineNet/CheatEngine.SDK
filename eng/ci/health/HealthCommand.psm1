#Requires -Version 7.0
<#
.SYNOPSIS
    Side-effect helpers shared by the scheduled health scripts: native commands with exit-code checks, git queries,
    the project inventory, and the GitHub Actions output and summary files.

.DESCRIPTION
    Decisions do not belong here: they live in HealthCheck.psm1, which is pure and tested offline. Every native command
    goes through Invoke-NativeCommand or Get-NativeCommandOutput, which check $LASTEXITCODE (shared.md 7). No helper
    writes to GITHUB_ENV (zizmor github-env); step outputs go to GITHUB_OUTPUT.
#>

Set-StrictMode -Version Latest

Import-Module -Name (Join-Path -Path $PSScriptRoot -ChildPath 'HealthCheck.psm1')

<#
.SYNOPSIS
    The repository root: two folders above eng/ci/health.
#>
function Get-HealthRepositoryRoot {
    [CmdletBinding()]
    [OutputType([string])]
    param()

    return [System.IO.Path]::GetFullPath((Join-Path -Path $PSScriptRoot -ChildPath '../../..'))
}

<#
.SYNOPSIS
    Runs a native command with its output on the host and throws when it exits with another code than 0, unless
    -AllowFailure is set, in which case the exit code is returned.
#>
function Invoke-NativeCommand {
    [CmdletBinding()]
    [OutputType([int])]
    param(
        [Parameter(Mandatory)] [string] $FilePath,
        [AllowEmptyCollection()] [Parameter(Mandatory)] [string[]] $ArgumentList,
        [Parameter(Mandatory)] [string] $Description,
        [switch] $AllowFailure
    )

    Write-Host "> $([System.IO.Path]::GetFileName($FilePath)) $($ArgumentList -join ' ')"
    & $FilePath @ArgumentList | Out-Host
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0 -and -not $AllowFailure) {
        throw "$Description failed with exit code $exitCode."
    }

    if ($AllowFailure) {
        return $exitCode
    }
}

<#
.SYNOPSIS
    Runs a native command and returns its standard output lines; throws on a non-zero exit code.
#>
function Get-NativeCommandOutput {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)] [string] $FilePath,
        [AllowEmptyCollection()] [Parameter(Mandatory)] [string[]] $ArgumentList,
        [Parameter(Mandatory)] [string] $Description
    )

    $output = & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }

    return @($output | ForEach-Object { [string] $_ })
}

<#
.SYNOPSIS
    Runs git in the repository and returns its non-empty output lines; throws on a non-zero exit code.
#>
function Get-GitOutput {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)] [string[]] $ArgumentList
    )

    $root = Get-HealthRepositoryRoot
    return @(Get-NativeCommandOutput -FilePath 'git' -ArgumentList (@('-C', $root) + $ArgumentList) `
            -Description "git $($ArgumentList -join ' ')" | Where-Object { $_ })
}

<#
.SYNOPSIS
    Writes the raw bytes of a git object (for example v1.0.0:native/x.c) to a file: line endings and encodings are
    kept exactly as committed, which a PowerShell pipeline would not guarantee.
#>
function Save-GitBlob {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Object,
        [Parameter(Mandatory)] [string] $Destination
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new('git')
    foreach ($argument in @('-C', (Get-HealthRepositoryRoot), 'cat-file', 'blob', $Object)) {
        $startInfo.ArgumentList.Add($argument)
    }

    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false
    $process = [System.Diagnostics.Process]::Start($startInfo)
    try {
        $errorText = $process.StandardError.ReadToEndAsync()
        $file = [System.IO.File]::Create($Destination)
        try {
            $process.StandardOutput.BaseStream.CopyTo($file)
        }
        finally {
            $file.Dispose()
        }

        $process.WaitForExit()
        if ($process.ExitCode -ne 0) {
            throw "git cat-file blob $Object failed with exit code $($process.ExitCode): $($errorText.Result.Trim())"
        }
    }
    finally {
        $process.Dispose()
    }
}

<#
.SYNOPSIS
    The projects to restore: the solution first, then every tracked project the solution does not list (the same
    inventory as eng/Update-LockFiles.ps1), as repository-relative paths.
#>
function Get-RepositoryRestoreTarget {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [string] $SolutionFile = 'CheatEngine.SDK.slnx'
    )

    $root = Get-HealthRepositoryRoot
    $inSolution = @(Get-SolutionProjectPath -SolutionXml (Get-Content -Raw -LiteralPath (Join-Path -Path $root -ChildPath $SolutionFile)))
    $outside = @(Get-GitOutput -ArgumentList @('ls-files', '--', '*.csproj') | Where-Object { $_ -notin $inSolution } | Sort-Object)
    return @($SolutionFile) + $outside
}

<#
.SYNOPSIS
    Appends name=value lines to the GitHub Actions step output file when it exists. Values must be single-line.
#>
function Write-HealthOutput {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Value
    )

    if (-not $env:GITHUB_OUTPUT) {
        return
    }

    $lines = foreach ($name in $Value.Keys) {
        $text = [string] $Value[$name]
        if ($text.IndexOfAny([char[]] "`r`n") -ge 0) {
            throw "The step output '$name' must be a single line."
        }

        "$name=$text"
    }

    $lines | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}

<#
.SYNOPSIS
    Prints Markdown lines and appends them to the GitHub Actions job summary when it exists.
#>
function Write-HealthSummary {
    [CmdletBinding()]
    param(
        [AllowEmptyCollection()] [AllowEmptyString()] [Parameter(Mandatory)] [string[]] $Line
    )

    $Line | ForEach-Object { Write-Host $_ }
    if ($env:GITHUB_STEP_SUMMARY) {
        $Line | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
    }
}

<#
.SYNOPSIS
    Escapes a value for a Markdown table cell.
#>
function ConvertTo-HealthTableCell {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [AllowEmptyString()] [AllowNull()] [string] $Text
    )

    return ([string] $Text).Replace('|', '\|').Replace("`r", ' ').Replace("`n", ' ')
}

<#
.SYNOPSIS
    Resolves a path against the repository root unless it is already absolute, and creates the directory.
#>
function New-HealthOutputDirectory {
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)] [string] $Path
    )

    $full = if ([System.IO.Path]::IsPathRooted($Path)) { $Path } else { Join-Path -Path (Get-HealthRepositoryRoot) -ChildPath $Path }
    $full = [System.IO.Path]::GetFullPath($full)
    if ($PSCmdlet.ShouldProcess($full, 'Create directory')) {
        New-Item -ItemType Directory -Force -Path $full | Out-Null
    }

    return $full
}

Export-ModuleMember -Function Get-HealthRepositoryRoot, Invoke-NativeCommand, Get-NativeCommandOutput, Get-GitOutput,
Save-GitBlob, Get-RepositoryRestoreTarget, Write-HealthOutput, Write-HealthSummary, ConvertTo-HealthTableCell,
New-HealthOutputDirectory
