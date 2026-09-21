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

$expected = @{
    'fixture.schema' = '2'
    'source.upstream_commit' = 'ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37'
    'source.path' = 'Cheat Engine/plugin/cepluginsdk.h'
    'source.contract' = 'transcribed-pinned-header-subset'
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
}

$layouts = @{
    'plugin_version' = 16
    'plugin_type0_record' = 48
    'plugin_type0_init' = 16
    'plugin_type1_init' = 24
    'plugin_type2_init' = 8
    'plugin_type3_init' = 8
    'plugin_type4_init' = 8
    'plugin_type5_init' = 24
    'plugin_type6_init' = 32
    'plugin_type7_init' = 8
    'plugin_type8_init' = 8
    'register_modification_info' = 264
    'exported_functions_prefix' = 144
}

foreach ($layout in $layouts.GetEnumerator()) {
    $expected["sizeof.$($layout.Key)"] = $layout.Value.ToString([Globalization.CultureInfo]::InvariantCulture)
    $expected["alignof.$($layout.Key)"] = '8'
}

$offsets = @{
    'plugin_version.Version' = 0
    'plugin_version.PluginName' = 8
    'plugin_type0_record.InterpretedAddress' = 0
    'plugin_type0_record.Address' = 8
    'plugin_type0_record.IsPointer' = 16
    'plugin_type0_record.CountOffsets' = 20
    'plugin_type0_record.Offsets' = 24
    'plugin_type0_record.Description' = 32
    'plugin_type0_record.ValueType' = 40
    'plugin_type0_record.Size' = 41
    'plugin_type0_init.Name' = 0
    'plugin_type0_init.Callback' = 8
    'plugin_type1_init.Name' = 0
    'plugin_type1_init.Callback' = 8
    'plugin_type1_init.Shortcut' = 16
    'plugin_type2_init.Callback' = 0
    'plugin_type3_init.Callback' = 0
    'plugin_type4_init.Callback' = 0
    'plugin_type5_init.Name' = 0
    'plugin_type5_init.Callback' = 8
    'plugin_type5_init.Shortcut' = 16
    'plugin_type6_init.Name' = 0
    'plugin_type6_init.Callback' = 8
    'plugin_type6_init.CallbackOnPopup' = 16
    'plugin_type6_init.Shortcut' = 24
    'plugin_type7_init.Callback' = 0
    'plugin_type8_init.Callback' = 0
    'register_modification_info.Address' = 0
    'register_modification_info.ChangeEax' = 8
    'register_modification_info.ChangeR15' = 72
    'register_modification_info.ChangeOf' = 96
    'register_modification_info.NewEax' = 104
    'register_modification_info.NewR15' = 232
    'register_modification_info.NewCf' = 240
    'register_modification_info.NewOf' = 260
    'exported_functions_prefix.SizeOfExportedFunctions' = 0
    'exported_functions_prefix.ShowMessage' = 8
    'exported_functions_prefix.RegisterFunction' = 16
    'exported_functions_prefix.UnregisterFunction' = 24
    'exported_functions_prefix.OpenedProcessId' = 32
    'exported_functions_prefix.OpenedProcessHandle' = 40
    'exported_functions_prefix.GetMainWindowHandle' = 48
    'exported_functions_prefix.AutoAssemble' = 56
    'exported_functions_prefix.Assembler' = 64
    'exported_functions_prefix.Disassembler' = 72
    'exported_functions_prefix.ChangeRegistersAtAddress' = 80
    'exported_functions_prefix.InjectDll' = 88
    'exported_functions_prefix.FreezeMemory' = 96
    'exported_functions_prefix.UnfreezeMemory' = 104
    'exported_functions_prefix.FixMemory' = 112
    'exported_functions_prefix.ProcessList' = 120
    'exported_functions_prefix.ReloadSettings' = 128
    'exported_functions_prefix.GetAddressFromPointer' = 136
}

foreach ($offset in $offsets.GetEnumerator()) {
    $expected["offsetof.$($offset.Key)"] = $offset.Value.ToString([Globalization.CultureInfo]::InvariantCulture)
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

Write-Host "Validated $($expected.Count) ABI fixture facts from '$FactsPath'."
