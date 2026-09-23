#Requires -Version 7.4
<#
.SYNOPSIS
    Pure helpers of the local exact-host qualification runner (eng/qualification/Invoke-LocalQualification.ps1).

.DESCRIPTION
    Nothing in this module starts Cheat Engine, touches the registry, takes the Global\ce-lab mutex or reads the Cheat
    Engine installation. Functions take text, objects or explicit paths and return values, so the repository tests
    (tests/CheatEngine.SDK.Repository.Tests/Qualification/LocalQualificationRunnerTests.cs) import this module and
    exercise it with synthetic input.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:SdkAssemblies = @(
    'CheatEngine.SDK.Abi.dll'
    'CheatEngine.SDK.Annotations.dll'
    'CheatEngine.SDK.Engine.dll'
    'CheatEngine.SDK.Hosting.dll'
    'CheatEngine.SDK.Lua.dll'
    'CheatEngine.SDK.Lua.Interop.dll'
)
$script:BridgeFileName = 'cheatengine-sdk-lua-bridge.dll'

function Get-QualificationFileSha256 {
    <#
    .SYNOPSIS
        Lowercase SHA-256 of a file's raw bytes.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)] [string] $Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-QualificationTextSha256 {
    <#
    .SYNOPSIS
        Lowercase SHA-256 of text after CRLF is normalized to LF, encoded as UTF-8 without BOM (the hash rule of every
        committed qualification JSON document).
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Text)

    $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($Text.Replace("`r`n", "`n"))
    return [System.Convert]::ToHexStringLower([System.Security.Cryptography.SHA256]::HashData($bytes))
}

