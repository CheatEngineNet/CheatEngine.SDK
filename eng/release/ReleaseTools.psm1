#Requires -Version 7.4
# Pure logic of the release scripts in this folder (see README.md). Every function is a function of its arguments and
# of the files it is given; none reads the environment, writes the step summary or talks to the network, so the C# tests
# in tests/CheatEngine.SDK.Tests/Release call them directly. Encoding rules: SHA-256 as 64 lowercase hex digits, SHA-512
# as standard base64 with padding (the NuGet contentHash form), JSON as UTF-8 without BOM, LF, two-space indentation.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$script:SignatureEntry = '.signature.p7s'
$script:FingerprintPattern = '[0-9a-f]{64}:[0-9a-f]{64}'
$script:AbsoluteLocalPathPattern = '[A-Za-z]:\\|\\Users\\|/home/|/Users/|file://'
$script:GatingQualificationIds = @('Q02', 'Q03', 'Q04', 'Q05', 'Q06', 'Q07', 'Q08', 'Q09', 'Q10', 'Q40', 'Q41')

function Get-Sha256Hex {
  <# .SYNOPSIS SHA-256 of a file or a byte array, as 64 lowercase hex digits. #>
  [CmdletBinding(DefaultParameterSetName = 'Path')]
  [OutputType([string])]
  param(
    [Parameter(Mandatory, ParameterSetName = 'Path')] [string] $Path,
    [Parameter(Mandatory, ParameterSetName = 'Bytes')] [AllowEmptyCollection()] [byte[]] $Bytes
  )
  if ($PSCmdlet.ParameterSetName -eq 'Path') { $Bytes = [IO.File]::ReadAllBytes($Path) }
  return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()
}

function Get-Sha512Base64 {
  <# .SYNOPSIS SHA-512 of a file or a byte array, as standard base64 with padding (the NuGet contentHash form). #>
  [CmdletBinding(DefaultParameterSetName = 'Path')]
  [OutputType([string])]
  param(
    [Parameter(Mandatory, ParameterSetName = 'Path')] [string] $Path,
    [Parameter(Mandatory, ParameterSetName = 'Bytes')] [AllowEmptyCollection()] [byte[]] $Bytes
  )
  if ($PSCmdlet.ParameterSetName -eq 'Path') { $Bytes = [IO.File]::ReadAllBytes($Path) }
  return [Convert]::ToBase64String([Security.Cryptography.SHA512]::HashData($Bytes))
}

function Get-NormalizedTextSha256 {
  <#
  .SYNOPSIS
  SHA-256 of a committed text file after CRLF -> LF, i.e. of the git blob bytes: working trees are CRLF on every OS
  (.gitattributes), so hashing the raw file would give a different value per checkout.
  #>
  [CmdletBinding()]
  [OutputType([string])]
  param([Parameter(Mandatory)] [string] $Path)
  $bytes = [IO.File]::ReadAllBytes($Path)
  $normalized = [Collections.Generic.List[byte]]::new($bytes.Length)
  for ($index = 0; $index -lt $bytes.Length; $index++) {
    $isCrBeforeLf = ($bytes[$index] -eq 13) -and ($index + 1 -lt $bytes.Length) -and ($bytes[$index + 1] -eq 10)
    if (-not $isCrBeforeLf) { $normalized.Add($bytes[$index]) }
  }
  return Get-Sha256Hex -Bytes $normalized.ToArray()
}

function Get-ZipEntryName {
  <# .SYNOPSIS Every entry name of a zip archive (a .nupkg), in archive order. #>
  [CmdletBinding()]
  [OutputType([string[]])]
  param([Parameter(Mandatory)] [string] $Path)
  $archive = [IO.Compression.ZipFile]::OpenRead($Path)
  try {
    $names = [Collections.Generic.List[string]]::new()
    foreach ($entry in $archive.Entries) { $names.Add($entry.FullName) }
    return $names.ToArray()
  }
  finally {
    $archive.Dispose()
  }
}

