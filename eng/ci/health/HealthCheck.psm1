#Requires -Version 7.0
<#
.SYNOPSIS
    Pure rules of the scheduled health workflow (.github/workflows/scheduled-health.yml).

.DESCRIPTION
    The health scripts next to this module do the side effects (git, the .NET CLI, xmake, HTTP, gh). Every decision they
    take lives here instead, as a function of its arguments only: no file, network, git, process or environment
    access. tests/CheatEngine.SDK.Repository.Tests/Governance (HealthCheckScriptTests) calls each function with its
    vectors, so the decisions are tested offline while the scripts themselves are exercised by local runs.

    Functions:
      Select-ReleaseTag                 newest v<major>.<minor>.<patch> release tag
      Get-BridgeDriftClassification     Reproduced, ToolchainDrift or Failed for a rebuilt release bridge
      Get-SdkChannel                    the release channel (major.minor) of an SDK version
      Get-NewestSdkVersion              latest-sdk of a release-metadata releases.json document
      Get-UpdatedGlobalJson             global.json text with only sdk.version replaced
      Test-PackageReference             whether an MSBuild file references a package
      Get-TrxSummary                    the counters of a TRX test report
      ConvertFrom-PackageListReport     rows of a `package list --format json` report (vulnerable or deprecated)
      Get-SolutionProjectPath           the project paths a .slnx solution lists
      Get-ExternalLinkTarget            the external http(s) links of a Markdown text
      Get-ExternalLinkVerdict           Ok, Broken or Inconclusive for an HTTP status
      Get-HealthIssueReport             whether the run needs attention, and the issue body
      Select-HealthIssue                the open health issue with the exact title
#>

Set-StrictMode -Version Latest

$RegexTimeout = [TimeSpan]::FromSeconds(1)
$ReleaseTagPattern = '^v(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)$'
$SdkVersionPattern = '^(?<major>[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<band>[1-9]\d{2})$'
$Sha256Pattern = '^[0-9a-f]{64}$'
$HealthIssueTitle = 'Scheduled health check needs attention'
$JobResults = @('success', 'failure', 'cancelled', 'skipped')

function Test-Match {
    param(
        [AllowEmptyString()] [Parameter(Mandatory)] [string] $InputText,
        [Parameter(Mandatory)] [string] $Pattern
    )

    return [regex]::IsMatch($InputText, $Pattern, [System.Text.RegularExpressions.RegexOptions]::CultureInvariant, $RegexTimeout)
}

<#
.SYNOPSIS
    Returns the newest release tag: v<major>.<minor>.<patch> without a prerelease or build suffix, compared as versions
    (v1.10.0 is newer than v1.9.0). Returns $null when no tag qualifies.
#>
function Select-ReleaseTag {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [AllowEmptyCollection()] [Parameter(Mandatory)] [string[]] $Tag
    )

    $best = $null
    $bestVersion = $null
    foreach ($candidate in $Tag) {
        $match = [regex]::Match($candidate, $ReleaseTagPattern, [System.Text.RegularExpressions.RegexOptions]::CultureInvariant, $RegexTimeout)
        if (-not $match.Success) {
            continue
        }

        $version = [version]::new([int] $match.Groups['major'].Value, [int] $match.Groups['minor'].Value, [int] $match.Groups['patch'].Value)
        if ($null -eq $bestVersion -or $version -gt $bestVersion) {
            $best = $candidate
            $bestVersion = $version
        }
    }

    return $best
}

<#
.SYNOPSIS
    Classifies a rebuild of a release tag's native bridge.

.DESCRIPTION
    Failed         the two rebuilds from different directories differ (the build depends on its path), the rebuilt DLL
                   does not export the fingerprint of the tag's sources, the released package could not be read, or
                   the DLL committed at the tag is not the one the package shipped;
    Reproduced     today's toolchain rebuilds the released bytes exactly;
    ToolchainDrift everything is consistent, but today's toolchain produces other bytes than the released DLL.
    Every SHA-256 is compared in lowercase hex; a malformed value counts as a mismatch.