function ConvertFrom-RegistryExport {
    <#
    .SYNOPSIS
        Parses the text of a reg.exe export into key -> (value name -> raw data). The data stays in memory only, for the
        comparison; nothing returned by Compare-RegistrySnapshot contains it.
    #>
    [CmdletBinding()]
    [OutputType([System.Collections.Specialized.OrderedDictionary])]
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Text)

    $result = [ordered]@{}
    $current = $null
    $pending = ''
    foreach ($raw in ($Text -split "`r?`n")) {
        $line = $raw
        if ($pending -ne '') {
            $line = $pending + $line.TrimStart()
            $pending = ''
        }

        if ($line.EndsWith('\') -and -not $line.StartsWith('[')) {
            $pending = $line.Substring(0, $line.Length - 1)
            continue
        }

        if ($line.StartsWith('[') -and $line.EndsWith(']')) {
            $current = $line.Substring(1, $line.Length - 2)
            $result[$current] = [ordered]@{}
            continue
        }

        if ($null -eq $current -or $line.Trim() -eq '') {
            continue
        }

        $match = [regex]::Match($line, '^(@|"(?:[^"\\]|\\.)*")=(.*)$')
        if ($match.Success) {
            $name = $match.Groups[1].Value
            $name = if ($name -eq '@') { '(Default)' } else { $name.Substring(1, $name.Length - 2).Replace('\"', '"').Replace('\\', '\') }
            $result[$current][$name] = $match.Groups[2].Value
        }
    }

    return $result
}

function Read-RegistryExport {
    <#
    .SYNOPSIS
        Reads a reg.exe export file (UTF-16 LE) and parses it with ConvertFrom-RegistryExport.
    #>
    [CmdletBinding()]
    [OutputType([System.Collections.Specialized.OrderedDictionary])]
    param([Parameter(Mandatory)] [string] $Path)

    return ConvertFrom-RegistryExport -Text ([System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::Unicode))
}

function Get-RelativeRegistryName {
    <#
    .SYNOPSIS
        A key path relative to the root key, with a trailing backslash ('' for the root itself).
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)] [string] $Key, [Parameter(Mandatory)] [string] $RootKey)

    if ($Key.Length -le $RootKey.Length) { return '' }
    return $Key.Substring($RootKey.Length).TrimStart('\') + '\'
}

function Compare-RegistrySnapshot {
    <#
    .SYNOPSIS
        Compares two parsed exports. Returns counts and the affected names relative to the root key: a key as
        'Subkey\' and a value as 'Subkey\ValueName'. Registry data is never part of the result.
    #>
    [CmdletBinding()]
    [OutputType([System.Collections.Specialized.OrderedDictionary])]
    param(
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Before,
        [Parameter(Mandatory)] [System.Collections.IDictionary] $After,
        [Parameter(Mandatory)] [string] $RootKey
    )

    $added = 0
    $removed = 0
    $changed = 0
    $names = [System.Collections.Generic.SortedSet[string]]::new([System.StringComparer]::Ordinal)

    foreach ($key in $After.Keys) {
        if (-not $Before.Contains($key)) {
            $added++
            [void] $names.Add((Get-RelativeRegistryName -Key $key -RootKey $RootKey))
            foreach ($value in $After[$key].Keys) {
                $added++
                [void] $names.Add((Get-RelativeRegistryName -Key $key -RootKey $RootKey) + $value)
            }
        }
    }

    foreach ($key in $Before.Keys) {
        if (-not $After.Contains($key)) {
            $removed++
            [void] $names.Add((Get-RelativeRegistryName -Key $key -RootKey $RootKey))
            foreach ($value in $Before[$key].Keys) {
                $removed++
                [void] $names.Add((Get-RelativeRegistryName -Key $key -RootKey $RootKey) + $value)
            }
            continue
        }

        $old = $Before[$key]
        $new = $After[$key]
        foreach ($value in $new.Keys) {
            if (-not $old.Contains($value)) {
                $added++
                [void] $names.Add((Get-RelativeRegistryName -Key $key -RootKey $RootKey) + $value)
            }
            elseif ($old[$value] -cne $new[$value]) {
                $changed++
                [void] $names.Add((Get-RelativeRegistryName -Key $key -RootKey $RootKey) + $value)
            }
        }

        foreach ($value in $old.Keys) {
            if (-not $new.Contains($value)) {
                $removed++
                [void] $names.Add((Get-RelativeRegistryName -Key $key -RootKey $RootKey) + $value)
            }
        }
    }

    return [ordered]@{
        added      = $added
        removed    = $removed
        changed    = $changed
        valueNames = @($names)
    }
}

function Get-RedactionMap {
    <#
    .SYNOPSIS
        Builds the ordered path -> placeholder list the redaction applies, longest path first, plus the user and machine
        names.
    #>
    [CmdletBinding()]
    [OutputType([object[]])]
    param(
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Paths,
        [string] $UserName = [System.Environment]::UserName,
        [string] $MachineName = [System.Environment]::MachineName
    )

    $entries = [System.Collections.Generic.List[object]]::new()
    foreach ($placeholder in $Paths.Keys) {
        $path = [string] $Paths[$placeholder]
        if ([string]::IsNullOrWhiteSpace($path)) { continue }
        $full = [System.IO.Path]::GetFullPath($path).TrimEnd('\', '/')
        $entries.Add([pscustomobject]@{ Path = $full; Placeholder = $placeholder; WholeWord = $false })
    }

    $sorted = @($entries | Sort-Object -Property { $_.Path.Length } -Descending)
    $names = @()
    if ($UserName.Length -ge 3) { $names += [pscustomobject]@{ Path = $UserName; Placeholder = '<user>'; WholeWord = $true } }
    if ($MachineName.Length -ge 3) { $names += [pscustomobject]@{ Path = $MachineName; Placeholder = '<machine>'; WholeWord = $true } }
    return @($sorted) + $names
}

function ConvertTo-RedactedText {
    <#
    .SYNOPSIS
        Replaces every mapped path (backslash, forward-slash and JSON-escaped spellings, case-insensitive) with its
        placeholder, then the user and machine names, then any remaining user-profile path segment.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Text,
        [Parameter(Mandatory)] [object[]] $Map
    )

    $result = $Text
    foreach ($entry in $Map) {
        $spellings = @($entry.Path, $entry.Path.Replace('\', '/'), $entry.Path.Replace('\', '\\')) | Select-Object -Unique
        foreach ($spelling in $spellings) {
            $pattern = [regex]::Escape($spelling)
            if ($entry.WholeWord) { $pattern = '(?<![A-Za-z0-9])' + $pattern + '(?![A-Za-z0-9])' }
            $result = [regex]::Replace($result, $pattern, $entry.Placeholder.Replace('$', '$$'),
                [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        }
    }

    # A user-profile path the map did not know (for example a DLL Cheat Engine loaded from the profile).
    $result = [regex]::Replace($result, '(?i)[A-Za-z]:(\\\\|\\|/)Users(\\\\|\\|/)[^\\/"\s]+', '<userProfile>')
    return $result
}

function Limit-QualificationEventLog {
    <#
    .SYNOPSIS
        Keeps the first and last events of a long log and reports how many were dropped from the middle.
    #>
    [CmdletBinding()]
    [OutputType([System.Collections.Specialized.OrderedDictionary])]
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Events,
        [int] $KeepFirst = 400,
        [int] $KeepLast = 100
    )

    if ($Events.Count -le ($KeepFirst + $KeepLast)) {
        return [ordered]@{ events = @($Events); summarized = $null }
    }

    $kept = @($Events[0..($KeepFirst - 1)]) + @($Events[($Events.Count - $KeepLast)..($Events.Count - 1)])
    return [ordered]@{
        events     = $kept
        summarized = [ordered]@{ keptFirst = $KeepFirst; keptLast = $KeepLast; dropped = $Events.Count - $KeepFirst - $KeepLast }
    }
}

function Get-QualificationReceiptId {
    <#
    .SYNOPSIS
        R-<yyyyMMddTHHmmssZ>-<Qid>-<first 8 hex of the package SHA-256>, from the run start in UTC.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)] [datetimeoffset] $StartedUtc,
        [Parameter(Mandatory)] [ValidatePattern('^Q(0[1-9]|[1-3][0-9]|4[0-8])(\.[a-z])?$')] [string] $QualificationId,
        [Parameter(Mandatory)] [ValidatePattern('^[0-9a-f]{64}$')] [string] $NupkgSha256
    )

    $stamp = $StartedUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", [System.Globalization.CultureInfo]::InvariantCulture)
    return "R-$stamp-$QualificationId-$($NupkgSha256.Substring(0, 8))"
}

function Format-QualificationUtc {
    <#
    .SYNOPSIS
        ISO 8601 UTC with a Z suffix and whole seconds.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)] [datetimeoffset] $Value)

    return $Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", [System.Globalization.CultureInfo]::InvariantCulture)
}

