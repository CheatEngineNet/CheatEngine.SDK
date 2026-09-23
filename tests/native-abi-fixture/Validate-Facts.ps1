[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$FactsPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $FactsPath -PathType Leaf)) {
    throw "The ABI fixture facts file '$FactsPath' does not exist."
}

$actual = @{}
foreach ($line in Get-Content -LiteralPath $FactsPath) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    $separator = $line.IndexOf('=')
    if ($separator -le 0) { throw "The ABI fixture fact '$line' is not key=value." }

    $key = $line.Substring(0, $separator)
    $value = $line.Substring($separator + 1)
    if ($actual.ContainsKey($key)) { throw "The ABI fixture emitted duplicate fact '$key'." }
    $actual[$key] = $value
}

$invariant = [Globalization.CultureInfo]::InvariantCulture

# Schema 3: every transcribed record, from the pinned C header and the pinned host Pascal types, with every field's
# offset and width. The set is exact: a missing, extra or different fact fails.
$expected = @{
    'fixture.schema' = '3'
    'source.upstream_commit' = 'ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37'
    'source.path' = 'Cheat Engine/plugin/cepluginsdk.h'
    'source.host_path' = 'Cheat Engine/plugin.pas'
    'source.mirror_path' = 'Cheat Engine/plugin/cepluginsdk.pas'
    'source.contract' = 'transcribed-pinned-header-and-host-pascal-subset'
    'architecture' = 'win-x64'
    'sizeof.pointer' = '8'
    'sizeof.bool' = '4'
    'sizeof.uint_ptr' = '8'
    'sizeof.plugin_type' = '4'
    'sizeof.auto_assembler_phase' = '4'
    'sizeof.plugin_type6_popup_show' = '4'
    'calling_convention.classic_callbacks' = '__stdcall'
    'table.direct_slot' = 'non-null-address-not-invoked'
    'table.process_id_cell' = '0x2468'
    'table.process_handle_cell' = '0x12345678'
    'table.fixmem' = 'null-not-invoked'
    'table.get_address_from_pointer' = 'conflicted-not-invoked'
    'table.hookable_suffix' = 'outside-prefix-not-dereferenced'
    'table.topology' = 'passed'
    'export.0' = 'CEPlugin_GetVersion'
    'export.1' = 'CEPlugin_InitializePlugin'
    'export.2' = 'CEPlugin_DisablePlugin'
    'sentinel.plugin_version.outer_guard' = 'passed'
    'sentinel.plugin_version.padding' = 'passed'
    'sentinel.exports.return_values' = 'passed'
    'sentinel.managed_plugin_init_record.tail_guard' = 'passed'
}

# Record => size, alignment, and 'Field=offset/width' entries in declaration order.
$selectedRecordFields = @(
    'InterpretedAddress=0/8', 'Address=8/8', 'IsPointer=16/4', 'CountOffsets=20/4', 'Offsets=24/8',
    'Description=32/8', 'ValueType=40/1', 'Size=41/1'
)
$registerFields = @('Address=0/8')
$registerNames = @('Eax', 'Ebx', 'Ecx', 'Edx', 'Esi', 'Edi', 'Ebp', 'Esp', 'Eip', 'R8', 'R9', 'R10', 'R11', 'R12',
    'R13', 'R14', 'R15')
$flagNames = @('Cf', 'Pf', 'Af', 'Zf', 'Sf', 'Of')
$offset = 8
foreach ($name in $registerNames + $flagNames) { $registerFields += "Change$name=$offset/4"; $offset += 4 }
$offset = 104
foreach ($name in $registerNames) { $registerFields += "New$name=$offset/8"; $offset += 8 }
foreach ($name in $flagNames) { $registerFields += "New$name=$offset/4"; $offset += 4 }
$prefixNames = @('SizeOfExportedFunctions', 'ShowMessage', 'RegisterFunction', 'UnregisterFunction', 'OpenedProcessId',
    'OpenedProcessHandle', 'GetMainWindowHandle', 'AutoAssemble', 'Assembler', 'Disassembler',
    'ChangeRegistersAtAddress', 'InjectDll', 'FreezeMemory', 'UnfreezeMemory', 'FixMemory', 'ProcessList',
    'ReloadSettings', 'GetAddressFromPointer')
