// SPDX-License-Identifier: MIT
// A deliberately tiny native plugin DLL used only by ce77_native_abi_probe.

#include "ce77_plugin_abi_contract.h"

#include <type_traits>

namespace
{
constexpr uint32_t PluginVersion = 6;
char PluginName[] = "CE77 native ABI fixture";
}

extern "C" CE77_BOOL CE77_STDCALL CEPlugin_GetVersion(CE77PluginVersion* version, int32_t versionSize)
{
    if (version == nullptr || versionSize < static_cast<int32_t>(sizeof(CE77PluginVersion))) return 0;

    version->Version = PluginVersion;
    version->PluginName = PluginName;
    return 1;
}

extern "C" CE77_BOOL CE77_STDCALL CEPlugin_InitializePlugin(CE77ExportedFunctionsPrefix* exportedFunctions,
    int32_t pluginId)
{
    // The fixture deliberately never dereferences or retains the host table.  It
    // only proves the export's ABI, including a signed 32-bit plugin id.
    return exportedFunctions != nullptr && pluginId == 0x10203040 ? 1 : 0;
}

extern "C" CE77_BOOL CE77_STDCALL CEPlugin_DisablePlugin(void)
{
    return 1;
}

static_assert(std::is_same_v<decltype(&CEPlugin_GetVersion), CE77GetVersionExport>);
static_assert(std::is_same_v<decltype(&CEPlugin_InitializePlugin), CE77InitializePluginExport>);
static_assert(std::is_same_v<decltype(&CEPlugin_DisablePlugin), CE77DisablePluginExport>);