#>
function Get-BridgeDriftClassification {
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)] [string] $RebuiltSha256,
        [Parameter(Mandatory)] [string] $SecondRebuiltSha256,
        [Parameter(Mandatory)] [string] $ExpectedFingerprint,
        [AllowEmptyString()] [AllowNull()] [string] $ExportedFingerprint,
        [Parameter(Mandatory)] [string] $CheckedInSha256,
        [AllowEmptyString()] [AllowNull()] [string] $ReleasedSha256
    )

    $reasons = [System.Collections.Generic.List[string]]::new()
    foreach ($pair in @(
            @('rebuilt bridge', $RebuiltSha256), @('second rebuilt bridge', $SecondRebuiltSha256),
            @('bridge committed at the tag', $CheckedInSha256))) {
        if (-not (Test-Match -InputText $pair[1] -Pattern $Sha256Pattern)) {
            $reasons.Add("The SHA-256 of the $($pair[0]) is not 64 lowercase hex digits ('$($pair[1])').")
        }
    }

    if ($RebuiltSha256 -cne $SecondRebuiltSha256) {
        $reasons.Add('The two rebuilds from different directories differ: the build depends on its path.')
    }

    if ([string]::IsNullOrEmpty($ExportedFingerprint) -or $ExportedFingerprint -cne $ExpectedFingerprint) {
        $reasons.Add("The rebuilt bridge exports the fingerprint '$ExportedFingerprint', not '$ExpectedFingerprint' of the tag's sources.")
    }

    if ([string]::IsNullOrEmpty($ReleasedSha256)) {
        $reasons.Add('The bridge of the released package could not be read.')
    }
    elseif (-not (Test-Match -InputText $ReleasedSha256 -Pattern $Sha256Pattern)) {
        $reasons.Add("The SHA-256 of the released bridge is not 64 lowercase hex digits ('$ReleasedSha256').")
    }
    elseif ($CheckedInSha256 -cne $ReleasedSha256) {
        $reasons.Add('The bridge committed at the tag is not the bridge the released package ships.')
    }

    if ($reasons.Count -gt 0) {
        return [pscustomobject]@{ Classification = 'Failed'; Reasons = [string[]] $reasons }
    }

    if ($RebuiltSha256 -ceq $ReleasedSha256) {
        return [pscustomobject]@{
            Classification = 'Reproduced'
            Reasons        = [string[]] @('Today''s toolchain rebuilds the released bridge byte for byte.')
        }
    }

    return [pscustomobject]@{
        Classification = 'ToolchainDrift'
        Reasons        = [string[]] @(
            'The sources match the release (same fingerprint), but today''s toolchain produces other bytes than the released bridge.')
    }
}

<#
.SYNOPSIS
    Returns the release channel (major.minor) of a full .NET SDK version such as 10.0.401; throws on anything else.
#>
function Get-SdkChannel {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)] [string] $Version
    )

    $match = [regex]::Match($Version, $SdkVersionPattern, [System.Text.RegularExpressions.RegexOptions]::CultureInvariant, $RegexTimeout)
    if (-not $match.Success) {
        throw "'$Version' is not a full .NET SDK version (major.minor.feature-band-and-patch, for example 10.0.401)."
    }

    return "$($match.Groups['major'].Value).$($match.Groups['minor'].Value)"
}

<#
.SYNOPSIS
    Returns latest-sdk of a release-metadata releases.json document, checked to belong to the expected channel.
    https://builds.dotnet.microsoft.com/dotnet/release-metadata/<channel>/releases.json
#>
function Get-NewestSdkVersion {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)] [string] $ReleasesJson,
        [Parameter(Mandatory)] [string] $Channel
    )

    $metadata = $ReleasesJson | ConvertFrom-Json -AsHashtable
    if ($metadata -isnot [System.Collections.IDictionary] -or -not $metadata.Contains('latest-sdk')) {
        throw 'The release metadata has no latest-sdk property.'
    }

    $latest = [string] $metadata['latest-sdk']
    if ((Get-SdkChannel -Version $latest) -cne $Channel) {
        throw "The release metadata names $latest as the latest SDK, which is not on the $Channel channel."
    }

    return $latest
}

<#
.SYNOPSIS
    Returns the global.json text with sdk.version set to -Version. Every other property (rollForward, allowPrerelease,
    errorMessage, the test runner) is kept, in order: without the test section `dotnet test` would fall back to VSTest.
#>
function Get-UpdatedGlobalJson {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)] [string] $Json,
        [Parameter(Mandatory)] [string] $Version
    )

    [void] (Get-SdkChannel -Version $Version)
    $document = $Json | ConvertFrom-Json -AsHashtable
    if ($document -isnot [System.Collections.IDictionary] -or $document['sdk'] -isnot [System.Collections.IDictionary] -or
        -not $document['sdk'].Contains('version')) {
        throw 'global.json has no sdk.version property.'
    }

    $document['sdk']['version'] = $Version
    return ($document | ConvertTo-Json -Depth 10)
}

