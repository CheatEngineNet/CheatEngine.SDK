// SPDX-License-Identifier: MIT
// Emits the classic CE plugin ABI facts after checking the fixture's three exports.

#include "ce77_plugin_abi_contract.h"

#include <windows.h>

#include <array>
#include <cstdio>
#include <cstring>

namespace
{
constexpr unsigned char Guard = 0xA5;

bool IsGuarded(const unsigned char* bytes, size_t count)
{
    for (size_t index = 0; index < count; index++)
    {
        if (bytes[index] != Guard) return false;
    }

    return true;
}

void EmitFacts()
{
    std::printf("fixture.schema=%d\n", CE77_ABI_FIXTURE_SCHEMA_VERSION);
    std::printf("source.upstream_commit=%s\n", CE77_UPSTREAM_COMMIT);
    std::printf("source.path=%s\n", CE77_CEPLUGINSDK_PATH);
    std::printf("architecture=win-x64\n");
    std::printf("sizeof.pointer=%zu\n", sizeof(void*));
    std::printf("sizeof.bool=%zu\n", sizeof(CE77_BOOL));
    std::printf("sizeof.uint_ptr=%zu\n", sizeof(CE77_UINT_PTR));
    std::printf("sizeof.plugin_version=%zu\n", sizeof(CE77PluginVersion));
    std::printf("offsetof.plugin_version.version=%zu\n", offsetof(CE77PluginVersion, Version));
    std::printf("offsetof.plugin_version.plugin_name=%zu\n", offsetof(CE77PluginVersion, PluginName));
    std::printf("sizeof.plugin_type0_record=%zu\n", sizeof(CE77PluginType0Record));
    std::printf("offsetof.plugin_type0_record.value_type=%zu\n", offsetof(CE77PluginType0Record, ValueType));
    std::printf("offsetof.plugin_type0_record.size=%zu\n", offsetof(CE77PluginType0Record, Size));
    std::printf("sizeof.register_modification_info=%zu\n", sizeof(CE77RegisterModificationInfo));
    std::printf("offsetof.register_modification_info.new_eax=%zu\n", offsetof(CE77RegisterModificationInfo, NewEax));
    std::printf("offsetof.register_modification_info.new_r15=%zu\n", offsetof(CE77RegisterModificationInfo, NewR15));
    std::printf("offsetof.register_modification_info.new_of=%zu\n", offsetof(CE77RegisterModificationInfo, NewOf));
    std::printf("sizeof.exported_functions_prefix=%zu\n", sizeof(CE77ExportedFunctionsPrefix));
    std::printf("offsetof.exported_functions_prefix.register_function=%zu\n",
        offsetof(CE77ExportedFunctionsPrefix, RegisterFunction));
    std::printf("offsetof.exported_functions_prefix.get_address_from_pointer=%zu\n",
        offsetof(CE77ExportedFunctionsPrefix, GetAddressFromPointer));
    std::printf("sizeof.plugin_type6_popup_show=%zu\n", sizeof(CE77_BOOL));
    std::printf("calling_convention.classic_callbacks=__stdcall\n");
    std::printf("export.0=CEPlugin_GetVersion\n");
    std::printf("export.1=CEPlugin_InitializePlugin\n");
    std::printf("export.2=CEPlugin_DisablePlugin\n");
}
}

int wmain(int argc, wchar_t** argv)
{
    if (argc != 2)
    {
        std::fputs("usage: ce77-native-abi-probe.exe <ce77-native-abi-fixture.dll>\n", stderr);
        return 64;
    }

    HMODULE fixture = LoadLibraryW(argv[1]);
    if (fixture == nullptr)
    {
        std::fprintf(stderr, "LoadLibraryW failed: %lu\n", GetLastError());
        return 65;
    }

    int result = 0;
    do
    {
        auto getVersion = reinterpret_cast<CE77GetVersionExport>(GetProcAddress(fixture, "CEPlugin_GetVersion"));
        auto initialize = reinterpret_cast<CE77InitializePluginExport>(
            GetProcAddress(fixture, "CEPlugin_InitializePlugin"));
        auto disable = reinterpret_cast<CE77DisablePluginExport>(GetProcAddress(fixture, "CEPlugin_DisablePlugin"));
        if (getVersion == nullptr || initialize == nullptr || disable == nullptr)
        {
            std::fputs("fixture export lookup failed\n", stderr);
            result = 66;
            break;
        }

        std::array<unsigned char, sizeof(CE77PluginVersion) + 16> guardedVersion{};
        guardedVersion.fill(Guard);
        auto* version = reinterpret_cast<CE77PluginVersion*>(guardedVersion.data() + 8);
        bool sentinelsAreIntact = IsGuarded(guardedVersion.data(), 8) &&
            IsGuarded(guardedVersion.data() + 8 + sizeof(CE77PluginVersion), 8);
        bool paddingWasNotWritten = IsGuarded(guardedVersion.data() + 8 + 4, 4);
        bool getVersionSucceeded = getVersion(version, static_cast<int32_t>(sizeof(*version))) != 0 &&
            version->Version == 6 && std::strcmp(version->PluginName, "CE77 native ABI fixture") == 0;
        sentinelsAreIntact = sentinelsAreIntact && IsGuarded(guardedVersion.data(), 8) &&
            IsGuarded(guardedVersion.data() + 8 + sizeof(CE77PluginVersion), 8);

        CE77ExportedFunctionsPrefix prefix{};
        bool exportsSucceeded = initialize(&prefix, 0x10203040) != 0 && disable() != 0;
        if (!getVersionSucceeded || !sentinelsAreIntact || !paddingWasNotWritten || !exportsSucceeded)
        {
            std::fputs("fixture sentinel call failed\n", stderr);
            result = 67;
            break;
        }

        EmitFacts();
        std::printf("sentinel.plugin_version.outer_guard=passed\n");
        std::printf("sentinel.plugin_version.padding=passed\n");
        std::printf("sentinel.exports.return_values=passed\n");
    } while (false);

    FreeLibrary(fixture);
    return result;
}
