// SPDX-License-Identifier: MIT
//
// Minimal, header-derived classic-plugin ABI contract for an x64 fixture.
//
// Provenance (do not replace this with a locally installed header):
//   Cheat Engine upstream commit ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37
//   Cheat Engine/plugin/cepluginsdk.h, lines 15-160, 163-180 and 271-456
//   https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/plugin/cepluginsdk.h
//
// This is intentionally not a vendored copy of cepluginsdk.h.  It carries only
// the declarations that the fixture measures.  The fixture is an x64 MSVC
// build; Windows SDK headers supply no CE declarations and no CE installation
// is consulted.

#pragma once

#include <stddef.h>
#include <stdint.h>

#if !defined(_WIN64)
#error The CE 7.7 fixture is deliberately Windows x64 only.
#endif

#define CE77_ABI_FIXTURE_SCHEMA_VERSION 2
#define CE77_UPSTREAM_COMMIT "ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37"
#define CE77_CEPLUGINSDK_PATH "Cheat Engine/plugin/cepluginsdk.h"

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