function Read-ZipEntry {
  <# .SYNOPSIS The exact bytes of one zip entry, matched by its full name (ordinal); throws when it is absent. #>
  [CmdletBinding()]
  [OutputType([byte[]])]
  param(
    [Parameter(Mandatory)] [string] $Path,
    [Parameter(Mandatory)] [string] $EntryName
  )
  $archive = [IO.Compression.ZipFile]::OpenRead($Path)
  try {
    foreach ($entry in $archive.Entries) {
      if ([string]::Equals($entry.FullName, $EntryName, [StringComparison]::Ordinal)) {
        $stream = $entry.Open()
        try {
          $copy = [IO.MemoryStream]::new()
          $stream.CopyTo($copy)
          return , $copy.ToArray()
        }
        finally {
          $stream.Dispose()
        }
      }
    }
  }
  finally {
    $archive.Dispose()
  }
  throw "'$([IO.Path]::GetFileName($Path))' has no '$EntryName' entry."
}

function Get-NuspecIdentity {
  <# .SYNOPSIS The id, version and repository commit (or $null) declared by the .nuspec at the root of a package. #>
  [CmdletBinding()]
  [OutputType([pscustomobject])]
  param([Parameter(Mandatory)] [string] $PackagePath)
  $nuspecNames = @(Get-ZipEntryName -Path $PackagePath | Where-Object { $_ -notmatch '/' -and $_.EndsWith('.nuspec', [StringComparison]::OrdinalIgnoreCase) })
  if ($nuspecNames.Count -ne 1) {
    throw "'$([IO.Path]::GetFileName($PackagePath))' must have exactly one root .nuspec, found $($nuspecNames.Count)."
  }
  $document = [Xml.XmlDocument]::new()
  $document.Load([IO.MemoryStream]::new((Read-ZipEntry -Path $PackagePath -EntryName $nuspecNames[0])))
  $metadata = "/*[local-name()='package']/*[local-name()='metadata']"
  $id = $document.SelectSingleNode("$metadata/*[local-name()='id']")
  $version = $document.SelectSingleNode("$metadata/*[local-name()='version']")
  $commit = $document.SelectSingleNode("$metadata/*[local-name()='repository']/@commit")
  if ($null -eq $id -or $null -eq $version) {
    throw "The .nuspec of '$([IO.Path]::GetFileName($PackagePath))' has no id or version."
  }
  return [pscustomobject]@{
    Id               = $id.InnerText.Trim()
    Version          = $version.InnerText.Trim()
    RepositoryCommit = if ($null -eq $commit) { $null } else { $commit.Value }
  }
}

function Get-BridgeSourceFingerprint {
  <#
  .SYNOPSIS
  The '<c-sha256>:<xmake-sha256>' source fingerprint embedded in the native bridge, found in its bytes decoded as
  Latin-1. The DLL is never loaded (this also runs on Linux); exactly one match is required.
  #>
  [CmdletBinding()]
  [OutputType([string])]
  param([Parameter(Mandatory)] [byte[]] $Bytes)
  $found = [regex]::Matches([Text.Encoding]::Latin1.GetString($Bytes), $script:FingerprintPattern)
  if ($found.Count -ne 1) {
    throw "The native bridge must embed exactly one source fingerprint '<sha256>:<sha256>', found $($found.Count)."
  }
  return $found[0].Value
}

function Get-JsonValue {
  <#
  .SYNOPSIS
  A property of a parsed JSON object (ConvertFrom-Json) by path, without tripping strict mode: $null when a segment is
  absent, or an error naming the path and the file when -Required is set.
  #>
  [CmdletBinding()]
  param(
    [Parameter(Mandatory)] [AllowNull()] [object] $InputObject,
    [Parameter(Mandatory)] [string] $Name,
    [switch] $Required,
    [string] $Source = 'JSON document'
  )
  $current = $InputObject
  foreach ($segment in $Name.Split('.')) {
    if ($null -eq $current -or $current -isnot [pscustomobject]) { $current = $null; break }
    $property = $current.PSObject.Properties[$segment]
    if ($null -eq $property) { $current = $null; break }
    $current = $property.Value
  }
  if ($Required -and ($null -eq $current -or ($current -is [string] -and $current.Length -eq 0))) {
    throw "$Source has no '$Name'."
  }
  return $current
}

function ConvertTo-ReleaseJson {
  <# .SYNOPSIS Serializes ordered dictionaries as two-space indented JSON with LF line endings and a final newline. #>
  [CmdletBinding()]
  [OutputType([string])]
  param([Parameter(Mandatory)] [object] $InputObject)
  $json = ConvertTo-Json -InputObject $InputObject -Depth 32
  return ($json -replace "`r`n", "`n") + "`n"
}

