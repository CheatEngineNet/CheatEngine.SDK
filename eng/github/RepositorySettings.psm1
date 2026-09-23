#Requires -Version 7.0
<#
.SYNOPSIS
    Pure planning and comparison rules of eng/github/Set-RepositorySettings.ps1.

.DESCRIPTION
    No function here calls GitHub, git or a process, or reads the environment: they turn the JSON payloads of eng/github
    into an ordered plan, and compare a desired payload with a live GitHub response. The script does the calls.
    tests/CheatEngine.SDK.Repository.Tests/Governance (RepositorySettingsScriptTests) runs these functions offline.

    Functions:
      Read-SettingsPayload              a payload file without its _-prefixed documentation keys
      Get-RepositorySettingsPlan        the ordered steps the script applies
      Compare-SettingsObject            the differences between a desired payload and a live value (desired is a subset)
      Get-UnmanagedSetting              live fields the payload does not manage (informational)
      ConvertFrom-EnvironmentResponse   a GET environment response in the shape of the PUT body
#>

Set-StrictMode -Version Latest

# Identity keys of array elements, after the composite ones (bypass actors by actor type and id, reviewers by type and
# id): rules by type, status checks by context, labels and deployment branch policies by name.
$IdentityKeys = @('type', 'context', 'name')

<#
.SYNOPSIS
    Reads a payload file as an ordered hashtable and drops its top-level keys that start with '_' (_comment): they
    document the choice and are never sent to GitHub.
#>
function Read-SettingsPayload {
    [CmdletBinding()]
    [OutputType([System.Collections.IDictionary])]
    param(
        [Parameter(Mandatory)] [string] $Path
    )

    $payload = Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json -AsHashtable
    if ($payload -isnot [System.Collections.IDictionary]) {
        throw "The settings payload '$Path' must be a JSON object."
    }

    foreach ($key in @($payload.Keys)) {
        if (([string] $key).StartsWith('_', [StringComparison]::Ordinal)) {
            $payload.Remove($key)
        }
    }

    return , $payload
}

function Read-PlanPayload {
    param(
        [Parameter(Mandatory)] [string] $Directory,
        [Parameter(Mandatory)] [string] $Name
    )

    return Read-SettingsPayload -Path (Join-Path -Path $Directory -ChildPath $Name)
}

<#
.SYNOPSIS
    Returns the ordered steps of the settings script. Each step has an Id, a Kind (Object, Toggle, Label, Ruleset,
    Environment), the endpoints and method, the desired value and a description.

.DESCRIPTION
    Order: repository settings; Dependabot alerts, Dependabot security updates and private vulnerability reporting;
    labels; the main and release-tag rulesets; the nuget environment; Actions permissions and the default token
    permissions; immutable releases (only with -EnableImmutableReleases, once the draft-first release workflow is on main).
    With -SkipRequiredChecks the main ruleset leaves its required status checks as they are (none on a first application):
    require them only after main is green and the PR policy check has reported at least once, otherwise every pull
    request is blocked.
