// SPDX-License-Identifier: MIT
//
// Minimal classic- and managed-plugin ABI contract for an x64 fixture, transcribed
// from pinned Cheat Engine sources (names, types and line ranges only).
//
// Provenance (do not replace this with a locally installed header):
//   Cheat Engine upstream commit ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37
//   Cheat Engine/plugin/cepluginsdk.h (SHA-256 b6500df1e94d7bb011b38e173b2603197b7a1f304496d751ede82e57e36e532f),
//     lines 15-160, 163-180 and 271-456: PluginVersion, PLUGINTYPE0_RECORD, the nine init records,
//     REGISTERMODIFICATIONINFO, the physical ExportedFunctions prefix and the three exports.
//   Cheat Engine/plugin.pas (SHA-256 358f51a39ad14d00ecba3c9137f440152d4ab85f1d2498068fa81fca906d09db), the host
//     authority: TPluginDotNetInitResult (packed record, lines 29-36) -> CE77ManagedPluginInitRecord,
//     TExportedFunctionsDotNetV1 (lines 38-45) -> CE77ManagedExportedFunctions, TPlugin0_SelectedRecord (lines
//     726-735) -> CE77HostPlugin0SelectedRecord.
//   Cheat Engine/plugin/cepluginsdk.pas (SHA-256 cda5269f441120e5a3bff2f87e289cd71de9158ca2a619c7d0a734eb98ee6052),
//     the Pascal kit mirror ({$MODE Delphi}, line 3; natural alignment deduced): TPlugin0_SelectedRecord (lines
//     161-170, address: dword) -> CE77PascalDwordMirrorSelectedRecord and TSelectedRecord (lines 147-156,
//     ispointer: boolean) -> CE77PascalBooleanMirrorSelectedRecord. Both mirrors are known-wrong for x64 and are
//     transcribed only as negative oracles.
//   https://github.com/cheat-engine/cheat-engine/tree/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37
//
// This is intentionally not a vendored copy of any of those files.  It carries only
// the declarations that the fixture measures, with the C# field names of
// CheatEngine.SDK.Abi.  The fixture is an x64 MSVC build; Windows SDK headers
// supply no CE declarations and no CE installation is consulted.

#pragma once

#include <stddef.h>
#include <stdint.h>

#if !defined(_WIN64)
#error The CE 7.7 fixture is deliberately Windows x64 only.
#endif

#define CE77_ABI_FIXTURE_SCHEMA_VERSION 3
#define CE77_UPSTREAM_COMMIT "ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37"
#define CE77_CEPLUGINSDK_PATH "Cheat Engine/plugin/cepluginsdk.h"
#define CE77_PLUGIN_PAS_PATH "Cheat Engine/plugin.pas"
#define CE77_CEPLUGINSDK_PAS_PATH "Cheat Engine/plugin/cepluginsdk.pas"

typedef int32_t CE77_BOOL;
typedef uint32_t CE77_ULONG;
typedef uint8_t CE77_BYTE;
typedef uintptr_t CE77_UINT_PTR;
typedef void* CE77_HANDLE;

#define CE77_STDCALL __stdcall

typedef enum CE77PluginType
{
    CE77PluginTypeAddressList = 0,
    CE77PluginTypeMemoryView = 1,
    CE77PluginTypeOnDebugEvent = 2,
    CE77PluginTypeProcessWatcherEvent = 3,
    CE77PluginTypeFunctionPointerChange = 4,
    CE77PluginTypeMainMenu = 5,
    CE77PluginTypeDisassemblerContext = 6,
    CE77PluginTypeDisassemblerRenderLine = 7,
    CE77PluginTypeAutoAssembler = 8
} CE77PluginType;

typedef enum CE77AutoAssemblerPhase
{
    CE77AutoAssemblerInitialize = 0,
    CE77AutoAssemblerPhase1 = 1,
    CE77AutoAssemblerPhase2 = 2,
    CE77AutoAssemblerFinalize = 3
} CE77AutoAssemblerPhase;

