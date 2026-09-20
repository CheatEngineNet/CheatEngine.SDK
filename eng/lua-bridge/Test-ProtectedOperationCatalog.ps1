[CmdletBinding()]
param(
    [string]$CatalogPath = (Join-Path $PSScriptRoot 'protected-operations.json'),
    [string]$SchemaPath = (Join-Path $PSScriptRoot 'protected-operations.schema.json'),
    [string]$NativeBridgePath = (Join-Path $PSScriptRoot '..\..\native\cheatengine-sdk-lua-bridge\cheatengine_sdk_lua_bridge.c')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Require([bool] $Condition, [string] $Message) {
    if (-not $Condition) {
        throw $Message
    }
}

function Require-Property($Object, [string] $Name, [string] $Context) {
    Require ($null -ne $Object.PSObject.Properties[$Name]) "$Context is missing '$Name'."
}

function Escape-Regex([string] $Value) {
    return [regex]::Escape($Value)
}

Require (Test-Path -LiteralPath $CatalogPath -PathType Leaf) "The catalogue does not exist: $CatalogPath"
Require (Test-Path -LiteralPath $SchemaPath -PathType Leaf) "The catalogue schema does not exist: $SchemaPath"
Require (Test-Path -LiteralPath $NativeBridgePath -PathType Leaf) "The native bridge source does not exist: $NativeBridgePath"
Require (Test-Json -Path $CatalogPath -SchemaFile $SchemaPath) "The catalogue is not valid against '$SchemaPath'."

$catalog = Get-Content -LiteralPath $CatalogPath -Raw | ConvertFrom-Json
Require ($catalog.schemaVersion -eq 1) 'Only protected-operation catalogue schema version 1 is supported.'
Require ($catalog.catalogId -eq 'cheatengine-sdk-lua-protected-operations') 'The catalogue id is invalid.'
Require ($catalog.bridgeContract.operationBitmapWidth -eq 64) 'The bridge operation bitmap must be 64 bits.'

$allowedRaises = @('never', 'memory', 'any')
$byId = @{}
$byOpcode = @{}
$byNativeEnum = @{}
$byManagedConstant = @{}
$bitmap = [UInt64]0

foreach ($operation in @($catalog.operations)) {
    foreach ($property in @('id', 'nativeEnum', 'managed', 'opcode', 'capability', 'protected', 'requiresNativeProtection', 'raises', 'stack', 'ownership', 'hostOperation', 'provenance')) {
        Require-Property $operation $property "Operation"
    }

    Require (-not $byId.ContainsKey($operation.id)) "Duplicate operation id '$($operation.id)'."
    Require (-not $byOpcode.ContainsKey([int]$operation.opcode)) "Duplicate bridge opcode '$($operation.opcode)'."
    Require (-not $byNativeEnum.ContainsKey($operation.nativeEnum)) "Duplicate native enum '$($operation.nativeEnum)'."
    Require (-not $byManagedConstant.ContainsKey($operation.managed.constant)) "Duplicate managed constant '$($operation.managed.constant)'."
    Require (($operation.opcode -ge 0) -and ($operation.opcode -lt 64)) "Operation '$($operation.id)' has an opcode outside the 64-bit bitmap."
    Require ($operation.protected -eq $true) "Operation '$($operation.id)' must remain a protected operation."
    Require ($operation.requiresNativeProtection -eq $true) "Operation '$($operation.id)' must remain below the C11 lua_pcallk boundary."
    Require ($allowedRaises -contains $operation.raises) "Operation '$($operation.id)' has an invalid raises classification '$($operation.raises)'."
    Require (@($operation.provenance).Count -gt 0) "Operation '$($operation.id)' has no provenance."

    $byId.Add($operation.id, $operation)
    $byOpcode.Add([int]$operation.opcode, $operation)
    $byNativeEnum.Add($operation.nativeEnum, $operation)
    $byManagedConstant.Add($operation.managed.constant, $operation)
    $bitmap = $bitmap -bor ([UInt64]1 -shl [int]$operation.opcode)
}

$expectedBitmap = [Convert]::ToUInt64(([string]$catalog.bridgeContract.operationBitmap).Substring(2), 16)
Require ($bitmap -eq $expectedBitmap) ('The operation bitmap is 0x{0:X16}, but the operations derive 0x{1:X16}.' -f $expectedBitmap, $bitmap)

$nativeSource = Get-Content -LiteralPath $NativeBridgePath -Raw
$nativeEnumMatch = [regex]::Match($nativeSource, 'enum\s*\{\s*(?<values>OP_PUSH_BYTES\s*=\s*0,[\s\S]*?CHEATENGINE_SDK_LUA_BRIDGE_OPERATION_COUNT\s*=\s*\d+)\s*\};')
Require $nativeEnumMatch.Success 'Could not find the native protected-operation enum.'

$nativeCountMatch = [regex]::Match($nativeEnumMatch.Groups['values'].Value, 'CHEATENGINE_SDK_LUA_BRIDGE_OPERATION_COUNT\s*=\s*(?<count>\d+)')
Require $nativeCountMatch.Success 'The native protected-operation enum has no count sentinel.'
Require ([int]$nativeCountMatch.Groups['count'].Value -eq @($catalog.operations).Count) 'The native protected-operation count does not match the catalogue.'

foreach ($operation in @($catalog.operations)) {
    $nativeOpcodePattern = '\b' + (Escape-Regex $operation.nativeEnum) + '\s*=\s*' + [int]$operation.opcode + '\b'
    Require ([regex]::IsMatch($nativeEnumMatch.Groups['values'].Value, $nativeOpcodePattern)) "Native enum '$($operation.nativeEnum)' does not equal catalogue opcode $($operation.opcode)."
    Require ([regex]::IsMatch($nativeSource, ('case\s+' + (Escape-Regex $operation.nativeEnum) + '\s*:'))) "Native bridge has no case for '$($operation.nativeEnum)'."
    Require ($nativeSource.Contains($operation.nativeEnum)) "Native bridge bitmap does not name '$($operation.nativeEnum)'."

}

$managedSymbols = @{}
foreach ($policy in @($catalog.directApiPolicy)) {
    foreach ($property in @('managedSymbol', 'nativeSymbol', 'raises', 'allowedDirectly', 'requiresBridge', 'reason', 'provenance')) {
        Require-Property $policy $property "Direct API policy"
    }

    Require (-not $managedSymbols.ContainsKey($policy.managedSymbol)) "Duplicate direct API policy for '$($policy.managedSymbol)'."
    Require ($allowedRaises -contains $policy.raises) "Direct API policy '$($policy.managedSymbol)' has an invalid raises classification '$($policy.raises)'."
    Require (-not ($policy.allowedDirectly -and $policy.requiresBridge)) "Direct API policy '$($policy.managedSymbol)' cannot both allow direct use and require the bridge."
    Require (-not ((-not $policy.allowedDirectly) -and (-not $policy.requiresBridge))) "Direct API policy '$($policy.managedSymbol)' must explicitly choose a direct or bridge route."
    Require (-not ($policy.allowedDirectly -and $policy.raises -ne 'never')) "A directly allowed API '$($policy.managedSymbol)' must have the never classification."
    $bridgeOperationProperty = $policy.PSObject.Properties['bridgeOperation']
    if ($null -ne $bridgeOperationProperty) {
        Require ($byId.ContainsKey($bridgeOperationProperty.Value)) "Direct API policy '$($policy.managedSymbol)' refers to missing bridge operation '$($bridgeOperationProperty.Value)'."
    }
    $conditionalDirectUseProperty = $policy.PSObject.Properties['conditionalDirectUse']
    if ($null -ne $conditionalDirectUseProperty) {
        Require ($policy.allowedDirectly -eq $false) "A conditional direct API must retain a conservative default route."
        Require ($policy.requiresBridge -eq $true) "A conditional direct API must retain a conservative bridge route."
        Require ($conditionalDirectUseProperty.Value.allowed -eq $true) "A conditional direct API rule must explicitly opt in."
    }
    Require (@($policy.provenance).Count -gt 0) "Direct API policy '$($policy.managedSymbol)' has no provenance."
    $managedSymbols.Add($policy.managedSymbol, $policy)
}

Write-Host ('Validated {0} protected operations and {1} direct Lua API policies.' -f @($catalog.operations).Count, @($catalog.directApiPolicy).Count)