#>
function Get-RepositorySettingsPlan {
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)] [string] $PayloadDirectory,
        [Parameter(Mandatory)] [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')] [string] $Repository,
        [switch] $SkipRequiredChecks,
        [switch] $EnableImmutableReleases
    )

    $base = "repos/$Repository"

    [pscustomobject]@{
        Id = 'repository'; Kind = 'Object'; ReadEndpoint = $base; WriteMethod = 'PATCH'; WriteEndpoint = $base
        Desired = Read-PlanPayload -Directory $PayloadDirectory -Name 'repository.json'
        Description = 'Merge methods, squash commit title, branch clean-up, issues, secret scanning and push protection'
    }

    foreach ($toggle in @(
            @('vulnerability-alerts', 'Dependabot alerts'),
            @('automated-security-fixes', 'Dependabot security updates'),
            @('private-vulnerability-reporting', 'Private vulnerability reporting'))) {
        [pscustomobject]@{
            Id = $toggle[0]; Kind = 'Toggle'; ReadEndpoint = "$base/$($toggle[0])"; WriteMethod = 'PUT'
            WriteEndpoint = "$base/$($toggle[0])"; Desired = $true; Description = $toggle[1]
        }
    }

    foreach ($label in @((Read-PlanPayload -Directory $PayloadDirectory -Name 'labels.json')['labels'])) {
        [pscustomobject]@{
            Id = "label:$($label['name'])"; Kind = 'Label'; ReadEndpoint = "$base/labels/$([Uri]::EscapeDataString($label['name']))"
            WriteMethod = 'PATCH or POST'; WriteEndpoint = "$base/labels"; Desired = $label
            Description = "Label '$($label['name'])'"
        }
    }

    foreach ($file in @('protect-main.json', 'protect-release-tags.json')) {
        $ruleset = Read-PlanPayload -Directory $PayloadDirectory -Name "rulesets/$file"
        if ($SkipRequiredChecks) {
            $ruleset['rules'] = @($ruleset['rules'] | Where-Object { $_['type'] -cne 'required_status_checks' })
        }

        [pscustomobject]@{
            Id = "ruleset:$($ruleset['name'])"; Kind = 'Ruleset'; ReadEndpoint = "$base/rulesets"; WriteMethod = 'PUT or POST'
            WriteEndpoint = "$base/rulesets"; Desired = $ruleset; KeepLiveRequiredChecks = [bool] $SkipRequiredChecks
            Description = "Ruleset '$($ruleset['name'])' ($($ruleset['target']))"
        }
    }

    $environment = Read-PlanPayload -Directory $PayloadDirectory -Name 'environments/nuget.json'
    [pscustomobject]@{
        Id = "environment:$($environment['name'])"; Kind = 'Environment'
        ReadEndpoint = "$base/environments/$($environment['name'])"; WriteMethod = 'PUT'
        WriteEndpoint = "$base/environments/$($environment['name'])"; Desired = $environment['settings']
        BranchPolicies = @($environment['deployment_branch_policies'])
        Description = "Environment '$($environment['name'])': reviewers, admin bypass, tag deployment policy"
    }

    [pscustomobject]@{
        Id = 'actions-permissions'; Kind = 'Object'; ReadEndpoint = "$base/actions/permissions"; WriteMethod = 'PUT'
        WriteEndpoint = "$base/actions/permissions"; Desired = Read-PlanPayload -Directory $PayloadDirectory -Name 'actions-permissions.json'
        Description = 'GitHub Actions: enabled, actions pinned to a full commit SHA'
    }

    [pscustomobject]@{
        Id = 'actions-workflow-permissions'; Kind = 'Object'; ReadEndpoint = "$base/actions/permissions/workflow"; WriteMethod = 'PUT'
        WriteEndpoint = "$base/actions/permissions/workflow"
        Desired = Read-PlanPayload -Directory $PayloadDirectory -Name 'actions-workflow-permissions.json'
        Description = 'Default GITHUB_TOKEN permissions: read-only, no pull request approval'
    }

    if ($EnableImmutableReleases) {
        [pscustomobject]@{
            Id = 'immutable-releases'; Kind = 'Toggle'; ReadEndpoint = "$base/immutable-releases"; WriteMethod = 'PUT'
            WriteEndpoint = "$base/immutable-releases"; Desired = $true; Description = 'Immutable releases'
        }
    }
}

function Test-Scalar {
    param([AllowNull()] [object] $Value)

    return $null -eq $Value -or $Value -is [string] -or $Value -is [bool] -or $Value -is [ValueType]
}

