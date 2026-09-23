#Requires -Version 7.0
<#
.SYNOPSIS
    Applies the repository settings of eng/github (merge policy, security features, labels, rulesets, the nuget
    environment, Actions permissions) to GitHub, idempotently. Run by a repository administrator, never by CI.

.DESCRIPTION
    Every setting lives in a JSON payload next to this script, with a _comment that explains it (eng/github/README.md).
    For each step of Get-RepositorySettingsPlan (RepositorySettings.psm1) the script reads the live value, compares it
    with the payload (the payload is a subset: fields it does not name are left alone), and writes only when they
    differ. A second run changes nothing.

    Modes, in the order a maintainer uses them:
      -PlanOnly   prints every step, endpoint and body. No network access, no gh call. Safe anywhere.
      -WhatIf     reads the live settings and prints each difference and the unmanaged live fields; writes nothing.
      (default)   applies the differences, asking for confirmation of each write (ConfirmImpact High; pass
                  -Confirm:$false to apply without prompts), then compares every step again and reports what remains.

    Order of the steps: repository settings; Dependabot alerts, Dependabot security updates, private vulnerability
    reporting; labels; the 'Protect main' and 'Protect release tags' rulesets (matched by name); the nuget environment
    (reviewers resolved from -NuGetReviewer) and its v*.*.* tag policy; Actions permissions; default workflow token
    permissions; immutable releases (only with -EnableImmutableReleases). It then checks that code scanning default
    setup is off (advanced CodeQL uploads are rejected while it is on) and never changes it. It never touches automatic
    dependency submission, which needs an organization-level setting.

.PARAMETER Repository
    owner/name.

.PARAMETER NuGetReviewer
    GitHub logins that approve the publish job (environment nuget). Resolved to user ids at run time.

.PARAMETER SkipRequiredChecks
    Leave the required status checks of 'Protect main' as they are (none on a first application). Require CI / Gate
    and PR policy only once main is green and the PR policy check has reported at least once, or every pull request
    is blocked.

.PARAMETER EnableImmutableReleases
    Also enable immutable releases. Only after the draft-first release.yml is on main: an immutable release cannot
    receive assets after publication.

.PARAMETER PlanOnly
    Print the plan without any network access.

.EXAMPLE
    ./eng/github/Set-RepositorySettings.ps1 -PlanOnly

.EXAMPLE
    ./eng/github/Set-RepositorySettings.ps1 -SkipRequiredChecks -WhatIf

.EXAMPLE
    ./eng/github/Set-RepositorySettings.ps1 -NuGetReviewer AriusII -Confirm:$false
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string] $Repository = 'CheatEngineNet/CheatEngine.SDK',

    [ValidateNotNullOrEmpty()]
    [ValidatePattern('^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})$')]
    [string[]] $NuGetReviewer = @('AriusII'),

    [switch] $SkipRequiredChecks,

    [switch] $EnableImmutableReleases,

    [switch] $PlanOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Settings are applied by an administrator at a terminal. A workflow token must never be able to reach this code.
if ($env:CI -eq 'true' -or $env:GITHUB_ACTIONS) {
    throw 'Set-RepositorySettings.ps1 refuses to run in CI: repository settings are applied by an administrator (eng/github/README.md).'
}

Import-Module -Name (Join-Path -Path $PSScriptRoot -ChildPath 'RepositorySettings.psm1') -Force