$prefixFields = @('SizeOfExportedFunctions=0/4')
for ($slot = 1; $slot -lt $prefixNames.Count; $slot++) { $prefixFields += "$($prefixNames[$slot])=$(8 * $slot)/8" }

$records = [ordered]@{
    'plugin_version' = @(16, 8, @('Version=0/4', 'PluginName=8/8'))
    'plugin_type0_record' = @(48, 8, $selectedRecordFields)
    'plugin_type0_init' = @(16, 8, @('Name=0/8', 'Callback=8/8'))
    'plugin_type1_init' = @(24, 8, @('Name=0/8', 'Callback=8/8', 'Shortcut=16/8'))
    'plugin_type2_init' = @(8, 8, @('Callback=0/8'))
    'plugin_type3_init' = @(8, 8, @('Callback=0/8'))
    'plugin_type4_init' = @(8, 8, @('Callback=0/8'))
    'plugin_type5_init' = @(24, 8, @('Name=0/8', 'Callback=8/8', 'Shortcut=16/8'))
    'plugin_type6_init' = @(32, 8, @('Name=0/8', 'Callback=8/8', 'CallbackOnPopup=16/8', 'Shortcut=24/8'))
    'plugin_type7_init' = @(8, 8, @('Callback=0/8'))
    'plugin_type8_init' = @(8, 8, @('Callback=0/8'))
    'register_modification_info' = @(264, 8, $registerFields)
    'exported_functions_prefix' = @(144, 8, $prefixFields)
    'managed_plugin_init_record' = @(36, 1, @('Name=0/8', 'GetVersion=8/8', 'EnablePlugin=16/8', 'DisablePlugin=24/8',
        'Version=32/4'))
    'managed_exported_functions' = @(48, 8, @('SizeOfExportedFunctions=0/4', 'GetLuaState=8/8', 'LuaRegister=16/8',
        'LuaPushClassInstance=24/8', 'ProcessMessages=32/8', 'CheckSynchronize=40/8'))
    'host_plugin0_selected_record' = @(48, 8, $selectedRecordFields)
    'pascal_dword_mirror_selected_record' = @(48, 8, @('InterpretedAddress=0/8', 'Address=8/4', 'IsPointer=12/4',
        'CountOffsets=16/4', 'Offsets=24/8', 'Description=32/8', 'ValueType=40/1', 'Size=41/1'))
    'pascal_boolean_mirror_selected_record' = @(48, 8, @('InterpretedAddress=0/8', 'Address=8/8', 'IsPointer=16/1',
        'CountOffsets=20/4', 'Offsets=24/8', 'Description=32/8', 'ValueType=40/1', 'Size=41/1'))
}

foreach ($record in $records.GetEnumerator()) {
    $size, $alignment, $fields = $record.Value
    $expected["sizeof.$($record.Key)"] = $size.ToString($invariant)
    $expected["alignof.$($record.Key)"] = $alignment.ToString($invariant)
    foreach ($field in $fields) {
        $name, $layout = $field.Split('=')
        $fieldOffset, $fieldWidth = $layout.Split('/')
        $expected["offsetof.$($record.Key).$name"] = $fieldOffset
        $expected["fieldsize.$($record.Key).$name"] = $fieldWidth
    }
}

foreach ($key in $expected.Keys) {
    if (-not $actual.ContainsKey($key)) { throw "The ABI fixture omitted expected fact '$key'." }
    if ($actual[$key] -ne $expected[$key]) {
        throw "The ABI fixture fact '$key' was '$($actual[$key])', expected '$($expected[$key])'."
    }
}

foreach ($key in $actual.Keys) {
    if (-not $expected.ContainsKey($key)) { throw "The ABI fixture emitted unexpected fact '$key'." }
}

Write-Host "Validated $($expected.Count) ABI fixture facts (schema 3) from '$FactsPath'."
