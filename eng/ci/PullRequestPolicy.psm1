#Requires -Version 7.0
<#
.SYNOPSIS
    Pure rules of the "PR policy" required check: pull request title shape and CHANGELOG entry.

.DESCRIPTION
    Test-PullRequestPolicy evaluates a pull request from its title, body, author login and changed paths, and returns one
    result per rule. It reads no file, runs no git command and touches no environment variable, so the rules are tested
    offline by tests/CheatEngine.SDK.Repository.Tests/Governance (PullRequestPolicyScriptTests), and the entry point
    eng/ci/Test-PullRequestPolicy.ps1 only collects its inputs and reports.

    Rules (stable ids, asserted by the tests):
      TitleLength                1 to 72 text elements after trimming (the squash subject on main is the title).
      TitleNoTrailingPeriod      no final period.
      TitleNoConventionalPrefix  no "type:", "type(scope):" or "type!:" prefix, and no "Area:" prefix either.
      TitleStartsUppercase       the first character is an uppercase letter.
      TitleImperative            the first word reads as an imperative verb (heuristic with an allowlist).
      ChangelogEntry             a change under a consumer-visible path (lock files excluded) also changes CHANGELOG.md,
                                 unless the description contains the waiver marker <!-- changelog: not-needed -->.

    Pull requests opened by Dependabot pass every rule: their titles are generated and their lock-file changes under
    libs/ are not consumer-visible on their own.

    Every match is ordinal and case-sensitive (git paths are case-exact), with a regex timeout.
#>

Set-StrictMode -Version Latest

# Repository-specific constants. The CheatEngine.Client twin of this module changes only this block.
$ChangelogPathPattern = '^(libs|src|analyzers|source-generators|native)/'
$ChangelogExcludedPattern = '(^|/)packages\.lock\.json$'
$ChangelogFile = 'CHANGELOG.md'
$ChangelogWaiverPattern = '<!--\s*changelog:\s*not-needed\s*-->'
$DependabotLogin = 'dependabot[bot]'

# Shared rules (both repositories, CONTRIBUTING.md "Commits").
$MaximumTitleLength = 72
$ConventionalPrefixPattern = '^\w+(\([^)]*\))?!?:\s'
$NonImperativeFirstWords = @('WIP', 'Draft', 'Misc', 'Various', 'Minor')
# Imperative verbs that the suffix heuristic (-ed, -ing, third-person -s) would otherwise reject.
$ImperativeFirstWords = @(
    'Alias', 'Bias', 'Bleed', 'Breed', 'Bring', 'Canvas', 'Embed', 'Exceed', 'Feed', 'Focus', 'Heed', 'Need', 'Ping',
    'Proceed', 'Ring', 'Seed', 'Shed', 'Shred', 'Sing', 'Speed', 'Spring', 'Sting', 'String', 'Succeed', 'Swing', 'Wring'
)
$MaximumListedPaths = 10
$RegexTimeout = [TimeSpan]::FromSeconds(1)

function Test-RegexMatch {
    param(
        [AllowEmptyString()] [Parameter(Mandatory)] [string] $InputText,
        [Parameter(Mandatory)] [string] $Pattern
    )

    return [regex]::IsMatch($InputText, $Pattern, [System.Text.RegularExpressions.RegexOptions]::CultureInvariant, $RegexTimeout)
}

function ConvertTo-RuleResult {
    param(
        [Parameter(Mandatory)] [string] $Rule,
        [Parameter(Mandatory)] [bool] $Passed,
        [Parameter(Mandatory)] [string] $Message
    )

    return [pscustomobject]@{ Rule = $Rule; Passed = $Passed; Message = $Message }
}

function Get-FirstWord {
    param(
        [AllowEmptyString()] [Parameter(Mandatory)] [string] $Title
    )

    $word = ($Title -split '\s+', 2)[0]
    return $word.TrimEnd(':', ',', ';', '.', '!', '?')
}

function Test-ImperativeWord {
    param(
        [AllowEmptyString()] [Parameter(Mandatory)] [string] $Word
    )

    foreach ($denied in $NonImperativeFirstWords) {
        if ([string]::Equals($Word, $denied, [StringComparison]::OrdinalIgnoreCase)) {
            return "'$Word' does not say what the change does; start with an imperative verb such as Add, Fix or Remove."
        }
    }

    foreach ($allowed in $ImperativeFirstWords) {
        if ([string]::Equals($Word, $allowed, [StringComparison]::OrdinalIgnoreCase)) {
            return $null
        }
    }

    $lower = $Word.ToLowerInvariant()
    if ($lower.Length -gt 3 -and $lower.EndsWith('ed', [StringComparison]::Ordinal)) {
        return "'$Word' reads as past tense; use the imperative mood (for example 'Add', not 'Added')."
    }

    if ($lower.Length -gt 4 -and $lower.EndsWith('ing', [StringComparison]::Ordinal)) {
        return "'$Word' reads as a gerund; use the imperative mood (for example 'Add', not 'Adding')."
    }

    if ($lower.Length -gt 2 -and $lower[-1] -ceq 's' -and $lower[-2] -cne 's' -and [char]::IsLetter($lower[-2])) {
        return "'$Word' reads as third person; use the imperative mood (for example 'Add', not 'Adds')."
    }

    return $null
}