$plan = @(Get-RepositorySettingsPlan -PayloadDirectory $PSScriptRoot -Repository $Repository `
        -SkipRequiredChecks:$SkipRequiredChecks -EnableImmutableReleases:$EnableImmutableReleases)
$base = "repos/$Repository"

function Format-Body {
    param([AllowNull()] [object] $Body)

    return ((ConvertTo-Json -InputObject $Body -Depth 12) -split "`r?`n" | ForEach-Object { "      $_" }) -join [Environment]::NewLine
}

if ($PlanOnly) {
    Write-Host "Plan for $Repository (-PlanOnly: nothing is read from or sent to GitHub)."
    Write-Host '-WhatIf compares each step with the live settings; a run without it applies the differences.'
    Write-Host ''
    $number = 0
    foreach ($step in $plan) {
        $number++
        Write-Host "[$number] $($step.Id): $($step.Description)"
        Write-Host "    GET $($step.ReadEndpoint) -> compare -> $($step.WriteMethod) $($step.WriteEndpoint) when it differs"
        switch ($step.Kind) {
            'Toggle' {
                Write-Host '    enable (no body)'
            }
            'Environment' {
                Write-Host (Format-Body -Body $step.Desired)
                Write-Host "    reviewers: users $($NuGetReviewer -join ', '), resolved to ids at run time (GET users/<login>)"
                foreach ($policy in $step.BranchPolicies) {
                    Write-Host "    deployment branch policy: $($policy['type']) '$($policy['name'])' (POST when missing)"
                }
            }
            'Ruleset' {
                Write-Host (Format-Body -Body $step.Desired)
                if ($step.KeepLiveRequiredChecks) {
                    Write-Host '    required status checks: left as they are live (-SkipRequiredChecks)'
                }
            }
            default {
                Write-Host (Format-Body -Body $step.Desired)
            }
        }
    }

    Write-Host "[check] GET $base/code-scanning/default-setup: must stay not-configured (never changed)"
    Write-Host '[verify] every step is compared again after the writes'
    return
}

$apiVersionHeader = 'X-GitHub-Api-Version: 2022-11-28'

# One REST call through gh. --include prints the status line and headers before the body, so the status is read from
# standard output (a 404 is an answer for an optional resource, not an error), independent of gh's message language.
function Invoke-GitHubApi {
    param(
        [ValidateSet('GET', 'POST', 'PUT', 'PATCH')] [string] $Method = 'GET',
        [Parameter(Mandatory)] [string] $Endpoint,
        [AllowNull()] [object] $Body,
        [switch] $AllowNotFound
    )

    $arguments = @('api', '--include', '--method', $Method, '-H', 'Accept: application/vnd.github+json', '-H', $apiVersionHeader, $Endpoint)
    $bodyFile = $null
    if ($null -ne $Body) {
        $bodyFile = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), "repository-settings-$([guid]::NewGuid().ToString('N')).json")
        [System.IO.File]::WriteAllText($bodyFile, (ConvertTo-Json -InputObject $Body -Depth 12), [System.Text.UTF8Encoding]::new($false))
        $arguments += @('--input', $bodyFile)
    }

    try {
        $lines = @(& gh @arguments 2>$null | ForEach-Object { [string] $_ })
        $exitCode = $LASTEXITCODE
    }
    finally {
        if ($bodyFile) {
            Remove-Item -LiteralPath $bodyFile -Force -ErrorAction SilentlyContinue
        }
    }

    $status = if ($lines.Count -gt 0 -and $lines[0] -match '^HTTP/\S+\s+(?<status>\d{3})') { [int] $Matches['status'] } else { 0 }
    if ($status -eq 0) {
        throw "gh api --method $Method $Endpoint returned no HTTP status (exit code $exitCode). Check 'gh auth status'."
    }

    $separator = [Array]::IndexOf($lines, '')
    $text = if ($separator -ge 0 -and $separator -lt $lines.Count - 1) { ($lines[($separator + 1)..($lines.Count - 1)] -join "`n").Trim() } else { '' }
    if ($status -eq 404 -and $AllowNotFound) {
        return [pscustomobject]@{ Status = 404; Body = $null }
    }

    if ($exitCode -ne 0 -or $status -ge 400) {
        throw "gh api --method $Method $Endpoint failed with HTTP $status (exit code $exitCode): $text"
    }

    # A plain assignment: an if-expression would unroll a one-element JSON array into its element.
    $parsed = $null
    if ($text) {
        $parsed = $text | ConvertFrom-Json -AsHashtable -NoEnumerate
    }

    return [pscustomobject]@{ Status = $status; Body = $parsed }
}

& gh auth status *> $null
if ($LASTEXITCODE -ne 0) {
    throw "gh is not signed in (gh auth status exited with code $LASTEXITCODE). Run 'gh auth login' with an administrator of $Repository."
}

$repositoryState = (Invoke-GitHubApi -Endpoint $base).Body
if (-not ($repositoryState -is [System.Collections.IDictionary] -and $repositoryState['permissions'] -is [System.Collections.IDictionary] -and
        $repositoryState['permissions']['admin'] -eq $true)) {
    throw "The signed-in gh account is not an administrator of $Repository."
}

