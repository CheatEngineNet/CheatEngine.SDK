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
char ExpectedPluginName[] = "CE77 native ABI fixture";

void CE77_STDCALL FixtureShowMessage(char* message)
{
    (void)message;
}

CE77_UINT_PTR CE77_STDCALL FixtureGetAddressFromPointer(CE77_UINT_PTR baseAddress, int32_t offsetCount,
    int32_t* offsets)
{
    (void)offsetCount;
    (void)offsets;
    return baseAddress;
}

CE77_ULONG FixtureProcessId = 0x2468;
CE77_HANDLE FixtureProcessHandle = reinterpret_cast<CE77_HANDLE>(static_cast<uintptr_t>(0x12345678));

CE77ExportedFunctionsPrefix CreateTopologyFixture()
{
    CE77ExportedFunctionsPrefix prefix{};
    prefix.SizeOfExportedFunctions = static_cast<int32_t>(sizeof(prefix));
    prefix.ShowMessage = &FixtureShowMessage;
    prefix.OpenedProcessId = &FixtureProcessId;
    prefix.OpenedProcessHandle = &FixtureProcessHandle;
    prefix.FixMemory = nullptr;
    prefix.GetAddressFromPointer = &FixtureGetAddressFromPointer;
    return prefix;
}

bool IsGuarded(const unsigned char* bytes, size_t count)
{
    for (size_t index = 0; index < count; index++)
    {
        if (bytes[index] != Guard) return false;
    }

    return true;
}

// This local negative fixture satisfies the version-export contract except for
// corrupting the four padding bytes between Version and PluginName. It proves
// that the probe observes the padding only after the export returns.
CE77_BOOL CE77_STDCALL PaddingWritingGetVersion(CE77PluginVersion* version, int32_t versionSize)
{
    if (version == nullptr || versionSize < static_cast<int32_t>(sizeof(CE77PluginVersion))) return 0;

    version->Version = 6;
    auto* bytes = reinterpret_cast<unsigned char*>(version);
    bytes[sizeof(version->Version)] = 0;
    version->PluginName = ExpectedPluginName;
    return 1;
}

bool ValidateGetVersion(CE77GetVersionExport getVersion)
{
    std::array<unsigned char, sizeof(CE77PluginVersion) + 16> guardedVersion{};
    guardedVersion.fill(Guard);
    auto* version = reinterpret_cast<CE77PluginVersion*>(guardedVersion.data() + 8);
    bool sentinelsAreIntact = IsGuarded(guardedVersion.data(), 8) &&
        IsGuarded(guardedVersion.data() + 8 + sizeof(CE77PluginVersion), 8);
    bool getVersionSucceeded = getVersion(version, static_cast<int32_t>(sizeof(*version))) != 0 &&
        version->Version == 6 && std::strcmp(version->PluginName, ExpectedPluginName) == 0;
    bool paddingWasNotWritten = IsGuarded(guardedVersion.data() + 8 + sizeof(version->Version),
        offsetof(CE77PluginVersion, PluginName) - sizeof(version->Version));
    sentinelsAreIntact = sentinelsAreIntact && IsGuarded(guardedVersion.data(), 8) &&
        IsGuarded(guardedVersion.data() + 8 + sizeof(CE77PluginVersion), 8);

    return getVersionSucceeded && sentinelsAreIntact && paddingWasNotWritten;
}