typedef struct CE77PluginVersion
{
    uint32_t Version;
    char* PluginName;
} CE77PluginVersion;

typedef struct CE77PluginType0Record
{
    char* InterpretedAddress;
    CE77_UINT_PTR Address;
    CE77_BOOL IsPointer;
    int32_t CountOffsets;
    CE77_ULONG* Offsets;
    char* Description;
    char ValueType;
    char Size;
} CE77PluginType0Record;

// Host type actually passed to a type-0 callback (plugin.pas lines 726-735):
// address is ptrUint, ispointer is the four-byte BOOL. Same layout as the C header.
typedef struct CE77HostPlugin0SelectedRecord
{
    char* InterpretedAddress;
    uintptr_t Address;
    int32_t IsPointer;
    int32_t CountOffsets;
    uint32_t* Offsets;
    char* Description;
    uint8_t ValueType;
    uint8_t Size;
} CE77HostPlugin0SelectedRecord;

// Pascal kit mirror TPlugin0_SelectedRecord (cepluginsdk.pas lines 161-170):
// address is a 32-bit dword, so every later field up to countoffsets moves.
typedef struct CE77PascalDwordMirrorSelectedRecord
{
    char* InterpretedAddress;
    uint32_t Address;
    int32_t IsPointer;
    int32_t CountOffsets;
    uint32_t* Offsets;
    char* Description;
    uint8_t ValueType;
    uint8_t Size;
} CE77PascalDwordMirrorSelectedRecord;

// Pascal kit mirror TSelectedRecord (cepluginsdk.pas lines 147-156):
// ispointer is a one-byte Pascal boolean; the offsets match the host type.
typedef struct CE77PascalBooleanMirrorSelectedRecord
{
    char* InterpretedAddress;
    uintptr_t Address;
    uint8_t IsPointer;
    int32_t CountOffsets;
    uint32_t* Offsets;
    char* Description;
    uint8_t ValueType;
    uint8_t Size;
} CE77PascalBooleanMirrorSelectedRecord;

// Managed bootstrap record TPluginDotNetInitResult (plugin.pas lines 29-36, a
// packed record): 36 bytes, byte alignment. A naturally aligned copy is 40.
#pragma pack(push, 1)
typedef struct CE77ManagedPluginInitRecord
{
    char* Name;
    void* GetVersion;
    void* EnablePlugin;
    void* DisablePlugin;
    uint32_t Version;
} CE77ManagedPluginInitRecord;
#pragma pack(pop)

// Managed services table TExportedFunctionsDotNetV1 (plugin.pas lines 38-45):
// one integer followed by five pointers, natural alignment, 48 bytes.
typedef struct CE77ManagedExportedFunctions
{
    int32_t SizeOfExportedFunctions;
    void* GetLuaState;
    void* LuaRegister;
    void* LuaPushClassInstance;
    void* ProcessMessages;
    void* CheckSynchronize;
} CE77ManagedExportedFunctions;

typedef CE77_BOOL(CE77_STDCALL* CE77PluginType0Callback)(CE77PluginType0Record* selectedRecord);
typedef CE77_BOOL(CE77_STDCALL* CE77PluginType1Callback)(CE77_UINT_PTR* disassemblerAddress,
    CE77_UINT_PTR* selectedDisassemblerAddress, CE77_UINT_PTR* hexViewAddress);
typedef int32_t(CE77_STDCALL* CE77PluginType2Callback)(void* debugEvent);
typedef void(CE77_STDCALL* CE77PluginType3Callback)(CE77_ULONG processId, CE77_ULONG peProcess, CE77_BOOL created);
typedef void(CE77_STDCALL* CE77PluginType4Callback)(int32_t reserved);
typedef void(CE77_STDCALL* CE77PluginType5Callback)(void);
typedef CE77_BOOL(CE77_STDCALL* CE77PluginType6OnPopupCallback)(CE77_UINT_PTR selectedAddress,
    char** addressOfName, CE77_BOOL* show);