function Write-Utf8File {
  <# .SYNOPSIS Writes text as UTF-8 without BOM, exactly as given (callers use LF line endings). #>
  [CmdletBinding()]
  param(
    [Parameter(Mandatory)] [string] $Path,
    [Parameter(Mandatory)] [AllowEmptyString()] [string] $Content
  )
  $directory = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Path))
  [void][IO.Directory]::CreateDirectory($directory)
  [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false))
}

function Test-AbsoluteLocalPath {
  <# .SYNOPSIS Whether a text contains an absolute local path (drive path, user profile, /home, file URI). #>
  [CmdletBinding()]
  [OutputType([bool])]
  param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Text)
  return [regex]::IsMatch($Text, $script:AbsoluteLocalPathPattern)
}

function ConvertTo-Sha256SumsText {
  <#
  .SYNOPSIS
  The 'sha256sum -c' form: '<64 lowercase hex><two spaces><name>' per line, sorted ordinally by name, LF, final newline.
  #>
  [CmdletBinding()]
  [OutputType([string])]
  param([Parameter(Mandatory)] [Collections.IDictionary] $Hashes)
  $names = [string[]]@($Hashes.Keys)
  [Array]::Sort($names, [StringComparer]::Ordinal)
  $builder = [Text.StringBuilder]::new()
  foreach ($name in $names) {
    $hash = [string]$Hashes[$name]
    if ($hash -cnotmatch '^[0-9a-f]{64}$') { throw "The SHA-256 of '$name' is not 64 lowercase hex digits." }
    if ($name -match '[\\/\s]' -or $name.Length -eq 0) { throw "'$name' is not a plain asset file name." }
    [void]$builder.Append($hash).Append('  ').Append($name).Append("`n")
  }
  return $builder.ToString()
}

function ConvertFrom-Sha256SumsText {
  <# .SYNOPSIS Parses a SHA256SUMS text into an ordered name -> hash dictionary; rejects anything but the exact format. #>
  [CmdletBinding()]
  [OutputType([Collections.Specialized.OrderedDictionary])]
  param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Text)
  $result = [ordered]@{}
  if ($Text.Contains("`r")) { throw 'SHA256SUMS must use LF line endings.' }
  if ($Text.Length -gt 0 -and -not $Text.EndsWith("`n")) { throw 'SHA256SUMS must end with a newline.' }
  foreach ($line in $Text.TrimEnd("`n").Split("`n")) {
    if ($line.Length -eq 0) { continue }
    $match = [regex]::Match($line, '^(?<hash>[0-9a-f]{64})  (?<name>[^\\/\s]+)$')
    if (-not $match.Success) { throw "SHA256SUMS line '$line' is not '<sha256>  <name>'." }
    $name = $match.Groups['name'].Value
    if ($result.Contains($name)) { throw "SHA256SUMS lists '$name' twice." }
    $result[$name] = $match.Groups['hash'].Value
  }
  return $result
}

function Compare-SignedPackageContent {
  <#
  .SYNOPSIS
  Compares the repository-signed nuget.org copy with the attested unsigned package: the signed copy must hold exactly
  the attested entries plus '.signature.p7s', and every common entry must be byte-identical.
  #>
  [CmdletBinding()]
  [OutputType([pscustomobject])]
  param(
    [Parameter(Mandatory)] [string] $AttestedPath,
    [Parameter(Mandatory)] [string] $SignedPath
  )
  $attested = Get-ZipEntryHashTable -Path $AttestedPath
  $signed = Get-ZipEntryHashTable -Path $SignedPath
  $missing = [Collections.Generic.List[string]]::new()
  $changed = [Collections.Generic.List[string]]::new()
  $unexpected = [Collections.Generic.List[string]]::new()
  foreach ($name in $attested.Keys) {
    if (-not $signed.ContainsKey($name)) { $missing.Add($name) }
    elseif ($signed[$name] -cne $attested[$name]) { $changed.Add($name) }
  }
  foreach ($name in $signed.Keys) {
    if (-not $attested.ContainsKey($name) -and $name -cne $script:SignatureEntry) { $unexpected.Add($name) }
  }
  $hasSignature = $signed.ContainsKey($script:SignatureEntry)
  return [pscustomobject]@{
    IsMatch      = ($missing.Count -eq 0) -and ($changed.Count -eq 0) -and ($unexpected.Count -eq 0) -and $hasSignature
    HasSignature = $hasSignature
    Missing      = $missing.ToArray()
    Changed      = $changed.ToArray()
    Unexpected   = $unexpected.ToArray()
    Compared     = $attested.Count
  }
}