void EmitFacts()
{
#define EMIT_LAYOUT(key, type) \
    std::printf("sizeof." key "=%zu\n", sizeof(type)); \
    std::printf("alignof." key "=%zu\n", alignof(type))
#define EMIT_OFFSET(key, type, field) \
    std::printf("offsetof." key "." #field "=%zu\n", offsetof(type, field))

    std::printf("fixture.schema=%d\n", CE77_ABI_FIXTURE_SCHEMA_VERSION);
    std::printf("source.upstream_commit=%s\n", CE77_UPSTREAM_COMMIT);
    std::printf("source.path=%s\n", CE77_CEPLUGINSDK_PATH);
    std::printf("source.contract=transcribed-pinned-header-subset\n");
    std::printf("architecture=win-x64\n");
    std::printf("sizeof.pointer=%zu\n", sizeof(void*));
    std::printf("sizeof.bool=%zu\n", sizeof(CE77_BOOL));
    std::printf("sizeof.uint_ptr=%zu\n", sizeof(CE77_UINT_PTR));
    std::printf("sizeof.plugin_type=%zu\n", sizeof(CE77PluginType));
    std::printf("sizeof.auto_assembler_phase=%zu\n", sizeof(CE77AutoAssemblerPhase));

    EMIT_LAYOUT("plugin_version", CE77PluginVersion);
    EMIT_OFFSET("plugin_version", CE77PluginVersion, Version);
    EMIT_OFFSET("plugin_version", CE77PluginVersion, PluginName);

    EMIT_LAYOUT("plugin_type0_record", CE77PluginType0Record);
    EMIT_OFFSET("plugin_type0_record", CE77PluginType0Record, InterpretedAddress);
    EMIT_OFFSET("plugin_type0_record", CE77PluginType0Record, Address);
    EMIT_OFFSET("plugin_type0_record", CE77PluginType0Record, IsPointer);
    EMIT_OFFSET("plugin_type0_record", CE77PluginType0Record, CountOffsets);
    EMIT_OFFSET("plugin_type0_record", CE77PluginType0Record, Offsets);
    EMIT_OFFSET("plugin_type0_record", CE77PluginType0Record, Description);
    EMIT_OFFSET("plugin_type0_record", CE77PluginType0Record, ValueType);
    EMIT_OFFSET("plugin_type0_record", CE77PluginType0Record, Size);

    EMIT_LAYOUT("plugin_type0_init", CE77PluginType0Init);
    EMIT_OFFSET("plugin_type0_init", CE77PluginType0Init, Name);
    EMIT_OFFSET("plugin_type0_init", CE77PluginType0Init, Callback);
    EMIT_LAYOUT("plugin_type1_init", CE77PluginType1Init);
    EMIT_OFFSET("plugin_type1_init", CE77PluginType1Init, Name);
    EMIT_OFFSET("plugin_type1_init", CE77PluginType1Init, Callback);
    EMIT_OFFSET("plugin_type1_init", CE77PluginType1Init, Shortcut);
    EMIT_LAYOUT("plugin_type2_init", CE77PluginType2Init);
    EMIT_OFFSET("plugin_type2_init", CE77PluginType2Init, Callback);
    EMIT_LAYOUT("plugin_type3_init", CE77PluginType3Init);
    EMIT_OFFSET("plugin_type3_init", CE77PluginType3Init, Callback);
    EMIT_LAYOUT("plugin_type4_init", CE77PluginType4Init);
    EMIT_OFFSET("plugin_type4_init", CE77PluginType4Init, Callback);
    EMIT_LAYOUT("plugin_type5_init", CE77PluginType5Init);
    EMIT_OFFSET("plugin_type5_init", CE77PluginType5Init, Name);
    EMIT_OFFSET("plugin_type5_init", CE77PluginType5Init, Callback);
    EMIT_OFFSET("plugin_type5_init", CE77PluginType5Init, Shortcut);
    EMIT_LAYOUT("plugin_type6_init", CE77PluginType6Init);
    EMIT_OFFSET("plugin_type6_init", CE77PluginType6Init, Name);
    EMIT_OFFSET("plugin_type6_init", CE77PluginType6Init, Callback);
    EMIT_OFFSET("plugin_type6_init", CE77PluginType6Init, CallbackOnPopup);
    EMIT_OFFSET("plugin_type6_init", CE77PluginType6Init, Shortcut);
    EMIT_LAYOUT("plugin_type7_init", CE77PluginType7Init);
    EMIT_OFFSET("plugin_type7_init", CE77PluginType7Init, Callback);
    EMIT_LAYOUT("plugin_type8_init", CE77PluginType8Init);
    EMIT_OFFSET("plugin_type8_init", CE77PluginType8Init, Callback);

    EMIT_LAYOUT("register_modification_info", CE77RegisterModificationInfo);
    EMIT_OFFSET("register_modification_info", CE77RegisterModificationInfo, Address);
    EMIT_OFFSET("register_modification_info", CE77RegisterModificationInfo, ChangeEax);
    EMIT_OFFSET("register_modification_info", CE77RegisterModificationInfo, ChangeR15);
    EMIT_OFFSET("register_modification_info", CE77RegisterModificationInfo, ChangeOf);
    EMIT_OFFSET("register_modification_info", CE77RegisterModificationInfo, NewEax);
    EMIT_OFFSET("register_modification_info", CE77RegisterModificationInfo, NewR15);
    EMIT_OFFSET("register_modification_info", CE77RegisterModificationInfo, NewCf);
    EMIT_OFFSET("register_modification_info", CE77RegisterModificationInfo, NewOf);

    EMIT_LAYOUT("exported_functions_prefix", CE77ExportedFunctionsPrefix);
    EMIT_OFFSET("exported_functions_prefix", CE77ExportedFunctionsPrefix, SizeOfExportedFunctions);
    EMIT_OFFSET("exported_functions_prefix", CE77ExportedFunctionsPrefix, ShowMessage);
    EMIT_OFFSET("exported_functions_prefix", CE77ExportedFunctionsPrefix, RegisterFunction);
    EMIT_OFFSET("exported_functions_prefix", CE77ExportedFunctionsPrefix, UnregisterFunction);
    EMIT_OFFSET("exported_functions_prefix", CE77ExportedFunctionsPrefix, OpenedProcessId);
    EMIT_OFFSET("exported_functions_prefix", CE77ExportedFunctionsPrefix, OpenedProcessHandle);
    EMIT_OFFSET("exported_functions_prefix", CE77ExportedFunctionsPrefix, GetMainWindowHandle);
    EMIT_OFFSET("exported_functions_prefix", CE77ExportedFunctionsPrefix, AutoAssemble);
    EMIT_OFFSET("exported_functions_prefix", CE77ExportedFunctionsPrefix, Assembler);
    EMIT_OFFSET("exported_functions_prefix", CE77ExportedFunctionsPrefix, Disassembler);
    EMIT_OFFSET("exported_functions_prefix", CE77ExportedFunctionsPrefix, ChangeRegistersAtAddress);
    EMIT_OFFSET("exported_functions_prefix", CE77ExportedFunctionsPrefix, InjectDll);
    EMIT_OFFSET("exported_functions_prefix", CE77ExportedFunctionsPrefix, FreezeMemory);
    EMIT_OFFSET("exported_functions_prefix", CE77ExportedFunctionsPrefix, UnfreezeMemory);
    EMIT_OFFSET("exported_functions_prefix", CE77ExportedFunctionsPrefix, FixMemory);
    EMIT_OFFSET("exported_functions_prefix", CE77ExportedFunctionsPrefix, ProcessList);
    EMIT_OFFSET("exported_functions_prefix", CE77ExportedFunctionsPrefix, ReloadSettings);
    EMIT_OFFSET("exported_functions_prefix", CE77ExportedFunctionsPrefix, GetAddressFromPointer);

    std::printf("sizeof.plugin_type6_popup_show=%zu\n", sizeof(CE77_BOOL));
    std::printf("calling_convention.classic_callbacks=__stdcall\n");
    auto topology = CreateTopologyFixture();
    bool topologyIsValid = topology.ShowMessage != nullptr && topology.OpenedProcessId != nullptr &&
        *topology.OpenedProcessId == FixtureProcessId && topology.OpenedProcessHandle != nullptr &&
        *topology.OpenedProcessHandle == FixtureProcessHandle && topology.FixMemory == nullptr &&
        topology.GetAddressFromPointer != nullptr;
    std::printf("table.direct_slot=non-null-address-not-invoked\n");
    std::printf("table.process_id_cell=0x%X\n", *topology.OpenedProcessId);
    std::printf("table.process_handle_cell=0x%llX\n",
        static_cast<unsigned long long>(reinterpret_cast<uintptr_t>(*topology.OpenedProcessHandle)));
    std::printf("table.fixmem=null-not-invoked\n");
    std::printf("table.get_address_from_pointer=conflicted-not-invoked\n");
    std::printf("table.hookable_suffix=outside-prefix-not-dereferenced\n");
    std::printf("table.topology=%s\n", topologyIsValid ? "passed" : "failed");
    std::printf("export.0=CEPlugin_GetVersion\n");
    std::printf("export.1=CEPlugin_InitializePlugin\n");
    std::printf("export.2=CEPlugin_DisablePlugin\n");

#undef EMIT_OFFSET
#undef EMIT_LAYOUT
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

        bool paddingNegativeTestRejected = !ValidateGetVersion(&PaddingWritingGetVersion);
        bool getVersionSucceeded = ValidateGetVersion(getVersion);

        CE77ExportedFunctionsPrefix prefix{};
        bool exportsSucceeded = initialize(&prefix, 0x10203040) != 0 && disable() != 0;
        if (!paddingNegativeTestRejected || !getVersionSucceeded || !exportsSucceeded)
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