typedef CE77_BOOL(CE77_STDCALL* CE77PluginType6Callback)(CE77_UINT_PTR* selectedAddress);
typedef void(CE77_STDCALL* CE77PluginType7Callback)(CE77_UINT_PTR address, char** addressStringPointer,
    char** byteStringPointer, char** opcodeStringPointer, char** specialStringPointer, CE77_ULONG* textColor);
typedef void(CE77_STDCALL* CE77PluginType8Callback)(char** line, CE77AutoAssemblerPhase phase, int32_t id);

typedef struct CE77PluginType0Init
{
    char* Name;
    CE77PluginType0Callback Callback;
} CE77PluginType0Init;

typedef struct CE77PluginType1Init
{
    char* Name;
    CE77PluginType1Callback Callback;
    char* Shortcut;
} CE77PluginType1Init;

typedef struct CE77PluginType2Init
{
    CE77PluginType2Callback Callback;
} CE77PluginType2Init;

typedef struct CE77PluginType3Init
{
    CE77PluginType3Callback Callback;
} CE77PluginType3Init;

typedef struct CE77PluginType4Init
{
    CE77PluginType4Callback Callback;
} CE77PluginType4Init;

typedef struct CE77PluginType5Init
{
    char* Name;
    CE77PluginType5Callback Callback;
    char* Shortcut;
} CE77PluginType5Init;

typedef struct CE77PluginType6Init
{
    char* Name;
    CE77PluginType6Callback Callback;
    CE77PluginType6OnPopupCallback CallbackOnPopup;
    char* Shortcut;
} CE77PluginType6Init;

typedef struct CE77PluginType7Init
{
    CE77PluginType7Callback Callback;
} CE77PluginType7Init;

typedef struct CE77PluginType8Init
{
    CE77PluginType8Callback Callback;
} CE77PluginType8Init;

typedef struct CE77RegisterModificationInfo
{
    CE77_UINT_PTR Address;
    CE77_BOOL ChangeEax;
    CE77_BOOL ChangeEbx;
    CE77_BOOL ChangeEcx;
    CE77_BOOL ChangeEdx;
    CE77_BOOL ChangeEsi;
    CE77_BOOL ChangeEdi;
    CE77_BOOL ChangeEbp;
    CE77_BOOL ChangeEsp;
    CE77_BOOL ChangeEip;
    CE77_BOOL ChangeR8;
    CE77_BOOL ChangeR9;
    CE77_BOOL ChangeR10;
    CE77_BOOL ChangeR11;
    CE77_BOOL ChangeR12;
    CE77_BOOL ChangeR13;
    CE77_BOOL ChangeR14;
    CE77_BOOL ChangeR15;
    CE77_BOOL ChangeCf;
    CE77_BOOL ChangePf;
    CE77_BOOL ChangeAf;
    CE77_BOOL ChangeZf;
    CE77_BOOL ChangeSf;
    CE77_BOOL ChangeOf;
    CE77_UINT_PTR NewEax;
    CE77_UINT_PTR NewEbx;
    CE77_UINT_PTR NewEcx;
    CE77_UINT_PTR NewEdx;
    CE77_UINT_PTR NewEsi;
    CE77_UINT_PTR NewEdi;
    CE77_UINT_PTR NewEbp;
    CE77_UINT_PTR NewEsp;
    CE77_UINT_PTR NewEip;
    CE77_UINT_PTR NewR8;
    CE77_UINT_PTR NewR9;
    CE77_UINT_PTR NewR10;
    CE77_UINT_PTR NewR11;
    CE77_UINT_PTR NewR12;
    CE77_UINT_PTR NewR13;
    CE77_UINT_PTR NewR14;
    CE77_UINT_PTR NewR15;
    CE77_BOOL NewCf;
    CE77_BOOL NewPf;
    CE77_BOOL NewAf;
    CE77_BOOL NewZf;
    CE77_BOOL NewSf;
    CE77_BOOL NewOf;
} CE77RegisterModificationInfo;