function Get-ZipEntryHashTable {
  <# .SYNOPSIS Entry name -> SHA-256 of its bytes, ordinal keys. #>
  [CmdletBinding()]
  [OutputType([Collections.Generic.Dictionary[string, string]])]
  param([Parameter(Mandatory)] [string] $Path)
  $hashes = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
  $archive = [IO.Compression.ZipFile]::OpenRead($Path)
  try {
    foreach ($entry in $archive.Entries) {
      $stream = $entry.Open()
      try {
        $hashes[$entry.FullName] = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant()
      }
      finally {
        $stream.Dispose()
      }
    }
  }
  finally {
    $archive.Dispose()
  }
  return , $hashes
}

function Test-VerifyOutputContainsContentHash {
  <#
  .SYNOPSIS
  Whether 'dotnet nuget verify' output names the expected content hash as a whole base64 token. The label around it is
  localized ('Content hash', 'Hachage du contenu', ...), so only the value is checked.
  #>
  [CmdletBinding()]
  [OutputType([bool])]
  param(
    [Parameter(Mandatory)] [AllowEmptyString()] [string] $Output,
    [Parameter(Mandatory)] [string] $ContentHash
  )
  if ($ContentHash -cnotmatch '^[A-Za-z0-9+/]{86}==$') { throw "'$ContentHash' is not a base64 SHA-512." }
  $pattern = '(?<![A-Za-z0-9+/=])' + [regex]::Escape($ContentHash) + '(?![A-Za-z0-9+/=])'
  return [regex]::IsMatch($Output, $pattern)
}

function Get-ReleaseAssetPlan {
  <#
  .SYNOPSIS
  Decides what the draft-release job may do with the GitHub release of a tag. No release: create a draft with every
  asset. A draft: upload the missing assets and replace only -ReplaceableName when it differs; any other difference
  needs a human. A published release: never upload; it is fine only when every asset except -ReplaceableName is present
  with the same SHA-256.
  .PARAMETER LocalAsset
  Asset name -> SHA-256 (lowercase hex) of the files this run produced.
  .PARAMETER Release
  The parsed 'gh release view <tag> --json isDraft,assets' object, or $null when the tag has no release.
  #>
  [CmdletBinding()]
  [OutputType([pscustomobject])]
  param(
    [Parameter(Mandatory)] [Collections.IDictionary] $LocalAsset,
    [Parameter(Mandatory)] [AllowNull()] [object] $Release,
    [Parameter(Mandatory)] [string] $ReplaceableName
  )
  $names = [string[]]@($LocalAsset.Keys)
  [Array]::Sort($names, [StringComparer]::Ordinal)
  $upload = [Collections.Generic.List[string]]::new()
  $replace = [Collections.Generic.List[string]]::new()
  $problems = [Collections.Generic.List[string]]::new()

  if ($null -eq $Release) {
    return [pscustomobject]@{ Action = 'Create'; Upload = $names; Replace = @(); Problems = @() }
  }

  $remote = @{}
  foreach ($asset in @(Get-JsonValue -InputObject $Release -Name 'assets')) {
    if ($null -eq $asset) { continue }
    $digest = [string](Get-JsonValue -InputObject $asset -Name 'digest')
    $remote[[string](Get-JsonValue -InputObject $asset -Name 'name' -Required)] =
      if ($digest -cmatch '^sha256:[0-9a-f]{64}$') { $digest.Substring(7) } else { '' }
  }
  $isDraft = [bool](Get-JsonValue -InputObject $Release -Name 'isDraft')

  foreach ($name in $names) {
    $expected = [string]$LocalAsset[$name]
    $isReplaceable = [string]::Equals($name, $ReplaceableName, [StringComparison]::Ordinal)
    if (-not $remote.ContainsKey($name)) {
      if ($isDraft) { $upload.Add($name) }
      elseif (-not $isReplaceable) { $problems.Add("the published release has no '$name'") }
      continue
    }
    $actual = [string]$remote[$name]
    if ($actual -ceq $expected) { continue }
    if ($isReplaceable) {
      if ($isDraft) { $replace.Add($name) }
      continue
    }
    $shown = if ($actual) { $actual } else { 'no digest' }
    $problems.Add("'$name' on the release has SHA-256 $shown, this run produced $expected")
  }

  # A refusal carries no action at all: a maintainer decides, nothing is half-uploaded first.
  if ($problems.Count -gt 0) {
    return [pscustomobject]@{ Action = 'Refuse'; Upload = @(); Replace = @(); Problems = $problems.ToArray() }
  }
  $action = if ($isDraft) { 'Complete' } else { 'AlreadyPublished' }
  return [pscustomobject]@{ Action = $action; Upload = $upload.ToArray(); Replace = $replace.ToArray(); Problems = @() }
}