function ConvertTo-QualificationJson {
    <#
    .SYNOPSIS
        Serializes a document with LF newlines and one final newline, the committed form of receipts and event logs.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)] [AllowNull()] [object] $InputObject)

    $json = ConvertTo-Json -InputObject $InputObject -Depth 64
    return $json.Replace("`r`n", "`n") + "`n"
}

function ConvertTo-LuaLiteral {
    <#
    .SYNOPSIS
        Renders a value as a Lua literal: strings are quoted and escaped, dictionaries become tables with string keys,
        lists become sequences. Used to embed the run's steps, bundles and targets into the autorun driver.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [object] $InputObject)

    if ($null -eq $InputObject) { return 'nil' }
    if ($InputObject -is [bool]) { return $(if ($InputObject) { 'true' } else { 'false' }) }
    if ($InputObject -is [int] -or $InputObject -is [long] -or $InputObject -is [uint32]) {
        return ([long] $InputObject).ToString([System.Globalization.CultureInfo]::InvariantCulture)
    }
    if ($InputObject -is [double] -or $InputObject -is [decimal]) {
        return ([double] $InputObject).ToString('R', [System.Globalization.CultureInfo]::InvariantCulture)
    }
    if ($InputObject -is [string]) {
        $builder = [System.Text.StringBuilder]::new('"')
        foreach ($character in $InputObject.ToCharArray()) {
            switch ($character) {
                '\' { [void] $builder.Append('\\') }
                '"' { [void] $builder.Append('\"') }
                "`n" { [void] $builder.Append('\n') }
                "`r" { [void] $builder.Append('\r') }
                "`t" { [void] $builder.Append('\t') }
                default {
                    if ([int] $character -lt 32) { [void] $builder.Append('\' + ([int] $character).ToString('000')) }
                    else { [void] $builder.Append($character) }
                }
            }
        }
        return $builder.Append('"').ToString()
    }
    if ($InputObject -is [System.Collections.IDictionary] -or $InputObject -is [System.Management.Automation.PSCustomObject]) {
        $parts = [System.Collections.Generic.List[string]]::new()
        if ($InputObject -is [System.Collections.IDictionary]) {
            foreach ($key in $InputObject.Keys) {
                $parts.Add('[' + (ConvertTo-LuaLiteral -InputObject ([string] $key)) + '] = ' + (ConvertTo-LuaLiteral -InputObject $InputObject[$key]))
            }
        }
        else {
            foreach ($property in $InputObject.PSObject.Properties) {
                $parts.Add('[' + (ConvertTo-LuaLiteral -InputObject $property.Name) + '] = ' + (ConvertTo-LuaLiteral -InputObject $property.Value))
            }
        }
        return '{ ' + ($parts -join ', ') + ' }'
    }
    if ($InputObject -is [System.Collections.IEnumerable]) {
        $parts = foreach ($item in $InputObject) { ConvertTo-LuaLiteral -InputObject $item }
        return '{ ' + (@($parts) -join ', ') + ' }'
    }
    throw "ConvertTo-LuaLiteral cannot render a value of type $($InputObject.GetType().FullName)."
}

function Expand-QualificationDriver {
    <#
    .SYNOPSIS
        Substitutes the __NAME__ placeholders of the driver template with Lua literals. Every placeholder must be given.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)] [string] $Template,
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Values
    )

    $result = $Template
    foreach ($name in $Values.Keys) {
        $result = $result.Replace("__$($name)__", (ConvertTo-LuaLiteral -InputObject $Values[$name]))
    }

    $left = [regex]::Matches($result, '__[A-Z][A-Z_]*__')
    if ($left.Count -gt 0) {
        throw "The driver template still contains placeholders: $((@($left | ForEach-Object Value) | Select-Object -Unique) -join ', ')."
    }

    return $result
}