typedef void(CE77_STDCALL* CE77ShowMessage)(char* message);
typedef int32_t(CE77_STDCALL* CE77RegisterFunction)(int32_t pluginId, CE77PluginType functionType, void* initialization);
typedef CE77_BOOL(CE77_STDCALL* CE77UnregisterFunction)(int32_t pluginId, int32_t functionId);
typedef CE77_HANDLE(CE77_STDCALL* CE77GetMainWindowHandle)(void);
typedef CE77_BOOL(CE77_STDCALL* CE77AutoAssemble)(char* script);
typedef CE77_BOOL(CE77_STDCALL* CE77Assembler)(CE77_UINT_PTR address, char* instruction, CE77_BYTE* output,
    int32_t maximumLength, int32_t* returnedSize);
typedef CE77_BOOL(CE77_STDCALL* CE77Disassembler)(CE77_UINT_PTR address, char* output, int32_t maximumSize);
typedef CE77_BOOL(CE77_STDCALL* CE77ChangeRegistersAtAddress)(CE77_UINT_PTR address,
    CE77RegisterModificationInfo* changes);
typedef CE77_BOOL(CE77_STDCALL* CE77InjectDll)(char* dllName, char* functionToCall);
typedef int32_t(CE77_STDCALL* CE77FreezeMemory)(CE77_UINT_PTR address, int32_t size);
typedef CE77_BOOL(CE77_STDCALL* CE77UnfreezeMemory)(int32_t freezeId);
typedef CE77_BOOL(CE77_STDCALL* CE77FixMemory)(void);
typedef CE77_BOOL(CE77_STDCALL* CE77ProcessList)(char* listBuffer, int32_t listSize);
typedef CE77_BOOL(CE77_STDCALL* CE77ReloadSettings)(void);
typedef CE77_UINT_PTR(CE77_STDCALL* CE77GetAddressFromPointer)(CE77_UINT_PTR baseAddress, int32_t offsetCount,
    int32_t* offsets);

// The physically contiguous C-header prefix only. Individual slots are not
// thereby live-qualified or callable. The upstream table continues at offset
// 144 with pointer-to-pointer hook slots; that dangerous suffix is intentionally
// outside the fixture's contract.
typedef struct CE77ExportedFunctionsPrefix
{
    int32_t SizeOfExportedFunctions;
    CE77ShowMessage ShowMessage;
    CE77RegisterFunction RegisterFunction;
    CE77UnregisterFunction UnregisterFunction;
    CE77_ULONG* OpenedProcessId;
    CE77_HANDLE* OpenedProcessHandle;
    CE77GetMainWindowHandle GetMainWindowHandle;
    CE77AutoAssemble AutoAssemble;
    CE77Assembler Assembler;
    CE77Disassembler Disassembler;
    CE77ChangeRegistersAtAddress ChangeRegistersAtAddress;
    CE77InjectDll InjectDll;
    CE77FreezeMemory FreezeMemory;
    CE77UnfreezeMemory UnfreezeMemory;
    CE77FixMemory FixMemory;
    CE77ProcessList ProcessList;
    CE77ReloadSettings ReloadSettings;
    CE77GetAddressFromPointer GetAddressFromPointer;
} CE77ExportedFunctionsPrefix;

typedef CE77_BOOL(CE77_STDCALL* CE77GetVersionExport)(CE77PluginVersion* version, int32_t versionSize);
typedef CE77_BOOL(CE77_STDCALL* CE77InitializePluginExport)(CE77ExportedFunctionsPrefix* exportedFunctions,
    int32_t pluginId);
typedef CE77_BOOL(CE77_STDCALL* CE77DisablePluginExport)(void);

#ifdef __cplusplus
#include <type_traits>

static_assert(sizeof(void*) == 8, "CE 7.7 fixture requires x64 pointers.");
static_assert(sizeof(CE77_BOOL) == 4, "Windows BOOL is four bytes.");
static_assert(sizeof(CE77_UINT_PTR) == 8, "UINT_PTR is pointer sized on x64.");
static_assert(sizeof(CE77PluginType) == 4, "C++ enum width must match the CE header.");
static_assert(sizeof(CE77AutoAssemblerPhase) == 4, "C++ enum width must match the CE header.");

