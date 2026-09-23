#Requires -Version 7.0
<#
.SYNOPSIS
    Entry point of the "PR policy" required check (.github/workflows/pr-policy.yml): checks the pull request title and
    the CHANGELOG entry with the rules of eng/ci/PullRequestPolicy.psm1.

.DESCRIPTION
    The workflow passes every pull-request value through environment variables (never through template expansion in
    the script text), so the defaults of the parameters read them. The changed paths come from
    `git diff --name-only --no-renames -z <base>...<head>` (a rename out of libs/ still counts as a change there), or
    from -ChangedFilesPath, one path per line, which the repository tests and local runs use.

    The script writes a rule table to the job summary when GITHUB_STEP_SUMMARY is set, emits one `::error` annotation per
    failed rule, and exits 1 when any rule failed. It never prints the pull request body.

.PARAMETER Title
    Pull request title. Defaults to $env:PR_TITLE.

.PARAMETER Body
    Pull request description. Defaults to $env:PR_BODY. Only searched for the waiver marker, which counts on a line of
    its own outside code (quoting it, as the pull request template does, never waives the rule).

.PARAMETER Author
    Login of the pull request author. Defaults to $env:PR_AUTHOR. Dependabot pull requests are exempt.

.PARAMETER BaseSha
    Base commit of the pull request. Defaults to $env:BASE_SHA.

.PARAMETER HeadSha
    Head commit of the pull request. Defaults to $env:HEAD_SHA.

.PARAMETER ChangedFilesPath
    A file with the changed paths, one per line. When given, git is not called.

.EXAMPLE
    ./eng/ci/Test-PullRequestPolicy.ps1 -Title 'Add CodeQL analysis' -ChangedFilesPath changed.txt
#>
[CmdletBinding()]
param(
    [string] $Title = $env:PR_TITLE,
    [string] $Body = $env:PR_BODY,
    [string] $Author = $env:PR_AUTHOR,
    [string] $BaseSha = $env:BASE_SHA,
    [string] $HeadSha = $env:HEAD_SHA,
    [string] $ChangedFilesPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module -Name (Join-Path -Path $PSScriptRoot -ChildPath 'PullRequestPolicy.psm1') -Force

# Workflow command data must escape %, CR and LF, or a crafted path could start a second command on a new line.
# https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-commands
function ConvertTo-WorkflowCommandData {
    param(
        [AllowEmptyString()] [Parameter(Mandatory)] [string] $Text
    )

    return $Text.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
}

function ConvertTo-MarkdownCell {
    param(
        [AllowEmptyString()] [Parameter(Mandatory)] [string] $Text
    )

    return $Text.Replace('|', '\|').Replace("`r", ' ').Replace("`n", ' ')
}

function Get-ChangedFile {
    param(
        [string] $ListPath,
        [string] $Base,
        [string] $Head
    )

    if ($ListPath) {
        return @(Get-Content -LiteralPath $ListPath -Encoding utf8 | Where-Object { $_ })
    }

    foreach ($sha in @($Base, $Head)) {
        if (-not [regex]::IsMatch([string] $sha, '^[0-9a-f]{40}([0-9a-f]{24})?$')) {
            throw "BASE_SHA and HEAD_SHA must be full commit ids (got '$sha'). The workflow passes github.event.pull_request.base.sha and head.sha."
        }
    }

    $repositoryRoot = Split-Path -Path $PSScriptRoot -Parent | Split-Path -Parent
    $output = & git -C $repositoryRoot diff --name-only --no-renames -z "$Base...$Head"
    if ($LASTEXITCODE -ne 0) {
        throw "git diff $Base...$Head failed with exit code $LASTEXITCODE. The checkout needs fetch-depth: 0 for the merge base."
    }

    return @((@($output) -join '') -split "`0" | Where-Object { $_ })
}

$changedFiles = @(Get-ChangedFile -ListPath $ChangedFilesPath -Base $BaseSha -Head $HeadSha)
$results = @(Test-PullRequestPolicy -Title $Title -Body $Body -Author $Author -ChangedFile $changedFiles)
$failures = @($results | Where-Object { -not $_.Passed })

if ($env:GITHUB_STEP_SUMMARY) {
    $lines = @('## PR policy', '', '| Rule | Result | Detail |', '| --- | --- | --- |')
    foreach ($result in $results) {
        $verdict = if ($result.Passed) { 'Passed' } else { '**Failed**' }
        $lines += "| $($result.Rule) | $verdict | $(ConvertTo-MarkdownCell -Text $result.Message) |"
    }

    $lines += ''
    $lines += "$($changedFiles.Count) changed path(s) evaluated."
    $lines | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}

foreach ($result in $results) {
    $state = if ($result.Passed) { 'pass' } else { 'FAIL' }
    # Messages can quote paths; a raw line break in a path must not start a workflow command on its own line.
    Write-Host "[$state] $(ConvertTo-WorkflowCommandData -Text "$($result.Rule): $($result.Message)")"
}

foreach ($failure in $failures) {
    Write-Host "::error title=PR policy::$(ConvertTo-WorkflowCommandData -Text "$($failure.Rule): $($failure.Message)")"
}

if ($failures.Count -gt 0) {
    exit 1
}

exit 0
