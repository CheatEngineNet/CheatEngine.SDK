#Requires -Version 7.0
<#
.SYNOPSIS
    Checks the external links of the tracked Markdown files and reports broken ones without failing.

.DESCRIPTION
    External URLs change without a commit, so they are checked here, weekly, and never in a pull request gate. Links back
    into this repository's main branch are not requested: the repository tests resolve them offline
    (DocumentationIntegrityTests).

    1. Collect the http(s) targets of every tracked *.md file (Get-ExternalLinkTarget in HealthCheck.psm1: fenced code,
       code spans, local hosts, example domains and templated URLs are skipped).
    2. Request each URL with HEAD, falling back to GET when the server refuses HEAD or answers 404/410 to it, following
       redirects, with up to three attempts and a growing pause for inconclusive answers.
    3. Classify (Get-ExternalLinkVerdict): 404 and 410 are broken; rate limiting (429, 403), server errors and timeouts
       are inconclusive warnings.
    4. Write links.json and the job summary, and broken=true|false to GITHUB_OUTPUT. The script exits 0 whatever it
       finds: the scheduled health workflow turns broken=true into its issue.

.PARAMETER OutputDirectory
    Where links.json is written.

.PARAMETER TimeoutSeconds
    Timeout of each request.

.EXAMPLE
    ./eng/ci/health/Test-ExternalLink.ps1 -OutputDirectory "$env:TEMP/links"
#>
[CmdletBinding()]
param(
    [string] $OutputDirectory = 'artifacts/health/links',

    [ValidateRange(1, 120)]
    [int] $TimeoutSeconds = 20
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module -Name (Join-Path -Path $PSScriptRoot -ChildPath 'HealthCheck.psm1') -Force
Import-Module -Name (Join-Path -Path $PSScriptRoot -ChildPath 'HealthCommand.psm1') -Force

# Repository-specific constant (the CheatEngine.Client twin changes only this line).
$RepositorySlug = 'CheatEngineNet/CheatEngine.SDK'
$MaximumAttempts = 3
$UserAgent = "$RepositorySlug scheduled link check"

$root = Get-HealthRepositoryRoot
$output = New-HealthOutputDirectory -Path $OutputDirectory

function Get-LinkStatus {
    param(
        [Parameter(Mandatory)] [string] $Url,
        [Parameter(Mandatory)] [ValidateSet('Head', 'Get')] [string] $Method,
        [Parameter(Mandatory)] [int] $Timeout
    )

    try {
        $response = Invoke-WebRequest -Uri $Url -Method $Method -SkipHttpErrorCheck -MaximumRedirection 10 -TimeoutSec $Timeout `
            -UserAgent $UserAgent -UseBasicParsing
        return [int] $response.StatusCode
    }
    catch {
        # DNS failures, TLS errors, timeouts and redirect loops have no status: inconclusive, never broken.
        Write-Verbose "$Method $Url failed: $($_.Exception.Message)"
        return 0
    }
}

$locations = [ordered]@{}
foreach ($file in @(Get-GitOutput -ArgumentList @('ls-files', '--', '*.md'))) {
    $text = Get-Content -Raw -LiteralPath (Join-Path -Path $root -ChildPath $file)
    foreach ($url in @(Get-ExternalLinkTarget -Markdown ([string] $text) -RepositorySlug $RepositorySlug)) {
        if (-not $locations.Contains($url)) {
            $locations[$url] = $file
        }
    }
}

$results = [System.Collections.Generic.List[object]]::new()
foreach ($url in $locations.Keys) {
    $status = 0
    $verdict = 'Inconclusive'
    for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++) {
        $status = Get-LinkStatus -Url $url -Method Head -Timeout $TimeoutSeconds
        # Some servers refuse HEAD (405, 501, 403) or answer it differently: GET decides before a link counts as broken.
        if ($status -in @(0, 403, 404, 405, 410, 501)) {
            $status = Get-LinkStatus -Url $url -Method Get -Timeout $TimeoutSeconds
        }

        $verdict = Get-ExternalLinkVerdict -StatusCode $status
        if ($verdict -cne 'Inconclusive' -or $attempt -eq $MaximumAttempts) {
            break
        }

        Start-Sleep -Seconds (5 * $attempt)
    }

    $results.Add([pscustomobject]@{ Url = $url; File = $locations[$url]; Status = $status; Verdict = $verdict })
}

$broken = @($results | Where-Object { $_.Verdict -ceq 'Broken' })
$inconclusive = @($results | Where-Object { $_.Verdict -ceq 'Inconclusive' })
[System.IO.File]::WriteAllText((Join-Path -Path $output -ChildPath 'links.json'), (ConvertTo-Json -InputObject @($results) -Depth 3),
    [System.Text.UTF8Encoding]::new($false))

$summary = @(
    '## External documentation links',
    '',
    "$($results.Count) distinct external links in tracked Markdown files: $($results.Count - $broken.Count - $inconclusive.Count) ok, $($broken.Count) broken, $($inconclusive.Count) inconclusive.",
    ''
)
if ($broken.Count + $inconclusive.Count -gt 0) {
    $summary += '| Verdict | Status | Link | First found in |'
    $summary += '| --- | --- | --- | --- |'
    foreach ($item in @($broken) + @($inconclusive)) {
        $summary += "| $($item.Verdict) | $(if ($item.Status) { $item.Status } else { 'no answer' }) | $(ConvertTo-HealthTableCell -Text $item.Url) | ``$($item.File)`` |"
    }
}

Write-HealthSummary -Line $summary
foreach ($item in $broken) {
    Write-Host "::warning title=Broken link::$($item.File): $($item.Url) answered $($item.Status)."
}

Write-HealthOutput -Value ([ordered]@{ broken = $(if ($broken.Count -gt 0) { 'true' } else { 'false' }) })