static_assert(sizeof(CE77PluginVersion) == 16);
static_assert(alignof(CE77PluginVersion) == 8);
static_assert(offsetof(CE77PluginVersion, Version) == 0);
static_assert(offsetof(CE77PluginVersion, PluginName) == 8);

static_assert(sizeof(CE77PluginType0Record) == 48);
static_assert(alignof(CE77PluginType0Record) == 8);
static_assert(offsetof(CE77PluginType0Record, InterpretedAddress) == 0);
static_assert(offsetof(CE77PluginType0Record, Address) == 8);
static_assert(offsetof(CE77PluginType0Record, IsPointer) == 16);
static_assert(offsetof(CE77PluginType0Record, CountOffsets) == 20);
static_assert(offsetof(CE77PluginType0Record, Offsets) == 24);
static_assert(offsetof(CE77PluginType0Record, Description) == 32);
static_assert(offsetof(CE77PluginType0Record, ValueType) == 40);
static_assert(offsetof(CE77PluginType0Record, Size) == 41);

static_assert(sizeof(CE77HostPlugin0SelectedRecord) == 48);
static_assert(alignof(CE77HostPlugin0SelectedRecord) == 8);
static_assert(offsetof(CE77HostPlugin0SelectedRecord, Address) == 8);
static_assert(offsetof(CE77HostPlugin0SelectedRecord, IsPointer) == 16);
static_assert(offsetof(CE77HostPlugin0SelectedRecord, CountOffsets) == 20);
static_assert(offsetof(CE77HostPlugin0SelectedRecord, Offsets) == 24);
static_assert(offsetof(CE77HostPlugin0SelectedRecord, ValueType) == 40);
static_assert(sizeof(CE77PascalDwordMirrorSelectedRecord) == 48);
static_assert(offsetof(CE77PascalDwordMirrorSelectedRecord, Address) == 8);
static_assert(offsetof(CE77PascalDwordMirrorSelectedRecord, IsPointer) == 12);
static_assert(offsetof(CE77PascalDwordMirrorSelectedRecord, CountOffsets) == 16);
static_assert(offsetof(CE77PascalDwordMirrorSelectedRecord, Offsets) == 24);
static_assert(sizeof(CE77PascalBooleanMirrorSelectedRecord) == 48);
static_assert(offsetof(CE77PascalBooleanMirrorSelectedRecord, IsPointer) == 16);
static_assert(sizeof(((CE77PascalBooleanMirrorSelectedRecord*)0)->IsPointer) == 1);
static_assert(offsetof(CE77PascalBooleanMirrorSelectedRecord, CountOffsets) == 20);

static_assert(sizeof(CE77ManagedPluginInitRecord) == 36);
static_assert(alignof(CE77ManagedPluginInitRecord) == 1);
static_assert(offsetof(CE77ManagedPluginInitRecord, Name) == 0);
static_assert(offsetof(CE77ManagedPluginInitRecord, GetVersion) == 8);
static_assert(offsetof(CE77ManagedPluginInitRecord, EnablePlugin) == 16);
static_assert(offsetof(CE77ManagedPluginInitRecord, DisablePlugin) == 24);
static_assert(offsetof(CE77ManagedPluginInitRecord, Version) == 32);
static_assert(sizeof(CE77ManagedExportedFunctions) == 48);
static_assert(alignof(CE77ManagedExportedFunctions) == 8);
static_assert(offsetof(CE77ManagedExportedFunctions, SizeOfExportedFunctions) == 0);
static_assert(offsetof(CE77ManagedExportedFunctions, GetLuaState) == 8);
static_assert(offsetof(CE77ManagedExportedFunctions, LuaRegister) == 16);
static_assert(offsetof(CE77ManagedExportedFunctions, LuaPushClassInstance) == 24);
static_assert(offsetof(CE77ManagedExportedFunctions, ProcessMessages) == 32);
static_assert(offsetof(CE77ManagedExportedFunctions, CheckSynchronize) == 40);