<#
.SYNOPSIS
    True when an MSBuild file's text has a PackageReference (or PackageVersion) to exactly -PackageId.
#>
function Test-PackageReference {
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [AllowEmptyString()] [Parameter(Mandatory)] [string] $ProjectText,
        [Parameter(Mandatory)] [string] $PackageId
    )

    $pattern = '<Package(Reference|Version)\s+Include="' + [regex]::Escape($PackageId) + '"'
    return Test-Match -InputText $ProjectText -Pattern $pattern
}

<#
.SYNOPSIS
    Returns the ResultSummary counters of a TRX report (total, executed, passed, failed, notExecuted) and its outcome.
#>
function Get-TrxSummary {
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)] [string] $Xml
    )

    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $document = [System.Xml.XmlDocument]::new()
    $reader = [System.Xml.XmlReader]::Create([System.IO.StringReader]::new($Xml), $settings)
    try {
        $document.Load($reader)
    }
    finally {
        $reader.Dispose()
    }

    $namespaces = [System.Xml.XmlNamespaceManager]::new($document.NameTable)
    $namespaces.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
    $summary = $document.SelectSingleNode('/t:TestRun/t:ResultSummary', $namespaces)
    $counters = $document.SelectSingleNode('/t:TestRun/t:ResultSummary/t:Counters', $namespaces)
    if ($null -eq $summary -or $null -eq $counters) {
        throw 'The TRX report has no ResultSummary/Counters element.'
    }

    $read = {
        param([string] $name)
        $value = $counters.GetAttribute($name)
        if ([string]::IsNullOrEmpty($value)) { return 0 }
        return [int]::Parse($value, [System.Globalization.CultureInfo]::InvariantCulture)
    }

    return [pscustomobject]@{
        Outcome     = $summary.GetAttribute('outcome')
        Total       = & $read 'total'
        Executed    = & $read 'executed'
        Passed      = & $read 'passed'
        Failed      = & $read 'failed'
        NotExecuted = & $read 'notExecuted'
    }
}

# A project path of a package list report, relative to the repository root with forward slashes; the file name alone
# when the path is outside the root (a report never carries a machine path into a summary).
function ConvertTo-ReportProjectPath {
    param(
        [AllowEmptyString()] [Parameter(Mandatory)] [string] $Path,
        [AllowEmptyString()] [string] $RepositoryRoot = ''
    )

    $normalized = $Path.Replace('\', '/')
    $root = $RepositoryRoot.Replace('\', '/').TrimEnd('/')
    if ($root -and $normalized.StartsWith("$root/", [StringComparison]::OrdinalIgnoreCase)) {
        return $normalized.Substring($root.Length + 1)
    }

    return [System.IO.Path]::GetFileName($normalized)
}

<#
.SYNOPSIS
    Flattens a `dotnet package list --format json` report (output version 1) run with --vulnerable or --deprecated into
    one row per project, framework and package. Detail holds the severities and advisory URLs, or the deprecation reasons
    and the alternative package. A report "problem" becomes a row with Package '(problem)'.
#>
function ConvertFrom-PackageListReport {
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)] [string] $Json,
        [Parameter(Mandatory)] [ValidateSet('Vulnerable', 'Deprecated')] [string] $Kind,
        [string] $RepositoryRoot = ''
    )

    $report = $Json | ConvertFrom-Json -AsHashtable
    if ($report -isnot [System.Collections.IDictionary] -or [int] $report['version'] -ne 1) {
        throw 'Expected a package list JSON report with "version": 1.'
    }

    foreach ($problem in @($report['problems'] | Where-Object { $_ })) {
        [pscustomobject]@{
            Project    = ConvertTo-ReportProjectPath -Path ([string] $problem['project']) -RepositoryRoot $RepositoryRoot
            Framework  = ''
            Package    = '(problem)'
            Resolved   = ''
            Transitive = $false
            Detail     = "$($problem['level']): $($problem['text'])"
        }
    }

    foreach ($project in @($report['projects'] | Where-Object { $_ })) {
        foreach ($framework in @($project['frameworks'] | Where-Object { $_ })) {
            foreach ($section in @('topLevelPackages', 'transitivePackages')) {
                foreach ($package in @($framework[$section] | Where-Object { $_ })) {
                    if ($Kind -ceq 'Vulnerable') {
                        $details = @($package['vulnerabilities'] | Where-Object { $_ } |
                                ForEach-Object { "$($_['severity']) $($_['advisoryurl'])" })
                    }
                    else {
                        $details = @(@($package['deprecationReasons'] | Where-Object { $_ }) -join ', ')
                        if ($package.Contains('alternativePackage') -and $package['alternativePackage']) {
                            $details += "use $($package['alternativePackage']['id']) $($package['alternativePackage']['versionRange'])"
                        }
                    }

                    [pscustomobject]@{
                        Project    = ConvertTo-ReportProjectPath -Path ([string] $project['path']) -RepositoryRoot $RepositoryRoot
                        Framework  = [string] $framework['framework']
                        Package    = [string] $package['id']
                        Resolved   = [string] $package['resolvedVersion']
                        Transitive = $section -ceq 'transitivePackages'
                        Detail     = (@($details | Where-Object { $_ }) -join '; ')
                    }
                }
            }
        }
    }
}