function Get-ElementIdentity {
    param([AllowNull()] [object] $Element)

    if ($Element -isnot [System.Collections.IDictionary]) {
        return $null
    }

    # Bypass actors and environment reviewers are identified by their type and id together.
    if ($Element.Contains('actor_type')) {
        return "$($Element['actor_type'])#$($Element['actor_id'])"
    }

    if ($Element.Contains('type') -and $Element.Contains('id')) {
        return "$($Element['type'])#$($Element['id'])"
    }

    foreach ($key in $IdentityKeys) {
        if ($Element.Contains($key)) {
            return [string] $Element[$key]
        }
    }

    return $null
}

function Format-SettingValue {
    param([AllowNull()] [object] $Value)

    if ($null -eq $Value) {
        return '(absent)'
    }

    if (Test-Scalar -Value $Value) {
        return (ConvertTo-Json -InputObject $Value -Compress)
    }

    return (ConvertTo-Json -InputObject $Value -Depth 10 -Compress)
}

<#
.SYNOPSIS
    Returns one line per difference between a desired payload and a live value, or nothing when the live value already
    satisfies the payload.

.DESCRIPTION
    The desired value is a subset: live fields it does not name are ignored (Get-UnmanagedSetting lists them).
    - scalars compare by JSON value (0 and false differ, "PR_TITLE" and "pr_title" differ);
    - arrays of scalars compare as sets (the order of merge methods or ref patterns carries no meaning);
    - arrays of objects match their elements by identity (actor type and id, type and id, type, context or name); a desired element
      missing from the live array, and a live element the payload does not list, are both differences, because a PUT
      replaces the whole array.
#>
function Compare-SettingsObject {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [AllowNull()] [object] $Desired,
        [AllowNull()] [object] $Actual,
        [string] $Path = '$'
    )

    if ($Desired -is [System.Collections.IDictionary]) {
        if ($Actual -isnot [System.Collections.IDictionary]) {
            return "${Path}: expected an object, found $(Format-SettingValue -Value $Actual)"
        }

        foreach ($key in $Desired.Keys) {
            # Plain assignments only: an if-expression would unroll a one-element array into its element.
            $live = $null
            if ($Actual.Contains($key)) {
                $live = $Actual[$key]
            }

            Compare-SettingsObject -Desired $Desired[$key] -Actual $live -Path "$Path.$key"
        }

        return
    }

    if ($Desired -is [System.Collections.IList]) {
        if ($null -ne $Actual -and $Actual -isnot [System.Collections.IList]) {
            return "${Path}: expected an array, found $(Format-SettingValue -Value $Actual)"
        }

        $desiredItems = @($Desired)
        $liveItems = @()
        if ($null -ne $Actual) {
            $liveItems = @($Actual)
        }

        $allScalars = @($desiredItems + $liveItems | Where-Object { -not (Test-Scalar -Value $_) }).Count -eq 0
        if ($allScalars) {
            $wanted = @($desiredItems | ForEach-Object { Format-SettingValue -Value $_ } | Sort-Object -CaseSensitive)
            $found = @($liveItems | ForEach-Object { Format-SettingValue -Value $_ } | Sort-Object -CaseSensitive)
            if (($wanted -join "`n") -cne ($found -join "`n")) {
                return "${Path}: desired [$($wanted -join ', ')], live [$($found -join ', ')]"
            }

            return
        }

        $liveByIdentity = [ordered]@{}
        foreach ($item in $liveItems) {
            $identity = Get-ElementIdentity -Element $item
            if ($null -ne $identity) {
                $liveByIdentity[$identity] = $item
            }
        }

        $desiredIdentities = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        foreach ($item in $desiredItems) {
            $identity = Get-ElementIdentity -Element $item
            if ($null -eq $identity) {
                throw "${Path}: every element of an object array needs one of $($IdentityKeys -join ', ')."
            }

            [void] $desiredIdentities.Add($identity)
            if (-not $liveByIdentity.Contains($identity)) {
                "${Path}[$identity]: missing"
                continue
            }

            Compare-SettingsObject -Desired $item -Actual $liveByIdentity[$identity] -Path "${Path}[$identity]"
        }

        foreach ($identity in $liveByIdentity.Keys) {
            if (-not $desiredIdentities.Contains($identity)) {
                "${Path}[$identity]: present live but not in the payload (a PUT removes it)"
            }
        }

        return
    }

    if ((Format-SettingValue -Value $Desired) -cne (Format-SettingValue -Value $Actual)) {
        return "${Path}: desired $(Format-SettingValue -Value $Desired), live $(Format-SettingValue -Value $Actual)"
    }
}

