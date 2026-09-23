// SPDX-License-Identifier: MIT
// Emits the classic and managed CE plugin ABI facts after checking the fixture's three
// exports and the guarded by-value write of the packed managed bootstrap record.

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

// The natural-alignment mirror of the packed host bootstrap record, as the
// historical C# template declares it: 40 bytes, four more than the host's 36.
struct NaturallyAlignedInitRecordMirror
{
    char* Name;
    void* GetVersion;
    void* EnablePlugin;
    void* DisablePlugin;
    uint32_t Version;
};

static_assert(sizeof(NaturallyAlignedInitRecordMirror) == 40, "The natural mirror is the 40-byte defect.");

// Copies writeSize bytes of a filled record into a guarded buffer at an odd
// address, exactly like a host that copies the record by value, and reports
// whether every guard byte around the 36-byte host slot kept its value. The
// buffer is 8-byte aligned, so the destination one byte in is always odd.
bool CopyLeavesInitRecordGuardsIntact(const void* source, size_t writeSize)
{
    constexpr size_t LeadingGuard = 1;
    constexpr size_t TrailingGuard = 8;
    alignas(8) std::array<unsigned char, LeadingGuard + sizeof(CE77ManagedPluginInitRecord) + TrailingGuard> buffer{};
    buffer.fill(Guard);
    unsigned char* destination = buffer.data() + LeadingGuard;
    std::memcpy(destination, source, writeSize);
    return IsGuarded(buffer.data(), LeadingGuard) &&
        IsGuarded(destination + sizeof(CE77ManagedPluginInitRecord), TrailingGuard);
}

// Positive: the packed 36-byte record copied by value leaves every guard intact.
// Negative self-test: the 40-byte natural mirror copied the same way must be
// detected, otherwise the guard check itself is broken and the probe fails.
bool ValidatePackedInitRecordWrite()
{
    CE77ManagedPluginInitRecord packed{};
    packed.Name = ExpectedPluginName;
    packed.GetVersion = reinterpret_cast<void*>(static_cast<uintptr_t>(0x0101010101010101ull));
    packed.EnablePlugin = reinterpret_cast<void*>(static_cast<uintptr_t>(0x0202020202020202ull));
    packed.DisablePlugin = reinterpret_cast<void*>(static_cast<uintptr_t>(0x0303030303030303ull));
    packed.Version = 6;

    NaturallyAlignedInitRecordMirror natural{};
    natural.Name = packed.Name;
    natural.GetVersion = packed.GetVersion;
    natural.EnablePlugin = packed.EnablePlugin;
    natural.DisablePlugin = packed.DisablePlugin;
    natural.Version = packed.Version;

    bool packedWriteIsContained = CopyLeavesInitRecordGuardsIntact(&packed, sizeof(packed));
    bool naturalMirrorOverrunIsDetected = !CopyLeavesInitRecordGuardsIntact(&natural, sizeof(natural));
    return packedWriteIsContained && naturalMirrorOverrunIsDetected;
}

