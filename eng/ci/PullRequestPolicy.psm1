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
                                 unless the description waives it: the marker <!-- changelog: not-needed --> on a line
                                 of its own, outside fenced code and outside another HTML comment.

    The waiver counts only as a bare line because the pull request template and CONTRIBUTING.md quote the marker to
    explain it: text that merely mentions the marker (in a code span, a code block, a sentence or a longer comment) must
    never waive the rule, or every description that keeps the template text would. In CommonMark a line that starts
    with "<!--" (after at most three spaces) begins an HTML block, even inside a paragraph, so a bare marker line is a
    real, invisible comment and never part of a code span.
    https://spec.commonmark.org/0.31.2/#html-blocks

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
# Verb prefixes that keep an allowlisted verb imperative (Refocus, Reseed, Unembed, Overfeed, Restring). "Co" is left
# out on purpose: "Coshed" is a past tense.
$ImperativeVerbPrefixes = @('Re', 'Un', 'De', 'Pre', 'Mis', 'Out', 'Over', 'Under', 'Up')
$MaximumListedPaths = 10
$RegexTimeout = [TimeSpan]::FromSeconds(1)
$ExemptionMessage = 'Dependabot pull request: title and changelog rules exempt.'

# The waiver marker alone on a line, after at most three spaces (four would make an indented code block).
$ChangelogWaiverLinePattern = '^ {0,3}' + $ChangelogWaiverPattern + '[ \t]*$'
# A fence opens or closes a fenced code block; a backtick fence whose info string holds a backtick is a code span.
$CodeFencePattern = '^ {0,3}(?<fence>`{3,}|~{3,})(?<info>.*)$'
$CodeSpanPattern = '(?<!`)(?<ticks>`+)(?!`).*?(?<!`)\k<ticks>(?!`)'

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

        foreach ($prefix in $ImperativeVerbPrefixes) {
            if ($Word.Length -eq $prefix.Length + $allowed.Length -and
                $Word.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -and
                $Word.EndsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) {
                return $null
            }
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

# True when the text leaves an HTML comment open at its end: comments do not nest, so the last "<!--" decides. Code
# spans are removed first, so a quoted "<!--" opens nothing.
function Test-OpenHtmlComment {
    param(
        [AllowEmptyString()] [Parameter(Mandatory)] [string] $Text
    )

    $prose = [regex]::Replace($Text, $CodeSpanPattern, ' ', [System.Text.RegularExpressions.RegexOptions]::CultureInvariant, $RegexTimeout)
    $open = $prose.LastIndexOf('<!--', [StringComparison]::Ordinal)
    return $open -ge 0 -and $prose.IndexOf('-->', $open + 4, [StringComparison]::Ordinal) -lt 0
}

# True when the description waives the CHANGELOG rule: the marker on a line of its own that is neither inside a fenced
# code block nor inside a longer HTML comment. A quoted marker (code span, code block, sentence) never waives it.
function Test-ChangelogWaiver {
    param(
        [AllowEmptyString()] [Parameter(Mandatory)] [string] $Body
    )

    $fence = ''
    $inComment = $false
    foreach ($line in [regex]::Split($Body, '\r\n|\r|\n', [System.Text.RegularExpressions.RegexOptions]::None, $RegexTimeout)) {
        if ($inComment) {
            # The line that closes a comment belongs to it, even when it looks like the marker.
            $close = $line.IndexOf('-->', [StringComparison]::Ordinal)
            if ($close -ge 0) {
                $inComment = Test-OpenHtmlComment -Text $line.Substring($close + 3)
            }

            continue
        }

        $fenceMatch = [regex]::Match($line, $CodeFencePattern, [System.Text.RegularExpressions.RegexOptions]::CultureInvariant, $RegexTimeout)
        if ($fence) {
            # A closing fence repeats the opening character at least as many times and carries no info string.
            if ($fenceMatch.Success -and [string]::IsNullOrWhiteSpace($fenceMatch.Groups['info'].Value) -and
                $fenceMatch.Groups['fence'].Value[0] -ceq $fence[0] -and $fenceMatch.Groups['fence'].Value.Length -ge $fence.Length) {
                $fence = ''
            }

            continue
        }

        if ($fenceMatch.Success -and -not ($fenceMatch.Groups['fence'].Value[0] -ceq [char] '`' -and
                $fenceMatch.Groups['info'].Value.Contains([char] '`'))) {
            $fence = $fenceMatch.Groups['fence'].Value
            continue
        }

        if (Test-RegexMatch -InputText $line -Pattern $ChangelogWaiverLinePattern) {
            return $true
        }

        $inComment = Test-OpenHtmlComment -Text $line
    }

    return $false
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

    if (Test-ChangelogWaiver -Body $Body) {
        return ConvertTo-RuleResult -Rule 'ChangelogEntry' -Passed $true -Message 'The description waives the CHANGELOG entry (<!-- changelog: not-needed -->).'
    }

    $listed = @($visible | Select-Object -First $MaximumListedPaths | ForEach-Object { "``$_``" })
    $more = if ($visible.Count -gt $MaximumListedPaths) { " and $($visible.Count - $MaximumListedPaths) more" } else { '' }
    $quoted = if (Test-RegexMatch -InputText $Body -Pattern $ChangelogWaiverPattern) {
        ' The description mentions the marker only inside code, inside another comment or within a line of text, which does not waive the rule.'
    }
    else {
        ''
    }

    return ConvertTo-RuleResult -Rule 'ChangelogEntry' -Passed $false -Message (
        "Consumer-visible paths changed without $ChangelogFile ($($listed -join ', ')$more). " +
        "Add an entry under '## [Unreleased]' in $ChangelogFile, or, when no consumer can observe the change, " +
        'put <!-- changelog: not-needed --> on a line of its own in the pull request description, with the reason.' + $quoted)
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
    $exemption = Get-PullRequestPolicyExemption -Author $Author
    if ($exemption) {
        foreach ($rule in $rules) {
            ConvertTo-RuleResult -Rule $rule -Passed $true -Message $exemption
        }

        return
    }

    $files = @($ChangedFile | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    Test-PullRequestTitle -Title ([string] $Title)
    Test-ChangelogEntry -Body ([string] $Body) -ChangedFile $files
}

<#
.SYNOPSIS
    Returns why a pull request of this author is exempt from every rule (Dependabot, compared with the exact login), or
    $null when it is not.
#>
function Get-PullRequestPolicyExemption {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [AllowEmptyString()] [AllowNull()] [string] $Author = ''
    )

    if ([string]::Equals($Author, $DependabotLogin, [StringComparison]::Ordinal)) {
        return $ExemptionMessage
    }

    return $null
}

Export-ModuleMember -Function Test-PullRequestPolicy, Get-PullRequestPolicyExemption