function Select-ReleasePullRequest {
  <#
  .SYNOPSIS
  Picks the pull request the release tuple names for a commit, from the parsed 'GET /repos/{repo}/commits/{sha}/pulls'
  answer. A tag points to the squash (merge) commit on main, so the pull request whose merge_commit_sha is the commit
  wins; a dry run from a branch has no merged pull request, so the single pull request whose head is the commit is used
  instead. Anything ambiguous or absent gives $null: the tuple then records no pull request, never a guessed one.
  #>
  [CmdletBinding()]
  [OutputType([pscustomobject])]
  param(
    [Parameter(Mandatory)] [AllowNull()] [AllowEmptyCollection()] [object[]] $PullRequest,
    [Parameter(Mandatory)] [string] $Commit
  )
  if ($Commit -cnotmatch '^[0-9a-f]{40}$') { throw "'$Commit' is not a 40-hex commit." }
  $merged = [Collections.Generic.List[object]]::new()
  $headed = [Collections.Generic.List[object]]::new()
  foreach ($candidate in @($PullRequest)) {
    if ($null -eq $candidate) { continue }
    $mergedAt = [string](Get-JsonValue -InputObject $candidate -Name 'merged_at')
    if ($mergedAt -and [string](Get-JsonValue -InputObject $candidate -Name 'merge_commit_sha') -ceq $Commit) { $merged.Add($candidate) }
    elseif ([string](Get-JsonValue -InputObject $candidate -Name 'head.sha') -ceq $Commit) { $headed.Add($candidate) }
  }
  $chosen = if ($merged.Count -eq 1) { $merged[0] } elseif ($merged.Count -eq 0 -and $headed.Count -eq 1) { $headed[0] } else { $null }
  if ($null -eq $chosen) { return $null }

  $number = [string](Get-JsonValue -InputObject $chosen -Name 'number')
  $headSha = [string](Get-JsonValue -InputObject $chosen -Name 'head.sha')
  if ($number -cnotmatch '^[1-9][0-9]*$' -or $headSha -cnotmatch '^[0-9a-f]{40}$') {
    throw 'The pull request of the commit has no valid number or head SHA.'
  }
  return [pscustomobject]@{
    Number  = $number
    HeadSha = $headSha
    Merged  = $merged.Count -eq 1
  }
}

function Get-ReleaseQualificationReport {
  <#
  .SYNOPSIS
  Evaluates the Checkpoint F qualification gate (Q02-Q10, Q40, Q41) on a parsed qualification matrix. A parent row
  passes when every required level is Passed, or NotApplicable with a justification, and every Passed C3/C4 cell names
  -TreeHash or carries a transfer justification. A row that does not pass is waived when the release notes list it
  under '### Qualification waivers' as '- Qxx: <reason>'.
  .PARAMETER Matrix
  The parsed docs/qualification/matrix.json, or $null when the file is absent (every gating row is then open).
  #>
  [CmdletBinding()]
  [OutputType([pscustomobject[]])]
  param(
    [Parameter(Mandatory)] [AllowNull()] [object] $Matrix,
    [Parameter(Mandatory)] [AllowEmptyString()] [string] $TreeHash,
    [AllowEmptyString()] [string] $ReleaseNotes = ''
  )
  $waivers = Get-QualificationWaiver -ReleaseNotes $ReleaseNotes
  $rows = @{}
  if ($null -ne $Matrix) {
    foreach ($row in @(Get-JsonValue -InputObject $Matrix -Name 'rows')) {
      if ($null -eq $row) { continue }
      $rowId = [string](Get-JsonValue -InputObject $row -Name 'id')
      if ($null -eq (Get-JsonValue -InputObject $row -Name 'parent') -and $rowId) { $rows[$rowId] = $row }
    }
  }

  $report = [Collections.Generic.List[pscustomobject]]::new()
  foreach ($id in $script:GatingQualificationIds) {
    $reason = if ($null -eq $Matrix) { 'docs/qualification/matrix.json is absent' }
    elseif (-not $rows.ContainsKey($id)) { "the matrix has no $id row" }
    else { Get-QualificationRowGap -Row $rows[$id] -TreeHash $TreeHash }

    $state = if (-not $reason) { 'Passed' } elseif ($waivers.Contains($id)) { 'Waived' } else { 'Open' }
    $detail = if ($state -eq 'Waived') { "waived: $($waivers[$id]) ($reason)" } elseif ($reason) { $reason } else { 'every required level qualifies' }
    $report.Add([pscustomobject]@{ Id = $id; State = $state; Reason = $detail })
  }
  return , $report.ToArray()
}