$reviewers = @(foreach ($login in $NuGetReviewer) {
        $user = (Invoke-GitHubApi -Endpoint "users/$login").Body
        [ordered]@{ type = 'User'; id = $user['id'] }
    })

# Returns the live value of a step in the shape of its payload, or $null when the resource does not exist.
function Get-LiveState {
    param([Parameter(Mandatory)] [pscustomobject] $Step)

    switch ($Step.Kind) {
        'Object' {
            return (Invoke-GitHubApi -Endpoint $Step.ReadEndpoint).Body
        }
        'Toggle' {
            $response = Invoke-GitHubApi -Endpoint $Step.ReadEndpoint -AllowNotFound
            if ($response.Status -eq 404) { return $false }
            # vulnerability-alerts answers 204 when enabled; the others answer { "enabled": ... }.
            if ($null -eq $response.Body) { return $true }
            return [bool] $response.Body['enabled']
        }
        'Label' {
            return (Invoke-GitHubApi -Endpoint $Step.ReadEndpoint -AllowNotFound).Body
        }
        'Ruleset' {
            $list = @((Invoke-GitHubApi -Endpoint $Step.ReadEndpoint).Body)
            $match = @($list | Where-Object { $_['name'] -ceq $Step.Desired['name'] -and $_['target'] -ceq $Step.Desired['target'] })
            if ($match.Count -gt 1) {
                throw "Several $($Step.Desired['target']) rulesets are named '$($Step.Desired['name'])'; delete the duplicates first."
            }

            if ($match.Count -eq 0) { return $null }
            return (Invoke-GitHubApi -Endpoint "$($Step.ReadEndpoint)/$($match[0]['id'])").Body
        }
        'Environment' {
            $response = Invoke-GitHubApi -Endpoint $Step.ReadEndpoint -AllowNotFound
            if ($response.Status -eq 404) { return $null }
            return ConvertFrom-EnvironmentResponse -Response $response.Body
        }
    }
}

function Get-DesiredState {
    param(
        [Parameter(Mandatory)] [pscustomobject] $Step,
        [AllowNull()] [object] $Live
    )

    switch ($Step.Kind) {
        'Environment' {
            $desired = [ordered]@{}
            foreach ($key in $Step.Desired.Keys) { $desired[$key] = $Step.Desired[$key] }
            $desired['reviewers'] = $reviewers
            return $desired
        }
        'Ruleset' {
            if (-not $Step.KeepLiveRequiredChecks -or $null -eq $Live) { return $Step.Desired }
            $desired = [ordered]@{}
            foreach ($key in $Step.Desired.Keys) { $desired[$key] = $Step.Desired[$key] }
            $desired['rules'] = @($Step.Desired['rules']) + @($Live['rules'] | Where-Object { $_['type'] -ceq 'required_status_checks' })
            return $desired
        }
        default {
            return $Step.Desired
        }
    }
}

function Get-StepDifference {
    param(
        [Parameter(Mandatory)] [pscustomobject] $Step,
        [AllowNull()] [object] $Live,
        [AllowNull()] [object] $Desired
    )

    if ($Step.Kind -ceq 'Toggle') {
        return @(if (-not $Live) { "$($Step.Id): disabled" })
    }

    if ($null -eq $Live) {
        return @("$($Step.Id): does not exist")
    }

    return @(Compare-SettingsObject -Desired $Desired -Actual $Live -Path $Step.Id)
}

function Invoke-StepWrite {
    param(
        [Parameter(Mandatory)] [pscustomobject] $Step,
        [AllowNull()] [object] $Live,
        [AllowNull()] [object] $Desired
    )

    switch ($Step.Kind) {
        'Toggle' {
            [void] (Invoke-GitHubApi -Method PUT -Endpoint $Step.WriteEndpoint)
        }
        'Object' {
            [void] (Invoke-GitHubApi -Method $Step.WriteMethod -Endpoint $Step.WriteEndpoint -Body $Desired)
        }
        'Label' {
            if ($null -eq $Live) {
                [void] (Invoke-GitHubApi -Method POST -Endpoint $Step.WriteEndpoint -Body $Desired)
            }
            else {
                $patch = [ordered]@{ color = $Desired['color']; description = $Desired['description'] }
                [void] (Invoke-GitHubApi -Method PATCH -Endpoint $Step.ReadEndpoint -Body $patch)
            }
        }
        'Ruleset' {
            if ($null -eq $Live) {
                [void] (Invoke-GitHubApi -Method POST -Endpoint $Step.WriteEndpoint -Body $Desired)
            }
            else {
                [void] (Invoke-GitHubApi -Method PUT -Endpoint "$($Step.WriteEndpoint)/$($Live['id'])" -Body $Desired)
            }
        }
        'Environment' {
            [void] (Invoke-GitHubApi -Method PUT -Endpoint $Step.WriteEndpoint -Body $Desired)
        }
    }
}