<#
.SYNOPSIS
    Lists the live fields, inside objects the payload manages, that the payload does not name: GitHub keeps them or
    applies its default on a write. Informational, printed by -WhatIf so a maintainer sees what the script leaves alone.
    Read-only response fields (ids, links, timestamps, counts) are not listed.
#>
function Get-UnmanagedSetting {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [AllowNull()] [object] $Desired,
        [AllowNull()] [object] $Actual,
        [string] $Path = '$',
        [string[]] $Ignore = @('id', 'node_id', 'url', 'html_url', '_links', 'created_at', 'updated_at', 'source', 'source_type',
            'current_user_can_bypass', 'full_name', 'owner', 'permissions')
    )

    if ($Desired -is [System.Collections.IDictionary] -and $Actual -is [System.Collections.IDictionary]) {
        foreach ($key in $Actual.Keys) {
            if (-not $Desired.Contains($key)) {
                if ($key -cnotin $Ignore) {
                    "$Path.$key = $(Format-SettingValue -Value $Actual[$key])"
                }

                continue
            }

            Get-UnmanagedSetting -Desired $Desired[$key] -Actual $Actual[$key] -Path "$Path.$key" -Ignore $Ignore
        }

        return
    }

    if ($Desired -is [System.Collections.IList] -and $Actual -is [System.Collections.IList]) {
        foreach ($item in @($Desired)) {
            $identity = Get-ElementIdentity -Element $item
            if ($null -eq $identity) {
                continue
            }

            foreach ($live in @($Actual)) {
                if ((Get-ElementIdentity -Element $live) -ceq $identity) {
                    Get-UnmanagedSetting -Desired $item -Actual $live -Path "${Path}[$identity]" -Ignore $Ignore
                }
            }
        }
    }
}

<#
.SYNOPSIS
    Converts a GET /repos/{owner}/{repo}/environments/{name} response into the shape of the PUT body: wait_timer,
    prevent_self_review and reviewers ({ type, id }) live inside protection_rules in the response.
#>
function ConvertFrom-EnvironmentResponse {
    [CmdletBinding()]
    [OutputType([System.Collections.IDictionary])]
    param(
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Response
    )

    $state = [ordered]@{
        wait_timer               = 0
        prevent_self_review      = $false
        can_admins_bypass        = if ($Response.Contains('can_admins_bypass')) { $Response['can_admins_bypass'] } else { $true }
        reviewers                = @()
        deployment_branch_policy = if ($Response.Contains('deployment_branch_policy')) { $Response['deployment_branch_policy'] } else { $null }
    }

    foreach ($rule in @($Response['protection_rules'] | Where-Object { $_ })) {
        switch ([string] $rule['type']) {
            'wait_timer' {
                $state['wait_timer'] = $rule['wait_timer']
            }
            'required_reviewers' {
                $state['prevent_self_review'] = [bool] $rule['prevent_self_review']
                $state['reviewers'] = @($rule['reviewers'] | Where-Object { $_ } | ForEach-Object {
                        [ordered]@{ type = [string] $_['type']; id = $_['reviewer']['id'] }
                    })
            }
        }
    }

    return , $state
}

Export-ModuleMember -Function Read-SettingsPayload, Get-RepositorySettingsPlan, Compare-SettingsObject, Get-UnmanagedSetting,
ConvertFrom-EnvironmentResponse