void EmitFacts()
{
#define EMIT_LAYOUT(key, type) \
    std::printf("sizeof." key "=%zu\n", sizeof(type)); \
    std::printf("alignof." key "=%zu\n", alignof(type))
#define EMIT_FIELD(key, type, field) \
    std::printf("offsetof." key "." #field "=%zu\n", offsetof(type, field)); \
    std::printf("fieldsize." key "." #field "=%zu\n", sizeof(((type*)0)->field))

    std::printf("fixture.schema=%d\n", CE77_ABI_FIXTURE_SCHEMA_VERSION);
    std::printf("source.upstream_commit=%s\n", CE77_UPSTREAM_COMMIT);
    std::printf("source.path=%s\n", CE77_CEPLUGINSDK_PATH);
    std::printf("source.host_path=%s\n", CE77_PLUGIN_PAS_PATH);
    std::printf("source.mirror_path=%s\n", CE77_CEPLUGINSDK_PAS_PATH);
    std::printf("source.contract=transcribed-pinned-header-and-host-pascal-subset\n");
    std::printf("architecture=win-x64\n");
    std::printf("sizeof.pointer=%zu\n", sizeof(void*));
    std::printf("sizeof.bool=%zu\n", sizeof(CE77_BOOL));
    std::printf("sizeof.uint_ptr=%zu\n", sizeof(CE77_UINT_PTR));
    std::printf("sizeof.plugin_type=%zu\n", sizeof(CE77PluginType));
    std::printf("sizeof.auto_assembler_phase=%zu\n", sizeof(CE77AutoAssemblerPhase));

    EMIT_LAYOUT("plugin_version", CE77PluginVersion);
    EMIT_FIELD("plugin_version", CE77PluginVersion, Version);
    EMIT_FIELD("plugin_version", CE77PluginVersion, PluginName);

    EMIT_LAYOUT("plugin_type0_record", CE77PluginType0Record);
    EMIT_FIELD("plugin_type0_record", CE77PluginType0Record, InterpretedAddress);
    EMIT_FIELD("plugin_type0_record", CE77PluginType0Record, Address);
    EMIT_FIELD("plugin_type0_record", CE77PluginType0Record, IsPointer);
    EMIT_FIELD("plugin_type0_record", CE77PluginType0Record, CountOffsets);
    EMIT_FIELD("plugin_type0_record", CE77PluginType0Record, Offsets);
    EMIT_FIELD("plugin_type0_record", CE77PluginType0Record, Description);
    EMIT_FIELD("plugin_type0_record", CE77PluginType0Record, ValueType);
    EMIT_FIELD("plugin_type0_record", CE77PluginType0Record, Size);

    EMIT_LAYOUT("plugin_type0_init", CE77PluginType0Init);
    EMIT_FIELD("plugin_type0_init", CE77PluginType0Init, Name);
    EMIT_FIELD("plugin_type0_init", CE77PluginType0Init, Callback);
    EMIT_LAYOUT("plugin_type1_init", CE77PluginType1Init);
    EMIT_FIELD("plugin_type1_init", CE77PluginType1Init, Name);
    EMIT_FIELD("plugin_type1_init", CE77PluginType1Init, Callback);
    EMIT_FIELD("plugin_type1_init", CE77PluginType1Init, Shortcut);
    EMIT_LAYOUT("plugin_type2_init", CE77PluginType2Init);
    EMIT_FIELD("plugin_type2_init", CE77PluginType2Init, Callback);
    EMIT_LAYOUT("plugin_type3_init", CE77PluginType3Init);
    EMIT_FIELD("plugin_type3_init", CE77PluginType3Init, Callback);
    EMIT_LAYOUT("plugin_type4_init", CE77PluginType4Init);
    EMIT_FIELD("plugin_type4_init", CE77PluginType4Init, Callback);
    EMIT_LAYOUT("plugin_type5_init", CE77PluginType5Init);
    EMIT_FIELD("plugin_type5_init", CE77PluginType5Init, Name);
    EMIT_FIELD("plugin_type5_init", CE77PluginType5Init, Callback);
    EMIT_FIELD("plugin_type5_init", CE77PluginType5Init, Shortcut);
    EMIT_LAYOUT("plugin_type6_init", CE77PluginType6Init);
    EMIT_FIELD("plugin_type6_init", CE77PluginType6Init, Name);
    EMIT_FIELD("plugin_type6_init", CE77PluginType6Init, Callback);
    EMIT_FIELD("plugin_type6_init", CE77PluginType6Init, CallbackOnPopup);
    EMIT_FIELD("plugin_type6_init", CE77PluginType6Init, Shortcut);
    EMIT_LAYOUT("plugin_type7_init", CE77PluginType7Init);
    EMIT_FIELD("plugin_type7_init", CE77PluginType7Init, Callback);
    EMIT_LAYOUT("plugin_type8_init", CE77PluginType8Init);
    EMIT_FIELD("plugin_type8_init", CE77PluginType8Init, Callback);

    EMIT_LAYOUT("register_modification_info", CE77RegisterModificationInfo);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, Address);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, ChangeEax);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, ChangeEbx);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, ChangeEcx);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, ChangeEdx);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, ChangeEsi);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, ChangeEdi);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, ChangeEbp);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, ChangeEsp);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, ChangeEip);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, ChangeR8);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, ChangeR9);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, ChangeR10);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, ChangeR11);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, ChangeR12);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, ChangeR13);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, ChangeR14);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, ChangeR15);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, ChangeCf);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, ChangePf);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, ChangeAf);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, ChangeZf);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, ChangeSf);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, ChangeOf);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, NewEax);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, NewEbx);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, NewEcx);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, NewEdx);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, NewEsi);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, NewEdi);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, NewEbp);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, NewEsp);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, NewEip);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, NewR8);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, NewR9);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, NewR10);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, NewR11);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, NewR12);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, NewR13);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, NewR14);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, NewR15);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, NewCf);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, NewPf);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, NewAf);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, NewZf);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, NewSf);
    EMIT_FIELD("register_modification_info", CE77RegisterModificationInfo, NewOf);

    EMIT_LAYOUT("exported_functions_prefix", CE77ExportedFunctionsPrefix);
    EMIT_FIELD("exported_functions_prefix", CE77ExportedFunctionsPrefix, SizeOfExportedFunctions);
    EMIT_FIELD("exported_functions_prefix", CE77ExportedFunctionsPrefix, ShowMessage);
    EMIT_FIELD("exported_functions_prefix", CE77ExportedFunctionsPrefix, RegisterFunction);
    EMIT_FIELD("exported_functions_prefix", CE77ExportedFunctionsPrefix, UnregisterFunction);
    EMIT_FIELD("exported_functions_prefix", CE77ExportedFunctionsPrefix, OpenedProcessId);
    EMIT_FIELD("exported_functions_prefix", CE77ExportedFunctionsPrefix, OpenedProcessHandle);
    EMIT_FIELD("exported_functions_prefix", CE77ExportedFunctionsPrefix, GetMainWindowHandle);
    EMIT_FIELD("exported_functions_prefix", CE77ExportedFunctionsPrefix, AutoAssemble);
    EMIT_FIELD("exported_functions_prefix", CE77ExportedFunctionsPrefix, Assembler);
    EMIT_FIELD("exported_functions_prefix", CE77ExportedFunctionsPrefix, Disassembler);
    EMIT_FIELD("exported_functions_prefix", CE77ExportedFunctionsPrefix, ChangeRegistersAtAddress);
    EMIT_FIELD("exported_functions_prefix", CE77ExportedFunctionsPrefix, InjectDll);
    EMIT_FIELD("exported_functions_prefix", CE77ExportedFunctionsPrefix, FreezeMemory);
    EMIT_FIELD("exported_functions_prefix", CE77ExportedFunctionsPrefix, UnfreezeMemory);
    EMIT_FIELD("exported_functions_prefix", CE77ExportedFunctionsPrefix, FixMemory);
    EMIT_FIELD("exported_functions_prefix", CE77ExportedFunctionsPrefix, ProcessList);
    EMIT_FIELD("exported_functions_prefix", CE77ExportedFunctionsPrefix, ReloadSettings);
    EMIT_FIELD("exported_functions_prefix", CE77ExportedFunctionsPrefix, GetAddressFromPointer);

    EMIT_LAYOUT("managed_plugin_init_record", CE77ManagedPluginInitRecord);
    EMIT_FIELD("managed_plugin_init_record", CE77ManagedPluginInitRecord, Name);
    EMIT_FIELD("managed_plugin_init_record", CE77ManagedPluginInitRecord, GetVersion);
    EMIT_FIELD("managed_plugin_init_record", CE77ManagedPluginInitRecord, EnablePlugin);
    EMIT_FIELD("managed_plugin_init_record", CE77ManagedPluginInitRecord, DisablePlugin);
    EMIT_FIELD("managed_plugin_init_record", CE77ManagedPluginInitRecord, Version);

    EMIT_LAYOUT("managed_exported_functions", CE77ManagedExportedFunctions);
    EMIT_FIELD("managed_exported_functions", CE77ManagedExportedFunctions, SizeOfExportedFunctions);
    EMIT_FIELD("managed_exported_functions", CE77ManagedExportedFunctions, GetLuaState);
    EMIT_FIELD("managed_exported_functions", CE77ManagedExportedFunctions, LuaRegister);
    EMIT_FIELD("managed_exported_functions", CE77ManagedExportedFunctions, LuaPushClassInstance);
    EMIT_FIELD("managed_exported_functions", CE77ManagedExportedFunctions, ProcessMessages);
    EMIT_FIELD("managed_exported_functions", CE77ManagedExportedFunctions, CheckSynchronize);

    EMIT_LAYOUT("host_plugin0_selected_record", CE77HostPlugin0SelectedRecord);
    EMIT_FIELD("host_plugin0_selected_record", CE77HostPlugin0SelectedRecord, InterpretedAddress);
    EMIT_FIELD("host_plugin0_selected_record", CE77HostPlugin0SelectedRecord, Address);
    EMIT_FIELD("host_plugin0_selected_record", CE77HostPlugin0SelectedRecord, IsPointer);
    EMIT_FIELD("host_plugin0_selected_record", CE77HostPlugin0SelectedRecord, CountOffsets);
    EMIT_FIELD("host_plugin0_selected_record", CE77HostPlugin0SelectedRecord, Offsets);
    EMIT_FIELD("host_plugin0_selected_record", CE77HostPlugin0SelectedRecord, Description);
    EMIT_FIELD("host_plugin0_selected_record", CE77HostPlugin0SelectedRecord, ValueType);
    EMIT_FIELD("host_plugin0_selected_record", CE77HostPlugin0SelectedRecord, Size);

    EMIT_LAYOUT("pascal_dword_mirror_selected_record", CE77PascalDwordMirrorSelectedRecord);
    EMIT_FIELD("pascal_dword_mirror_selected_record", CE77PascalDwordMirrorSelectedRecord, InterpretedAddress);
    EMIT_FIELD("pascal_dword_mirror_selected_record", CE77PascalDwordMirrorSelectedRecord, Address);
    EMIT_FIELD("pascal_dword_mirror_selected_record", CE77PascalDwordMirrorSelectedRecord, IsPointer);
    EMIT_FIELD("pascal_dword_mirror_selected_record", CE77PascalDwordMirrorSelectedRecord, CountOffsets);
    EMIT_FIELD("pascal_dword_mirror_selected_record", CE77PascalDwordMirrorSelectedRecord, Offsets);
    EMIT_FIELD("pascal_dword_mirror_selected_record", CE77PascalDwordMirrorSelectedRecord, Description);
    EMIT_FIELD("pascal_dword_mirror_selected_record", CE77PascalDwordMirrorSelectedRecord, ValueType);
    EMIT_FIELD("pascal_dword_mirror_selected_record", CE77PascalDwordMirrorSelectedRecord, Size);

    EMIT_LAYOUT("pascal_boolean_mirror_selected_record", CE77PascalBooleanMirrorSelectedRecord);
    EMIT_FIELD("pascal_boolean_mirror_selected_record", CE77PascalBooleanMirrorSelectedRecord, InterpretedAddress);
    EMIT_FIELD("pascal_boolean_mirror_selected_record", CE77PascalBooleanMirrorSelectedRecord, Address);
    EMIT_FIELD("pascal_boolean_mirror_selected_record", CE77PascalBooleanMirrorSelectedRecord, IsPointer);
    EMIT_FIELD("pascal_boolean_mirror_selected_record", CE77PascalBooleanMirrorSelectedRecord, CountOffsets);
    EMIT_FIELD("pascal_boolean_mirror_selected_record", CE77PascalBooleanMirrorSelectedRecord, Offsets);
    EMIT_FIELD("pascal_boolean_mirror_selected_record", CE77PascalBooleanMirrorSelectedRecord, Description);
    EMIT_FIELD("pascal_boolean_mirror_selected_record", CE77PascalBooleanMirrorSelectedRecord, ValueType);
    EMIT_FIELD("pascal_boolean_mirror_selected_record", CE77PascalBooleanMirrorSelectedRecord, Size);
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

#undef EMIT_FIELD
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
        bool packedInitRecordWriteIsContained = ValidatePackedInitRecordWrite();

        CE77ExportedFunctionsPrefix prefix{};
        bool exportsSucceeded = initialize(&prefix, 0x10203040) != 0 && disable() != 0;
        if (!paddingNegativeTestRejected || !getVersionSucceeded || !exportsSucceeded ||
            !packedInitRecordWriteIsContained)
        {
            std::fputs("fixture sentinel call failed\n", stderr);
            result = 67;
            break;
        }

        EmitFacts();
        std::printf("sentinel.plugin_version.outer_guard=passed\n");
        std::printf("sentinel.plugin_version.padding=passed\n");
        std::printf("sentinel.exports.return_values=passed\n");
        std::printf("sentinel.managed_plugin_init_record.tail_guard=passed\n");
    } while (false);

    FreeLibrary(fixture);
    return result;
}