<#
.SYNOPSIS
    Returns the project paths (forward slashes, as written) of a .slnx solution document.
#>
function Get-SolutionProjectPath {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)] [string] $SolutionXml
    )

    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $document = [System.Xml.XmlDocument]::new()
    $reader = [System.Xml.XmlReader]::Create([System.IO.StringReader]::new($SolutionXml), $settings)
    try {
        $document.Load($reader)
    }
    finally {
        $reader.Dispose()
    }

    foreach ($project in $document.SelectNodes('//Project')) {
        $project.GetAttribute('Path').Replace('\', '/')
    }
}

<#
.SYNOPSIS
    Returns the distinct external http(s) links of a Markdown text, in order of first appearance. Fenced code blocks
    and code spans are skipped (commands and templates, not links), and so are links back into this repository's main branch (the
    repository tests check them offline), local hosts, example domains and templated URLs (<...>, {...}, $...).
#>
function Get-ExternalLinkTarget {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [AllowEmptyString()] [Parameter(Mandatory)] [string] $Markdown,
        [Parameter(Mandatory)] [string] $RepositorySlug
    )

    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $selfPrefix = "https://github.com/$RepositorySlug/"
    $inFence = $false
    foreach ($line in ($Markdown -split "\r?\n")) {
        if (Test-Match -InputText $line -Pattern '^\s{0,3}(```|~~~)') {
            $inFence = -not $inFence
            continue
        }

        if ($inFence) {
            continue
        }

        # Code spans hold commands and templates, not links.
        $prose = [regex]::Replace($line, '(`+).+?\1', ' ', [System.Text.RegularExpressions.RegexOptions]::CultureInvariant, $RegexTimeout)
        $candidates = [regex]::Matches($prose, 'https?://[^\s<>()\[\]"''`|]+', [System.Text.RegularExpressions.RegexOptions]::CultureInvariant, $RegexTimeout)
        foreach ($match in $candidates) {
            # A URL that continues with a placeholder (https://host/<id>/...) is a template.
            $next = $match.Index + $match.Length
            if ($next -lt $prose.Length -and $prose[$next] -in @([char] '<', [char] '{')) {
                continue
            }

            $url = $match.Value.TrimEnd('.', ',', ';', ':', '!', '?', '*', '_')
            $uri = $null
            if (-not [Uri]::TryCreate($url, [UriKind]::Absolute, [ref] $uri)) {
                continue
            }

            $isSelf = $url.StartsWith($selfPrefix + 'blob/main/', [StringComparison]::OrdinalIgnoreCase) -or
            $url.StartsWith($selfPrefix + 'tree/main/', [StringComparison]::OrdinalIgnoreCase)
            $isLocal = $uri.Host -in @('localhost', '127.0.0.1', '[::1]') -or
            $uri.Host -match '(^|\.)example\.(com|org|net)$'
            $isTemplate = $url.IndexOfAny([char[]] '{}$') -ge 0
            if ($isSelf -or $isLocal -or $isTemplate) {
                continue
            }

            if ($seen.Add($url)) {
                $url
            }
        }
    }
}

<#
.SYNOPSIS
    Classifies the answer to a link check: Ok (2xx, 3xx), Broken (404 Not Found, 410 Gone), Inconclusive (anything
    else, including rate limiting, server errors and a request that got no answer, StatusCode 0).
