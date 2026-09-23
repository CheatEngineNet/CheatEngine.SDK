#Requires -Version 7.0
<#
.SYNOPSIS
    Opens, or comments on, the single "Scheduled health check needs attention" issue when a scheduled health run found
    a problem.

.DESCRIPTION
    Runs in the notify job of .github/workflows/scheduled-health.yml, for scheduled runs only, with a token that may
    write issues and nothing else.

    1. Decide with Get-HealthIssueReport (HealthCheck.psm1): the run needs attention when a job did not succeed, when the
       release bridge rebuild drifted, or when broken links were found. The body is built from closed vocabularies only
       (job ids, job results, the run URL): no text from a log, a pull request or an issue reaches it.
    2. Nothing to report: print it and stop.
    3. Issues disabled on the repository (has_issues false): print a ::warning:: and stop.
    4. Look for an OPEN issue with exactly that title (Select-HealthIssue) and comment on it; otherwise create it with
       the label -Label. One issue accumulates the failures until a maintainer closes it.

.PARAMETER NeedsJson
    toJSON(needs) of the notify job. Defaults to $env:NEEDS.

.PARAMETER RunUrl
    URL of the workflow run. Defaults to $env:RUN_URL.

.PARAMETER Drift
    'true' when the bridge rebuild drifted. Defaults to $env:DRIFT.

.PARAMETER BrokenLinks
    'true' when broken external links were found. Defaults to $env:BROKEN_LINKS.

.PARAMETER Repository
    owner/name. Defaults to $env:GH_REPO (gh also reads GH_REPO and GH_TOKEN).

.PARAMETER Label
    Label of a newly created issue.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $NeedsJson = $env:NEEDS,
    [string] $RunUrl = $env:RUN_URL,
    [string] $Drift = $env:DRIFT,
    [string] $BrokenLinks = $env:BROKEN_LINKS,
    [string] $Repository = $env:GH_REPO,
    [string] $Label = 'ci'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module -Name (Join-Path -Path $PSScriptRoot -ChildPath 'HealthCheck.psm1') -Force
Import-Module -Name (Join-Path -Path $PSScriptRoot -ChildPath 'HealthCommand.psm1') -Force

$report = Get-HealthIssueReport -NeedsJson $NeedsJson -Drift ([string] $Drift) -BrokenLinks ([string] $BrokenLinks) -RunUrl ([string] $RunUrl)
if (-not $report.NeedsAttention) {
    Write-HealthSummary -Line @('## Report health', '', 'Every scheduled health job succeeded: no issue to open or update.')
    return
}

if (-not [regex]::IsMatch([string] $Repository, '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')) {
    throw "GH_REPO must be owner/name (got '$Repository')."
}

$hasIssues = @(Get-NativeCommandOutput -FilePath 'gh' -ArgumentList @('api', "repos/$Repository", '--jq', '.has_issues') -Description 'Reading has_issues')[0]
if ([string] $hasIssues -ceq 'false') {
    Write-Host "::warning title=Scheduled health::Issues are disabled on $Repository; the health problems are only in this run's summary."
    Write-HealthSummary -Line @('## Report health', '', $report.Body)
    return
}

$openIssues = (Get-NativeCommandOutput -FilePath 'gh' -Description 'Listing the open issues' -ArgumentList @(
        'issue', 'list', '--repo', $Repository, '--state', 'open', '--json', 'number,title', '--limit', '500')) -join "`n"
$existing = Select-HealthIssue -IssueListJson $openIssues

$bodyFile = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), "health-issue-$([guid]::NewGuid().ToString('N')).md")
[System.IO.File]::WriteAllText($bodyFile, $report.Body, [System.Text.UTF8Encoding]::new($false))
try {
    if ($null -ne $existing) {
        if ($PSCmdlet.ShouldProcess("$Repository#$existing", 'Comment on the scheduled health issue')) {
            Invoke-NativeCommand -FilePath 'gh' -Description "Commenting on issue #$existing" -ArgumentList @(
                'issue', 'comment', [string] $existing, '--repo', $Repository, '--body-file', $bodyFile)
        }

        $outcome = "Commented on the open issue #$existing."
    }
    else {
        if ($PSCmdlet.ShouldProcess($Repository, "Create the issue '$($report.Title)'")) {
            Invoke-NativeCommand -FilePath 'gh' -Description 'Creating the scheduled health issue' -ArgumentList @(
                'issue', 'create', '--repo', $Repository, '--title', $report.Title, '--label', $Label, '--body-file', $bodyFile)
        }

        $outcome = "Opened the issue '$($report.Title)'."
    }
}
finally {
    Remove-Item -LiteralPath $bodyFile -Force -ErrorAction SilentlyContinue
}

Write-HealthSummary -Line @('## Report health', '', $outcome, '', $report.Body)