$results = [System.Collections.Generic.List[object]]::new()
foreach ($step in $plan) {
    $live = Get-LiveState -Step $step
    $desired = Get-DesiredState -Step $step -Live $live
    $differences = @(Get-StepDifference -Step $step -Live $live -Desired $desired)
    # Rulesets and the environment are replaced as a whole by a PUT: show what GitHub keeps or defaults there.
    if ($WhatIfPreference -and $step.Kind -cin @('Ruleset', 'Environment') -and $null -ne $live) {
        foreach ($field in @(Get-UnmanagedSetting -Desired $desired -Actual $live -Path $step.Id)) {
            Write-Host "  unmanaged (left as is): $field"
        }
    }

    $outcome = 'in sync'
    if ($differences.Count -gt 0) {
        $differences | ForEach-Object { Write-Host "  differs: $_" }
        $outcome = 'differs'
        if ($PSCmdlet.ShouldProcess("$Repository $($step.Id)", "$($step.WriteMethod) $($step.WriteEndpoint)")) {
            Invoke-StepWrite -Step $step -Live $live -Desired $desired
            $outcome = 'applied'
        }
    }

    # The tag policy needs the environment: check it when the environment exists or was just created.
    if ($step.Kind -ceq 'Environment' -and ($null -ne $live -or $outcome -ceq 'applied')) {
        $policiesEndpoint = "$($step.WriteEndpoint)/deployment-branch-policies"
        $existing = @((Invoke-GitHubApi -Endpoint $policiesEndpoint).Body['branch_policies'])
        foreach ($policy in $step.BranchPolicies) {
            $present = @($existing | Where-Object { $_['name'] -ceq $policy['name'] -and $_['type'] -ceq $policy['type'] }).Count -gt 0
            if (-not $present) {
                Write-Host "  differs: $($step.Id).deployment_branch_policies[$($policy['name'])]: missing"
                if ($PSCmdlet.ShouldProcess("$Repository $($step.Id)", "POST $policiesEndpoint $($policy['type']) $($policy['name'])")) {
                    [void] (Invoke-GitHubApi -Method POST -Endpoint $policiesEndpoint -Body $policy)
                    $outcome = 'applied'
                }
            }
        }
    }

    Write-Host "[$outcome] $($step.Id): $($step.Description)"
    $results.Add([pscustomobject]@{ Step = $step; Outcome = $outcome })
}

$defaultSetup = (Invoke-GitHubApi -Endpoint "$base/code-scanning/default-setup" -AllowNotFound).Body
if ($defaultSetup -is [System.Collections.IDictionary] -and $defaultSetup['state'] -ceq 'configured') {
    Write-Warning 'Code scanning default setup is ON: GitHub rejects the advanced CodeQL uploads of .github/workflows/codeql.yml. Switch it off in Settings > Code security (this script never changes it).'
}

if ($WhatIfPreference) {
    Write-Host 'WhatIf: nothing was written.'
    return
}

# Verify: a second comparison must find no difference, except those the maintainer declined or GitHub refused.
$remaining = [System.Collections.Generic.List[string]]::new()
foreach ($step in $plan) {
    $live = Get-LiveState -Step $step
    $remaining.AddRange([string[]] @(Get-StepDifference -Step $step -Live $live -Desired (Get-DesiredState -Step $step -Live $live)))
}

if ($remaining.Count -eq 0) {
    Write-Host "Every setting of $Repository matches eng/github."
    return
}

$remaining | ForEach-Object { Write-Host "  still differs: $_" }
if (@($remaining | Where-Object { $_ -like '*can_admins_bypass*' }).Count -gt 0) {
    Write-Warning "GitHub kept 'Allow administrators to bypass configured protection rules' on for the nuget environment: turn it off in Settings > Environments > nuget."
}

Write-Warning "$($remaining.Count) setting(s) still differ from eng/github (declined confirmations or refused by GitHub)."
