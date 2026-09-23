#Requires -Version 7.4
# Pure logic of the release scripts in this folder (see README.md). Every function is a function of its arguments and
# of the files it is given; none reads the environment, writes the step summary or talks to the network, so the C# tests
# in tests/CheatEngine.SDK.Tests/Release call them directly. Encoding rules: SHA-256 as 64 lowercase hex digits, SHA-512
# as standard base64 with padding (the NuGet contentHash form), JSON as UTF-8 without BOM, LF, two-space indentation.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$script:FingerprintPattern = '[0-9a-f]{64}:[0-9a-f]{64}'
$script:AbsoluteLocalPathPattern = '[A-Za-z]:\\|\\Users\\|/home/|/Users/|file://'

function Get-Sha256Hex {
  <# .SYNOPSIS SHA-256 of a file or a byte array, as 64 lowercase hex digits. #>
  [CmdletBinding(DefaultParameterSetName = 'Path')]
  [OutputType([string])]
  param(
    [Parameter(Mandatory, ParameterSetName = 'Path')] [string] $Path,
    [Parameter(Mandatory, ParameterSetName = 'Bytes')] [AllowEmptyCollection()] [byte[]] $Bytes
  )
  if ($PSCmdlet.ParameterSetName -eq 'Path') { $Bytes = [IO.File]::ReadAllBytes($Path) }
  return [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($Bytes))
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

Export-ModuleMember -Function @(
  'Get-Sha256Hex', 'Get-Sha512Base64', 'Get-NormalizedTextSha256', 'Get-ZipEntryName', 'Read-ZipEntry',
  'Get-NuspecIdentity', 'Get-BridgeSourceFingerprint', 'Get-JsonValue', 'ConvertTo-ReleaseJson', 'Write-Utf8File',
  'Test-AbsoluteLocalPath', 'ConvertTo-Sha256SumsText', 'ConvertFrom-Sha256SumsText'
)
