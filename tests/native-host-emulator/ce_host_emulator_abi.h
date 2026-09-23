// SPDX-License-Identifier: MIT
//
// The x64 hostfxr/managed-plugin ABI shapes the C2 native host emulator writes to and reads from. Transcribed from
// CheatEngine.SDK.Abi (source of authority, ADR-01), never from an installed Cheat Engine. This header does not
// include S-ABI's `tests/native-abi-fixture/ce77_plugin_abi_contract.h`: the emulator is a separate C2 evidence
// program with its own build and its own facts, and must not gain a compile-time dependency on the ABI fixture.
//
// Field names mirror the C# types so the emulator's static_assert lines can be parsed and compared against
// `sizeof(...)` of the managed records by CheatEngine.SDK.Hosting.Tests (see the "Emulator_abi_header_declares_the_
// managed_record_sizes" test).
//
//   CheatEngine.SDK.Abi.Managed.PluginInitRecord        (libs/CheatEngine.SDK.Abi/Managed/PluginInitRecord.cs)
//   CheatEngine.SDK.Abi.Managed.ManagedExportedFunctions (libs/CheatEngine.SDK.Abi/Managed/ManagedExportedFunctions.cs)
//   CheatEngine.SDK.Abi.PluginVersion                    (libs/CheatEngine.SDK.Abi/PluginVersion.cs)

#pragma once

#include <stddef.h>
#include <stdint.h>

#if !defined(_WIN64)
#error The native host emulator is deliberately Windows x64 only.
#endif

#define CE_HOST_EMULATOR_SCHEMA 1

typedef int32_t CeBool32;
typedef uint8_t CeBool8;

#define CE_STDCALL __stdcall

// PluginVersion (libs/CheatEngine.SDK.Abi/PluginVersion.cs): natural alignment, 16 bytes.
#pragma pack(push, 8)
typedef struct CePluginVersion
{
	uint32_t Version;
	char* PluginName;
} CePluginVersion;
#pragma pack(pop)

// Function pointer shapes the managed bootstrap writes into CePluginInitRecord (declared before the record so the
// record's fields can be the correctly typed, directly callable pointers, not opaque `void*`). The x64 Microsoft
// calling convention is one ABI regardless of the __stdcall/__cdecl keyword; the keyword documents intent only.
typedef CeBool32(CE_STDCALL* CeGetVersionFn)(CePluginVersion*, int32_t);
typedef struct CeManagedExportedFunctions CeManagedExportedFunctions;
typedef CeBool32(CE_STDCALL* CeEnablePluginFn)(CeManagedExportedFunctions*, uint32_t);
typedef CeBool32(CE_STDCALL* CeDisablePluginFn)(void);

// Function pointer shapes the emulator implements and places into CeManagedExportedFunctions.
typedef void* (CE_STDCALL* CeGetLuaStateFn)(void);
typedef void(CE_STDCALL* CeLuaPushClassInstanceFn)(void*, void*);
typedef void(CE_STDCALL* CeProcessMessagesFn)(void);
typedef CeBool8(CE_STDCALL* CeCheckSynchronizeFn)(int32_t);

// The default hostfxr component entry-point shape (coreclr_delegates.h `component_entry_point_fn`): the generated
// `CESDK.CESDK.CEPluginInitialize(IntPtr, int)` bootstrap method.
typedef int32_t(__stdcall* CeComponentEntryPointFn)(void*, int32_t);

// PluginInitRecord (libs/CheatEngine.SDK.Abi/Managed/PluginInitRecord.cs): the managed plugin's bootstrap writes this,
// byte-packed, 36 bytes, no tail padding. Every field here is directly callable/usable, unlike the fixture's own
// deliberately generic ABI contract header, because this emulator calls through every one of them.
#pragma pack(push, 1)
typedef struct CePluginInitRecord
{
	char* Name;
	CeGetVersionFn GetVersion;
	CeEnablePluginFn EnablePlugin;
	CeDisablePluginFn DisablePlugin;
	uint32_t Version;
} CePluginInitRecord;
#pragma pack(pop)

// ManagedExportedFunctions (libs/CheatEngine.SDK.Abi/Managed/ManagedExportedFunctions.cs): the emulator (host)
// fills this in and hands it to EnablePlugin. Natural alignment, 48 bytes. `LuaRegister` stays `void*`: the managed
// side declares it untyped on purpose ("do not call"), so this mirror keeps it untyped too.
#pragma pack(push, 8)
struct CeManagedExportedFunctions
{
	int32_t SizeOfExportedFunctions;
	CeGetLuaStateFn GetLuaState;
	void* LuaRegister;
	CeLuaPushClassInstanceFn LuaPushClassInstance;
	CeProcessMessagesFn ProcessMessages;
	CeCheckSynchronizeFn CheckSynchronize;
};
#pragma pack(pop)

#ifdef __cplusplus
static_assert(sizeof(void*) == 8, "The emulator requires x64 pointers.");

static_assert(sizeof(CePluginVersion) == 16, "PluginVersion is 16 bytes on x64.");
static_assert(offsetof(CePluginVersion, Version) == 0, "PluginVersion.Version is at offset 0.");
static_assert(offsetof(CePluginVersion, PluginName) == 8, "PluginVersion.PluginName is at offset 8.");

static_assert(sizeof(CePluginInitRecord) == 36, "PluginInitRecord is 36 bytes, byte-packed, on x64.");
static_assert(offsetof(CePluginInitRecord, Name) == 0, "PluginInitRecord.Name is at offset 0.");
static_assert(offsetof(CePluginInitRecord, GetVersion) == 8, "PluginInitRecord.GetVersion is at offset 8.");
static_assert(offsetof(CePluginInitRecord, EnablePlugin) == 16, "PluginInitRecord.EnablePlugin is at offset 16.");
static_assert(offsetof(CePluginInitRecord, DisablePlugin) == 24, "PluginInitRecord.DisablePlugin is at offset 24.");
static_assert(offsetof(CePluginInitRecord, Version) == 32, "PluginInitRecord.Version is at offset 32.");

static_assert(sizeof(CeManagedExportedFunctions) == 48, "ManagedExportedFunctions is 48 bytes, naturally aligned, on x64.");
static_assert(offsetof(CeManagedExportedFunctions, SizeOfExportedFunctions) == 0,
	"ManagedExportedFunctions.SizeOfExportedFunctions is at offset 0.");
static_assert(offsetof(CeManagedExportedFunctions, GetLuaState) == 8, "ManagedExportedFunctions.GetLuaState is at offset 8.");
static_assert(offsetof(CeManagedExportedFunctions, LuaRegister) == 16, "ManagedExportedFunctions.LuaRegister is at offset 16.");
static_assert(offsetof(CeManagedExportedFunctions, LuaPushClassInstance) == 24,
	"ManagedExportedFunctions.LuaPushClassInstance is at offset 24.");
static_assert(offsetof(CeManagedExportedFunctions, ProcessMessages) == 32,
	"ManagedExportedFunctions.ProcessMessages is at offset 32.");
static_assert(offsetof(CeManagedExportedFunctions, CheckSynchronize) == 40,
	"ManagedExportedFunctions.CheckSynchronize is at offset 40.");
#endif