function Get-QualificationRowGap {
  <# .SYNOPSIS Why a matrix row does not pass the release gate, or '' when it does. #>
  [CmdletBinding()]
  [OutputType([string])]
  param(
    [Parameter(Mandatory)] [object] $Row,
    [Parameter(Mandatory)] [AllowEmptyString()] [string] $TreeHash
  )
  $gaps = [Collections.Generic.List[string]]::new()
  $levels = Get-JsonValue -InputObject $Row -Name 'levels'
  $required = @(Get-JsonValue -InputObject $Row -Name 'requiredLevels' | Where-Object { $null -ne $_ })
  if ($required.Count -eq 0) { return 'the row declares no required level' }
  foreach ($level in $required) {
    $cell = Get-JsonValue -InputObject $levels -Name ([string]$level)
    if ($null -eq $cell) { $gaps.Add("$level has no cell"); continue }
    $status = [string](Get-JsonValue -InputObject $cell -Name 'status')
    if ($status -ceq 'NotApplicable') {
      if (-not [string](Get-JsonValue -InputObject $cell -Name 'justification')) { $gaps.Add("$level is NotApplicable without a justification") }
      continue
    }
    if ($status -cne 'Passed') { $gaps.Add("$level is $status"); continue }
    if ($level -in @('C3', 'C4')) {
      # Host evidence is valid for the tree it names; committing its receipt already changes the tree, so a transfer
      # justification is the normal way a C3/C4 result reaches a release (shared contract 2.2).
      $cellTree = [string](Get-JsonValue -InputObject $cell -Name 'treeHash')
      $transfer = [string](Get-JsonValue -InputObject $cell -Name 'transferJustification')
      $sameTree = ($cellTree -cmatch '^[0-9a-f]{40}$') -and ($cellTree -ceq $TreeHash)
      if (-not $sameTree -and -not $transfer) {
        $named = if ($cellTree) { "tree $cellTree" } else { 'no tree' }
        $gaps.Add("$level passed on $named, not on the release tree, without a transfer justification")
      }
    }
  }
  return ($gaps -join '; ')
}

function Get-QualificationWaiver {
  <# .SYNOPSIS The '- Qxx: <reason>' lines under a '### Qualification waivers' heading, as Qxx -> reason. #>
  [CmdletBinding()]
  [OutputType([hashtable])]
  param([AllowEmptyString()] [string] $ReleaseNotes = '')
  $waivers = @{}
  $inSection = $false
  foreach ($line in ($ReleaseNotes -split "`r?`n")) {
    if ($line -match '^#{1,6}\s') {
      $inSection = $line -cmatch '^###\s+Qualification waivers\s*$'
      continue
    }
    if (-not $inSection) { continue }
    $match = [regex]::Match($line, '^\s*[-*]\s+(?<id>Q(?:0[1-9]|[1-3][0-9]|4[0-8])):\s*(?<reason>\S.*)$')
    if ($match.Success) { $waivers[$match.Groups['id'].Value] = $match.Groups['reason'].Value.Trim() }
  }
  return $waivers
}

Export-ModuleMember -Function @(
  'Get-Sha256Hex', 'Get-Sha512Base64', 'Get-NormalizedTextSha256', 'Get-ZipEntryName', 'Read-ZipEntry',
  'Get-NuspecIdentity', 'Get-BridgeSourceFingerprint', 'Get-JsonValue', 'ConvertTo-ReleaseJson', 'Write-Utf8File',
  'Test-AbsoluteLocalPath', 'ConvertTo-Sha256SumsText', 'ConvertFrom-Sha256SumsText', 'Compare-SignedPackageContent',
  'Test-VerifyOutputContainsContentHash', 'Get-ReleaseAssetPlan', 'Select-ReleasePullRequest',
  'Get-ReleaseQualificationReport', 'Get-QualificationWaiver'
)