#>
function Get-ExternalLinkVerdict {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)] [int] $StatusCode
    )

    if ($StatusCode -ge 200 -and $StatusCode -lt 400) {
        return 'Ok'
    }

    if ($StatusCode -in @(404, 410)) {
        return 'Broken'
    }

    return 'Inconclusive'
}

<#
.SYNOPSIS
    Decides whether a scheduled health run needs attention and builds the issue body.

.DESCRIPTION
    -NeedsJson is `toJSON(needs)` of the notify job. A run needs attention when a job did not succeed, when the bridge
    check reported drift, or when broken links were found. Only values from closed vocabularies reach the body: job ids
    that are not plain identifiers and results outside success/failure/cancelled/skipped are rendered as '(unexpected)',
    and a run URL that is not a GitHub Actions run URL is left out.
#>
function Get-HealthIssueReport {
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)] [string] $NeedsJson,
        [AllowEmptyString()] [string] $Drift = '',
        [AllowEmptyString()] [string] $BrokenLinks = '',
        [AllowEmptyString()] [string] $RunUrl = ''
    )

    $needs = $NeedsJson | ConvertFrom-Json -AsHashtable
    if ($needs -isnot [System.Collections.IDictionary] -or $needs.Count -eq 0) {
        throw 'NEEDS must be the non-empty toJSON(needs) object of the notify job.'
    }

    $rows = [System.Collections.Generic.List[string]]::new()
    $attention = $false
    foreach ($job in @($needs.Keys | Sort-Object)) {
        $entry = $needs[$job]
        $result = if ($entry -is [System.Collections.IDictionary]) { [string] $entry['result'] } else { '' }
        $safeJob = if (Test-Match -InputText ([string] $job) -Pattern '^[A-Za-z0-9_-]{1,64}$') { [string] $job } else { '(unexpected)' }
        $safeResult = if ($result -cin $JobResults) { $result } else { '(unexpected)' }
        if ($safeResult -cne 'success') {
            $attention = $true
        }

        $rows.Add("| $safeJob | $safeResult |")
    }

    $findings = [System.Collections.Generic.List[string]]::new()
    if ($Drift -ceq 'true') {
        $attention = $true
        $findings.Add('- The release bridge rebuild does not reproduce the released DLL (see the `health-bridge-drift` artifact).')
    }

    if ($BrokenLinks -ceq 'true') {
        $attention = $true
        $findings.Add('- External documentation links are broken (see the job summary of `links`).')
    }

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('The weekly scheduled health workflow found a problem.')
    $lines.Add('')
    if (Test-Match -InputText $RunUrl -Pattern '^https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/actions/runs/\d+$') {
        $lines.Add("Run: $RunUrl")
        $lines.Add('')
    }

    $lines.Add('| Job | Result |')
    $lines.Add('| --- | --- |')
    $lines.AddRange($rows)
    if ($findings.Count -gt 0) {
        $lines.Add('')
        $lines.AddRange($findings)
    }

    $lines.Add('')
    $lines.Add('How to act on each job: eng/ci/health/README.md.')
    return [pscustomobject]@{
        NeedsAttention = $attention
        Title          = $HealthIssueTitle
        Body           = ($lines -join "`n")
    }
}

<#
.SYNOPSIS
    Returns the number of the open issue whose title is exactly the health issue title, the lowest one when several
    exist, or $null. -IssueListJson is the output of `gh issue list --json number,title`.
#>
function Select-HealthIssue {
    [CmdletBinding()]
    [OutputType([int])]
    param(
        [Parameter(Mandatory)] [string] $IssueListJson
    )

    $issues = @($IssueListJson | ConvertFrom-Json -AsHashtable)
    $numbers = @($issues | Where-Object { $_ -is [System.Collections.IDictionary] -and ([string] $_['title']) -ceq $HealthIssueTitle } |
            ForEach-Object { [int] $_['number'] } | Sort-Object)
    if ($numbers.Count -eq 0) {
        return $null
    }

    return $numbers[0]
}

Export-ModuleMember -Function Select-ReleaseTag, Get-BridgeDriftClassification, Get-SdkChannel, Get-NewestSdkVersion,
Get-UpdatedGlobalJson, Test-PackageReference, Get-TrxSummary, ConvertFrom-PackageListReport, Get-SolutionProjectPath,
Get-ExternalLinkTarget, Get-ExternalLinkVerdict, Get-HealthIssueReport, Select-HealthIssue