function Get-BundleFileManifest {
    <#
    .SYNOPSIS
        Every file of a bundle with its lowercase SHA-256, bundle-relative forward-slash paths, sorted ordinally.
    #>
    [CmdletBinding()]
    [OutputType([object[]])]
    param([Parameter(Mandatory)] [string] $BundleDirectory)

    $root = [System.IO.Path]::GetFullPath($BundleDirectory).TrimEnd('\') + '\'
    $files = foreach ($file in Get-ChildItem -LiteralPath $BundleDirectory -Recurse -File) {
        [ordered]@{
            path   = $file.FullName.Substring($root.Length).Replace('\', '/')
            sha256 = Get-QualificationFileSha256 -Path $file.FullName
        }
    }

    return @(@($files) | Sort-Object -Property { $_.path } -CaseSensitive)
}

function Test-EntryPointExport {
    <#
    .SYNOPSIS
        True when the assembly declares the public static class CESDK.CESDK with a public static
        int CEPluginInitialize(nint, int), read with System.Reflection.Metadata without loading the assembly.
    #>
    [CmdletBinding()]
    [OutputType([bool])]
    param([Parameter(Mandatory)] [string] $AssemblyPath)

    $stream = [System.IO.File]::OpenRead($AssemblyPath)
    try {
        $pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
        try {
            if (-not $pe.HasMetadata) { return $false }
            $reader = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
            foreach ($typeHandle in $reader.TypeDefinitions) {
                $type = $reader.GetTypeDefinition($typeHandle)
                if ($reader.GetString($type.Namespace) -cne 'CESDK' -or $reader.GetString($type.Name) -cne 'CESDK') { continue }
                $typeAttributes = $type.Attributes
                $isPublicStatic = (($typeAttributes -band [System.Reflection.TypeAttributes]::VisibilityMask) -eq [System.Reflection.TypeAttributes]::Public) -and
                    (($typeAttributes -band [System.Reflection.TypeAttributes]::Abstract) -ne 0) -and
                    (($typeAttributes -band [System.Reflection.TypeAttributes]::Sealed) -ne 0)
                if (-not $isPublicStatic) { return $false }
                foreach ($methodHandle in $type.GetMethods()) {
                    $method = $reader.GetMethodDefinition($methodHandle)
                    if ($reader.GetString($method.Name) -cne 'CEPluginInitialize') { continue }
                    $attributes = $method.Attributes
                    $isPublic = ($attributes -band [System.Reflection.MethodAttributes]::MemberAccessMask) -eq [System.Reflection.MethodAttributes]::Public
                    $isStatic = ($attributes -band [System.Reflection.MethodAttributes]::Static) -ne 0
                    $blob = $reader.GetBlobBytes($method.Signature)
                    # Default calling convention, 2 parameters, returns I4 (0x08), takes I (0x18) and I4 (0x08).
                    $shape = ($blob.Length -eq 5) -and $blob[0] -eq 0x00 -and $blob[1] -eq 2 -and $blob[2] -eq 0x08 -and $blob[3] -eq 0x18 -and $blob[4] -eq 0x08
                    if ($isPublic -and $isStatic -and $shape) { return $true }
                }
                return $false
            }
            return $false
        }
        finally {
            $pe.Dispose()
        }
    }
    catch [System.BadImageFormatException] {
        return $false
    }
    finally {
        $stream.Dispose()
    }
}

function Test-QualificationBundleClosure {
    <#
    .SYNOPSIS
        Checks that a plugin folder built from the exact package is self-contained: the plugin with the CESDK.CESDK entry
        point, the six SDK assemblies, a .deps.json without workspace project entries or absolute paths, the
        .runtimeconfig.json, and the native bridge equal to the package's build/native copy. Returns the problems found.
    #>
    [CmdletBinding()]
    [OutputType([string[]])]
    param(
        [Parameter(Mandatory)] [string] $BundleDirectory,
        [Parameter(Mandatory)] [string] $PluginFileName,
        [Parameter(Mandatory)] [ValidatePattern('^[0-9a-fA-F]{64}$')] [string] $PackagedBridgeSha256
    )

    $problems = [System.Collections.Generic.List[string]]::new()
    $plugin = Join-Path $BundleDirectory $PluginFileName
    if (-not (Test-Path -LiteralPath $plugin -PathType Leaf)) {
        $problems.Add("The plugin $PluginFileName is missing.")
    }
    elseif (-not (Test-EntryPointExport -AssemblyPath $plugin)) {
        $problems.Add("$PluginFileName declares no public static CESDK.CESDK.CEPluginInitialize(nint, int).")
    }

    foreach ($assembly in $script:SdkAssemblies) {
        if (-not (Test-Path -LiteralPath (Join-Path $BundleDirectory $assembly) -PathType Leaf)) {
            $problems.Add("The SDK assembly $assembly is missing.")
        }
    }

    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($PluginFileName)
    $deps = Join-Path $BundleDirectory "$baseName.deps.json"
    if (-not (Test-Path -LiteralPath $deps -PathType Leaf)) {
        $problems.Add("$baseName.deps.json is missing.")
    }
    else {
        $depsText = [System.IO.File]::ReadAllText($deps)
        if ($depsText -match '"type"\s*:\s*"project"') {
            $problems.Add("$baseName.deps.json contains a workspace project entry; the SDK must come from the package.")
        }
        if ($depsText -match '(?<![A-Za-z0-9])[A-Za-z]:(\\\\|/)') {
            $problems.Add("$baseName.deps.json contains an absolute path.")
        }
    }

    if (-not (Test-Path -LiteralPath (Join-Path $BundleDirectory "$baseName.runtimeconfig.json") -PathType Leaf)) {
        $problems.Add("$baseName.runtimeconfig.json is missing.")
    }

    $bridge = Join-Path $BundleDirectory $script:BridgeFileName
    if (-not (Test-Path -LiteralPath $bridge -PathType Leaf)) {
        $problems.Add("The native bridge $($script:BridgeFileName) is missing.")
    }
    elseif ((Get-QualificationFileSha256 -Path $bridge) -ne $PackagedBridgeSha256.ToLowerInvariant()) {
        $problems.Add("The native bridge differs from the package's build/native/$($script:BridgeFileName).")
    }

    return $problems.ToArray()
}

function Test-QualificationWorkRoot {
    <#
    .SYNOPSIS
        Returns why a work root is unsafe (inside a git work tree, or below the repository's parent directory), or $null.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)] [string] $WorkRoot,
        [Parameter(Mandatory)] [string] $RepositoryRoot
    )

    $full = [System.IO.Path]::GetFullPath($WorkRoot).TrimEnd('\') + '\'
    $repositoryParent = [System.IO.Path]::GetFullPath((Join-Path $RepositoryRoot '..')).TrimEnd('\') + '\'
    if ($full.StartsWith($repositoryParent, [System.StringComparison]::OrdinalIgnoreCase)) {
        return "The work root '$WorkRoot' is below the repository's parent directory; bundles must not see workspace files."
    }

    for ($directory = [System.IO.DirectoryInfo]::new($full); $null -ne $directory; $directory = $directory.Parent) {
        if (Test-Path -LiteralPath (Join-Path $directory.FullName '.git')) {
            return "The work root '$WorkRoot' is inside the git work tree '$($directory.FullName)'."
        }
    }

    return $null
}

function Get-CiEnvironmentVariable {
    <#
    .SYNOPSIS
        Returns the name of the first non-empty CI marker variable (CI, GITHUB_ACTIONS, TF_BUILD), or $null.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param()

    foreach ($name in 'CI', 'GITHUB_ACTIONS', 'TF_BUILD') {
        if (-not [string]::IsNullOrEmpty([System.Environment]::GetEnvironmentVariable($name))) { return $name }
    }

    return $null
}

function Test-HasProperty {
    <#
    .SYNOPSIS
        True when an object (a JSON-parsed PSCustomObject or a dictionary) has the named property; safe in strict mode.
    #>
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)] [AllowNull()] [object] $InputObject,
        [Parameter(Mandatory)] [string] $Name
    )

    if ($null -eq $InputObject) { return $false }
    if ($InputObject -is [System.Collections.IDictionary]) { return $InputObject.Contains($Name) }
    return @($InputObject.PSObject.Properties.Name) -ccontains $Name
}

function Get-OptionalProperty {
    <#
    .SYNOPSIS
        The named property of an object, or $null when it is absent; safe in strict mode.
    #>
    [CmdletBinding()]
    [OutputType([object])]
    param(
        [Parameter(Mandatory)] [AllowNull()] [object] $InputObject,
        [Parameter(Mandatory)] [string] $Name
    )

    if (-not (Test-HasProperty -InputObject $InputObject -Name $Name)) { return $null }
    if ($InputObject -is [System.Collections.IDictionary]) { return $InputObject[$Name] }
    return $InputObject.$Name
}

function Get-StepValue {
    <#
    .SYNOPSIS
        Reads a value of a recorded step: 'N' is the Nth returned Lua value (0-based); any other selector is a dotted
        JSON path into the first returned value, parsed as JSON.
    #>
    [CmdletBinding()]
    [OutputType([object])]
    param(
        [Parameter(Mandatory)] [AllowNull()] [object] $Step,
        [Parameter(Mandatory)] [string] $Selector
    )

    $values = @(Get-OptionalProperty -InputObject $Step -Name 'values')
    if ($values.Count -eq 1 -and $null -eq $values[0]) { $values = @() }
    if ($Selector -match '^\d+$') {
        $index = [int] $Selector
        if ($index -ge $values.Count) { return $null }
        return Get-OptionalProperty -InputObject $values[$index] -Name 'value'
    }

    if ($values.Count -eq 0 -or (Get-OptionalProperty -InputObject $values[0] -Name 'type') -ne 'string') { return $null }
    try { $current = $values[0].value | ConvertFrom-Json -Depth 64 }
    catch { return $null }
    foreach ($segment in $Selector.Split('.')) {
        if (-not (Test-HasProperty -InputObject $current -Name $segment)) { return $null }
        $current = Get-OptionalProperty -InputObject $current -Name $segment
    }

    return $current
}

function Resolve-QualificationOutcome {
    <#
    .SYNOPSIS
        Evaluates a scenario pass rule on the recorded, redacted step results and operator answers. Returns the receipt
        status, pass kind, observed text and justification; Inconclusive means no receipt is written.
    #>
    [CmdletBinding()]
    [OutputType([System.Collections.Specialized.OrderedDictionary])]
    param(
        [Parameter(Mandatory)] [object] $Scenario,
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Steps,
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Answers,
        [string] $PackagedBridgeSha256 = ''
    )

    $rule = $Scenario.passRule
    $results = [System.Collections.Generic.List[string]]::new()
    $failed = 0
    foreach ($check in @($rule.checks)) {
        $passed = $false
        $label = $check.step
        if (Test-HasProperty -InputObject $check -Name 'answer') {
            $answer = [string] $Answers[$check.step]
            $passed = $answer.Trim().StartsWith([string] $check.answer, [System.StringComparison]::OrdinalIgnoreCase)
            $label = "$($check.step) answered '$($answer.Trim())'"
        }
        else {
            $step = $Steps[$check.step]
            $passed = Test-StepCheck -Check $check -Step $step -Steps $Steps -PackagedBridgeSha256 $PackagedBridgeSha256
            $label = Format-StepCheck -Check $check -Step $step
        }

        if (-not $passed) { $failed++ }
        $results.Add("$(if ($passed) { 'ok' } else { 'FAILED' }): $label")
    }

    $observe = if (Test-HasProperty -InputObject $Scenario -Name 'observe') { @($Scenario.observe) } else { @() }
    foreach ($selector in $observe) {
        $stepId, $path = $selector.Split(':', 2)
        $value = Get-StepValue -Step $Steps[$stepId] -Selector $path
        $results.Add("observed $($stepId):$path = $(ConvertTo-Json -InputObject $value -Compress -Depth 8)")
    }

    $observed = $results -join '; '
    $outcome = [ordered]@{ status = 'Failed'; passKind = $null; observed = $observed; justification = $null }
    if (Test-HasProperty -InputObject $rule -Name 'requiresAnswer') {
        $required = $rule.requiresAnswer
        if (-not ([string] $Answers[$required.step]).Trim().StartsWith([string] $required.answer, [System.StringComparison]::OrdinalIgnoreCase)) {
            $outcome.status = 'Inconclusive'
            $outcome.justification = "The operator did not perform step '$($required.step)'; no receipt is written."
            return $outcome
        }
    }

    switch ($rule.kind) {
        'NotApplicableObservation' {
            $outcome.status = if ($failed -eq 0) { 'NotApplicable' } else { 'Inconclusive' }
            $outcome.justification = if ($failed -eq 0) { [string] (Get-OptionalProperty -InputObject $rule -Name 'justification') } else { 'The observation the NotApplicable decision records was not obtained.' }
        }
        default {
            if ($failed -eq 0) {
                $outcome.status = 'Passed'
                $outcome.passKind = [string] (Get-OptionalProperty -InputObject $rule -Name 'passKind')
            }
            else {
                $outcome.justification = "$failed check(s) of the pass rule failed; see observed."
            }
        }
    }

    return $outcome
}

function Test-StepCheck {
    <#
    .SYNOPSIS
        Evaluates one non-operator check of a pass rule against a recorded step.
    #>
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)] [object] $Check,
        [Parameter(Mandatory)] [AllowNull()] [object] $Step,
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Steps,
        [string] $PackagedBridgeSha256 = ''
    )

    if ($null -eq $Step) { return $false }
    $names = @($Check.PSObject.Properties.Name)
    if ($names -contains 'ok') { return [bool] $Step.ok -eq [bool] $Check.ok }
    if ($names -contains 'errorContains') {
        $text = [string] (Get-StepValue -Step $Step -Selector '0')
        return (-not [bool] $Step.ok) -and $text.Contains([string] $Check.errorContains, [System.StringComparison]::Ordinal)
    }

    $value = Get-StepValue -Step $Step -Selector ([string] $Check.json)
    if ($names -contains 'equals') { return ($null -ne $value) -and ($value -ceq $Check.equals) }
    if ($names -contains 'present') { return $null -ne $value }
    if ($names -contains 'atLeast') { return ($null -ne $value) -and ([double] $value -ge [double] $Check.atLeast) }
    if ($names -contains 'contains') { return @($value) -ccontains $Check.contains }
    if ($names -contains 'startsWith') { return ($null -ne $value) -and ([string] $value).StartsWith([string] $Check.startsWith, [System.StringComparison]::Ordinal) }
    if ($names -contains 'equalsPackagedBridge') {
        return ($PackagedBridgeSha256.Length -eq 64) -and ([string] $value).Equals($PackagedBridgeSha256, [System.StringComparison]::OrdinalIgnoreCase)
    }
    if ($names -contains 'greaterThan' -or $names -contains 'sameAs') {
        $reference = if ($names -contains 'greaterThan') { $Check.greaterThan } else { $Check.sameAs }
        $other = Get-StepValue -Step $Steps[$reference.step] -Selector ([string] $reference.json)
        if ($null -eq $value -or $null -eq $other) { return $false }
        if ($names -contains 'greaterThan') { return [double] $value -gt [double] $other }
        return $value -ceq $other
    }

    throw "Unknown pass-rule check: $(ConvertTo-Json -InputObject $Check -Compress)."
}