static_assert(sizeof(CE77PluginType0Init) == 16);
static_assert(alignof(CE77PluginType0Init) == 8);
static_assert(offsetof(CE77PluginType0Init, Name) == 0);
static_assert(offsetof(CE77PluginType0Init, Callback) == 8);
static_assert(sizeof(CE77PluginType1Init) == 24);
static_assert(alignof(CE77PluginType1Init) == 8);
static_assert(offsetof(CE77PluginType1Init, Name) == 0);
static_assert(offsetof(CE77PluginType1Init, Callback) == 8);
static_assert(offsetof(CE77PluginType1Init, Shortcut) == 16);
static_assert(sizeof(CE77PluginType2Init) == 8);
static_assert(alignof(CE77PluginType2Init) == 8);
static_assert(offsetof(CE77PluginType2Init, Callback) == 0);
static_assert(sizeof(CE77PluginType3Init) == 8);
static_assert(alignof(CE77PluginType3Init) == 8);
static_assert(offsetof(CE77PluginType3Init, Callback) == 0);
static_assert(sizeof(CE77PluginType4Init) == 8);
static_assert(alignof(CE77PluginType4Init) == 8);
static_assert(offsetof(CE77PluginType4Init, Callback) == 0);
static_assert(sizeof(CE77PluginType5Init) == 24);
static_assert(alignof(CE77PluginType5Init) == 8);
static_assert(offsetof(CE77PluginType5Init, Name) == 0);
static_assert(offsetof(CE77PluginType5Init, Callback) == 8);
static_assert(offsetof(CE77PluginType5Init, Shortcut) == 16);
static_assert(sizeof(CE77PluginType6Init) == 32);
static_assert(alignof(CE77PluginType6Init) == 8);
static_assert(offsetof(CE77PluginType6Init, Name) == 0);
static_assert(offsetof(CE77PluginType6Init, Callback) == 8);
static_assert(offsetof(CE77PluginType6Init, CallbackOnPopup) == 16);
static_assert(offsetof(CE77PluginType6Init, Shortcut) == 24);
static_assert(sizeof(CE77PluginType7Init) == 8);
static_assert(alignof(CE77PluginType7Init) == 8);
static_assert(offsetof(CE77PluginType7Init, Callback) == 0);
static_assert(sizeof(CE77PluginType8Init) == 8);
static_assert(alignof(CE77PluginType8Init) == 8);
static_assert(offsetof(CE77PluginType8Init, Callback) == 0);
static_assert(sizeof(CE77RegisterModificationInfo) == 264);
static_assert(alignof(CE77RegisterModificationInfo) == 8);
static_assert(offsetof(CE77RegisterModificationInfo, NewEax) == 104);
static_assert(offsetof(CE77RegisterModificationInfo, NewR15) == 232);
static_assert(offsetof(CE77RegisterModificationInfo, NewCf) == 240);
static_assert(offsetof(CE77RegisterModificationInfo, NewOf) == 260);

static_assert(sizeof(CE77ExportedFunctionsPrefix) == 144);
static_assert(alignof(CE77ExportedFunctionsPrefix) == 8);
static_assert(offsetof(CE77ExportedFunctionsPrefix, SizeOfExportedFunctions) == 0);
static_assert(offsetof(CE77ExportedFunctionsPrefix, ShowMessage) == 8);
static_assert(offsetof(CE77ExportedFunctionsPrefix, RegisterFunction) == 16);
static_assert(offsetof(CE77ExportedFunctionsPrefix, GetAddressFromPointer) == 136);

static_assert(std::is_same_v<decltype(CE77PluginType6Init::CallbackOnPopup), CE77PluginType6OnPopupCallback>);
static_assert(std::is_same_v<CE77PluginType6OnPopupCallback,
    CE77_BOOL(CE77_STDCALL*)(CE77_UINT_PTR, char**, CE77_BOOL*)>);
#endif