function Test-PullRequestTitle {
    param(
        [AllowEmptyString()] [Parameter(Mandatory)] [string] $Title
    )

    $trimmed = $Title.Trim()
    $length = [System.Globalization.StringInfo]::new($trimmed).LengthInTextElements
    $lengthOk = $length -ge 1 -and $length -le $MaximumTitleLength
    ConvertTo-RuleResult -Rule 'TitleLength' -Passed $lengthOk -Message $(
        if ($length -eq 0) { 'The title is empty.' }
        elseif ($lengthOk) { "$length characters (at most $MaximumTitleLength)." }
        else { "The title has $length characters; shorten it to at most $MaximumTitleLength (it becomes the commit subject on main)." }
    )

    if ($length -eq 0) {
        foreach ($rule in 'TitleNoTrailingPeriod', 'TitleNoConventionalPrefix', 'TitleStartsUppercase', 'TitleImperative') {
            ConvertTo-RuleResult -Rule $rule -Passed $true -Message 'Not evaluated: the title is empty (see TitleLength).'
        }

        return
    }

    $noPeriod = -not $trimmed.EndsWith('.', [StringComparison]::Ordinal)
    ConvertTo-RuleResult -Rule 'TitleNoTrailingPeriod' -Passed $noPeriod -Message $(
        if ($noPeriod) { 'No trailing period.' } else { 'Remove the trailing period.' }
    )

    $noPrefix = -not (Test-RegexMatch -InputText $trimmed -Pattern $ConventionalPrefixPattern)
    ConvertTo-RuleResult -Rule 'TitleNoConventionalPrefix' -Passed $noPrefix -Message $(
        if ($noPrefix) { 'No type or area prefix.' }
        else { "Remove the 'type:' or 'Area:' prefix; describe the change with an imperative sentence instead." }
    )

    $uppercase = Test-RegexMatch -InputText $trimmed -Pattern '^\p{Lu}'
    ConvertTo-RuleResult -Rule 'TitleStartsUppercase' -Passed $uppercase -Message $(
        if ($uppercase) { 'Starts with an uppercase letter.' } else { 'Start the title with an uppercase letter.' }
    )

    $problem = Test-ImperativeWord -Word (Get-FirstWord -Title $trimmed)
    ConvertTo-RuleResult -Rule 'TitleImperative' -Passed ($null -eq $problem) -Message $(
        if ($null -eq $problem) { 'The first word reads as an imperative verb.' } else { $problem }
    )
}

function Test-ChangelogEntry {
    param(
        [AllowEmptyString()] [Parameter(Mandatory)] [string] $Body,
        [AllowEmptyCollection()] [Parameter(Mandatory)] [string[]] $ChangedFile
    )

    $visible = [System.Collections.Generic.List[string]]::new()
    foreach ($path in $ChangedFile) {
        if ((Test-RegexMatch -InputText $path -Pattern $ChangelogPathPattern) -and
            -not (Test-RegexMatch -InputText $path -Pattern $ChangelogExcludedPattern)) {
            $visible.Add($path)
        }
    }

    if ($visible.Count -eq 0) {
        return ConvertTo-RuleResult -Rule 'ChangelogEntry' -Passed $true -Message 'No consumer-visible path changed.'
    }

    if ($ChangedFile -ccontains $ChangelogFile) {
        return ConvertTo-RuleResult -Rule 'ChangelogEntry' -Passed $true -Message "$ChangelogFile changes with the consumer-visible paths."
    }

    if (Test-RegexMatch -InputText $Body -Pattern $ChangelogWaiverPattern) {
        return ConvertTo-RuleResult -Rule 'ChangelogEntry' -Passed $true -Message 'The description waives the CHANGELOG entry (<!-- changelog: not-needed -->).'
    }

    $listed = @($visible | Select-Object -First $MaximumListedPaths | ForEach-Object { "``$_``" })
    $more = if ($visible.Count -gt $MaximumListedPaths) { " and $($visible.Count - $MaximumListedPaths) more" } else { '' }
    return ConvertTo-RuleResult -Rule 'ChangelogEntry' -Passed $false -Message (
        "Consumer-visible paths changed without $ChangelogFile ($($listed -join ', ')$more). " +
        "Add an entry under '## [Unreleased]' in $ChangelogFile, or, when no consumer can observe the change, " +
        'put <!-- changelog: not-needed --> in the pull request description with the reason.')
}

<#
.SYNOPSIS
    Evaluates every PR policy rule and returns one [pscustomobject] @{ Rule; Passed; Message } per rule, in a fixed order.
#>
function Test-PullRequestPolicy {
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param(
        [AllowEmptyString()] [AllowNull()] [string] $Title = '',
        [AllowEmptyString()] [AllowNull()] [string] $Body = '',
        [AllowEmptyString()] [AllowNull()] [string] $Author = '',
        [AllowEmptyCollection()] [AllowNull()] [string[]] $ChangedFile = @()
    )

    $rules = @('TitleLength', 'TitleNoTrailingPeriod', 'TitleNoConventionalPrefix', 'TitleStartsUppercase', 'TitleImperative',
        'ChangelogEntry')
    if ([string]::Equals($Author, $DependabotLogin, [StringComparison]::Ordinal)) {
        foreach ($rule in $rules) {
            ConvertTo-RuleResult -Rule $rule -Passed $true -Message 'Dependabot pull request: title and changelog rules exempt.'
        }

        return
    }

    $files = @($ChangedFile | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    Test-PullRequestTitle -Title ([string] $Title)
    Test-ChangelogEntry -Body ([string] $Body) -ChangedFile $files
}

Export-ModuleMember -Function Test-PullRequestPolicy