function Format-StepCheck {
    <#
    .SYNOPSIS
        A one-line description of a check and of the value it saw, for the receipt's observed text.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)] [object] $Check,
        [Parameter(Mandatory)] [AllowNull()] [object] $Step
    )

    $names = @($Check.PSObject.Properties.Name)
    if ($null -eq $Step) { return "$($Check.step) was not recorded" }
    if ($names -contains 'ok') { return "$($Check.step).ok is $(([bool] $Step.ok).ToString().ToLowerInvariant()), expected $(([bool] $Check.ok).ToString().ToLowerInvariant())" }
    if ($names -contains 'errorContains') { return "$($Check.step) error contains '$($Check.errorContains)'" }
    $value = Get-StepValue -Step $Step -Selector ([string] $Check.json)
    $condition = ($names | Where-Object { $_ -notin 'step', 'json' }) -join ','
    return "$($Check.step):$($Check.json) = $(ConvertTo-Json -InputObject $value -Compress -Depth 8) ($condition)"
}

function ConvertTo-RedactedSessionEvent {
    <#
    .SYNOPSIS
        Stage 13: redacts every event and returns the event log entries and the step results the pass rule reads.
    #>
    [CmdletBinding()]
    [OutputType([System.Collections.Specialized.OrderedDictionary])]
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Raw,
        [Parameter(Mandatory)] [object[]] $Map,
        [Parameter(Mandatory)] [long] $OffsetMs
    )

    $entries = [System.Collections.Generic.List[object]]::new()
    $steps = [ordered]@{}
    foreach ($item in $Raw) {
        $message = [string] $item.message
        if ($item.kind -eq 'StepResult') {
            $result = $message | ConvertFrom-Json -Depth 64
            foreach ($value in @($result.values)) {
                if ($null -ne $value -and (Test-HasProperty -InputObject $value -Name 'value') -and $value.value -is [string]) {
                    $value.value = ConvertTo-RedactedText -Text $value.value -Map $Map
                }
            }
            $steps[[string] $result.id] = $result
            $message = ConvertTo-Json -InputObject $result -Compress -Depth 64
        }
        else {
            $message = ConvertTo-RedactedText -Text $message -Map $Map
        }

        $time = [long] $item.tMs
        $entries.Add([ordered]@{ tMs = [long] [Math]::Max(0, $(if ($item.source -eq 'Runner') { $time } else { $OffsetMs + $time })); source = [string] $item.source; kind = [string] $item.kind; message = $message })
    }

    return [ordered]@{ entries = @($entries | Sort-Object -Property { $_.tMs } -Stable); steps = $steps }
}

function ConvertTo-QualificationReceipt {
    <#
    .SYNOPSIS
        Assembles a receipt (schema cheatengine-qualification-receipt/v0) in schema order from the run context.
    #>
    [CmdletBinding()]
    [OutputType([System.Collections.Specialized.OrderedDictionary])]
    param([Parameter(Mandatory)] [System.Collections.IDictionary] $Context)

    $receiptId = Get-QualificationReceiptId -StartedUtc $Context.StartedUtc -QualificationId $Context.QualificationId -NupkgSha256 $Context.Package.nupkgSha256
    $outcome = $Context.Outcome
    $timings = [ordered]@{
        startedUtc  = Format-QualificationUtc -Value $Context.StartedUtc
        finishedUtc = Format-QualificationUtc -Value $Context.FinishedUtc
        durationMs  = [long] [Math]::Max(0, ($Context.FinishedUtc - $Context.StartedUtc).TotalMilliseconds)
    }
    if ($Context.Contains('CeStartMs')) { $timings.ceStartMs = [long] $Context.CeStartMs }
    if ($Context.Contains('Indicative') -and $Context.Indicative) { $timings.indicative = $true }

    return [ordered]@{
        schema                = 'cheatengine-qualification-receipt/v0'
        receiptId             = $receiptId
        qualificationId       = $Context.QualificationId
        level                 = $Context.Level
        profileId             = $Context.ProfileId
        operator              = $Context.Operator
        loadRoute             = $Context.LoadRoute
        repository            = $Context.Repository
        runner                = $Context.Runner
        package               = $Context.Package
        host                  = $Context.Host
        bridge                = $Context.Bridge
        bundles               = @($Context.Bundles)
        target                = $Context.Target
        registry              = $Context.Registry
        preconditions         = @($Context.Preconditions)
        operation             = $Context.Operation
        expected              = $Context.Expected
        observed              = $outcome.observed
        status                = $outcome.status
        passKind              = $outcome.passKind
        evidenceKind          = 'ObservedHost'
        justification         = $outcome.justification
        timings               = $timings
        eventLog              = [ordered]@{
            path       = "$receiptId.events.json"
            sha256     = $Context.EventLogSha256
            format     = 'cheatengine-qualification-events/v0'
            redactions = @($Context.Redactions)
        }
        transferJustification = $null
        createdUtc            = Format-QualificationUtc -Value $Context.CreatedUtc
    }
}

Export-ModuleMember -Function @(
    'Get-QualificationFileSha256'
    'Get-QualificationTextSha256'
    'ConvertFrom-RegistryExport'
    'Read-RegistryExport'
    'Compare-RegistrySnapshot'
    'Get-RedactionMap'
    'ConvertTo-RedactedText'
    'Limit-QualificationEventLog'
    'Get-QualificationReceiptId'
    'Format-QualificationUtc'
    'ConvertTo-QualificationJson'
    'ConvertTo-LuaLiteral'
    'Expand-QualificationDriver'
    'Get-BundleFileManifest'
    'Test-EntryPointExport'
    'Test-QualificationBundleClosure'
    'Test-QualificationWorkRoot'
    'Get-CiEnvironmentVariable'
    'Test-HasProperty'
    'Get-OptionalProperty'
    'Get-StepValue'
    'Resolve-QualificationOutcome'
    'Test-StepCheck'
    'Format-StepCheck'
    'ConvertTo-RedactedSessionEvent'
    'ConvertTo-QualificationReceipt'
)
