namespace CheatEngine.SDK.Abi.Tests.Support;

/// <summary>
///     The x64 offset, width and kind of <b>every</b> instance field of <b>every</b> structure of
///     <c>CheatEngine.SDK.Abi</c> (public, internal and nested private alike, including the compiler-generated backing
///     fields of <see cref="Bool32" /> and <see cref="Bool8" />).
/// </summary>
/// <remarks>
///     <para>
///         Literals only, never derived from the code under test. Adding, removing, moving or retyping a field without
///         editing this table fails <c>FieldLayout.FieldLayoutContractTests</c> in both directions (a field without a row,
///         a row
///         without a field, a wrong offset, width or kind).
///     </para>
///     <para>
///         Sources of the numbers: the host Pascal types of the pinned <c>plugin.pas</c> (<c>TPluginVersion</c> L22-25,
///         <c>TPluginDotNetInitResult</c> L29-36 packed, <c>TExportedFunctionsDotNetV1</c> L38-45,
///         <c>TPlugin0_SelectedRecord</c> L726-735, <c>TExportedFunctions5</c> L47-226), the C header
///         <c>cepluginsdk.h</c> for the registration records and <c>REGISTERMODIFICATIONINFO</c>, and the x64 natural
///         alignment rule; see <c>libs/CheatEngine.SDK.Abi/README.md</c>. The <c>PluginType0Record</c> rows are identical
///         to the host
///         type table of the audit's annex 05 (the oracle of <c>Native.SelectedRecordOracleTests</c>).
///     </para>
/// </remarks>
internal static class FieldLayoutExpectations
{
	private const string Abi = "CheatEngine.SDK.Abi.";
	private const string Managed = Abi + "Managed.";
	private const string Native = Abi + "Native.";

	/// <summary>Every expected field, grouped by structure in declaration order.</summary>
	public static IReadOnlyList<FieldLayoutRow> Rows
	{
		get;
	} =
	[
		new(Abi + "Bool32", "<RawValue>k__BackingField", 0, 4, FieldKind.Integer),

		new(Abi + "Bool8", "<RawValue>k__BackingField", 0, 1, FieldKind.Integer),

		new(Managed + "PluginInitRecord", "Name", 0, 8, FieldKind.Pointer),
		new(Managed + "PluginInitRecord", "GetVersion", 8, 8, FieldKind.FunctionPointer),
		new(Managed + "PluginInitRecord", "EnablePlugin", 16, 8, FieldKind.FunctionPointer),
		new(Managed + "PluginInitRecord", "DisablePlugin", 24, 8, FieldKind.FunctionPointer),
		new(Managed + "PluginInitRecord", "Version", 32, 4, FieldKind.Integer),

		new(Managed + "ManagedExportedFunctions", "SizeOfExportedFunctions", 0, 4, FieldKind.Integer),
		new(Managed + "ManagedExportedFunctions", "GetLuaState", 8, 8, FieldKind.FunctionPointer),
		new(Managed + "ManagedExportedFunctions", "LuaRegister", 16, 8, FieldKind.OpaquePointer),
		new(Managed + "ManagedExportedFunctions", "LuaPushClassInstance", 24, 8, FieldKind.FunctionPointer),
		new(Managed + "ManagedExportedFunctions", "ProcessMessages", 32, 8, FieldKind.FunctionPointer),
		new(Managed + "ManagedExportedFunctions", "CheckSynchronize", 40, 8, FieldKind.FunctionPointer),

		new(Native + "PluginVersion", "Version", 0, 4, FieldKind.Integer),
		new(Native + "PluginVersion", "PluginName", 8, 8, FieldKind.Pointer),

		new(Native + "AddressListPluginInit", "Name", 0, 8, FieldKind.Pointer),
		new(Native + "AddressListPluginInit", "Callback", 8, 8, FieldKind.OpaquePointer),

		new(Native + "MemoryViewPluginInit", "Name", 0, 8, FieldKind.Pointer),
		new(Native + "MemoryViewPluginInit", "Callback", 8, 8, FieldKind.FunctionPointer),
		new(Native + "MemoryViewPluginInit", "Shortcut", 16, 8, FieldKind.Pointer),

		new(Native + "DebugEventPluginInit", "Callback", 0, 8, FieldKind.FunctionPointer),

		new(Native + "DebugEventObservation", "SequenceNumber", 0, 8, FieldKind.Integer),
		new(Native + "DebugEventObservation", "EventCode", 8, 4, FieldKind.Integer),
		new(Native + "DebugEventObservation", "ProcessId", 12, 4, FieldKind.Integer),
		new(Native + "DebugEventObservation", "ThreadId", 16, 4, FieldKind.Integer),

		new(Native + "ProcessWatcherPluginInit", "Callback", 0, 8, FieldKind.OpaquePointer),

		new(Native + "FunctionPointerChangePluginInit", "Callback", 0, 8, FieldKind.OpaquePointer),

		new(Native + "MainMenuPluginInit", "Name", 0, 8, FieldKind.Pointer),
		new(Native + "MainMenuPluginInit", "Callback", 8, 8, FieldKind.FunctionPointer),
		new(Native + "MainMenuPluginInit", "Shortcut", 16, 8, FieldKind.Pointer),

		new(Native + "DisassemblerContextPluginInit", "Name", 0, 8, FieldKind.Pointer),
		new(Native + "DisassemblerContextPluginInit", "Callback", 8, 8, FieldKind.OpaquePointer),
		new(Native + "DisassemblerContextPluginInit", "CallbackOnPopup", 16, 8, FieldKind.OpaquePointer),
		new(Native + "DisassemblerContextPluginInit", "Shortcut", 24, 8, FieldKind.Pointer),

		new(Native + "DisassemblerRenderLinePluginInit", "Callback", 0, 8, FieldKind.FunctionPointer),

		new(Native + "AutoAssemblerPluginInit", "Callback", 0, 8, FieldKind.FunctionPointer),

		new(Native + "ExportedFunctionsPrefix", "SizeOfExportedFunctions", 0, 4, FieldKind.Integer),
		new(Native + "ExportedFunctionsPrefix", "ShowMessage", 8, 8, FieldKind.FunctionPointer),
		new(Native + "ExportedFunctionsPrefix", "RegisterFunction", 16, 8, FieldKind.FunctionPointer),
		new(Native + "ExportedFunctionsPrefix", "UnregisterFunction", 24, 8, FieldKind.FunctionPointer),
		new(Native + "ExportedFunctionsPrefix", "OpenedProcessId", 32, 8, FieldKind.Pointer),
		new(Native + "ExportedFunctionsPrefix", "OpenedProcessHandle", 40, 8, FieldKind.Pointer),
		new(Native + "ExportedFunctionsPrefix", "GetMainWindowHandle", 48, 8, FieldKind.FunctionPointer),
		new(Native + "ExportedFunctionsPrefix", "AutoAssemble", 56, 8, FieldKind.FunctionPointer),
		new(Native + "ExportedFunctionsPrefix", "Assembler", 64, 8, FieldKind.FunctionPointer),
		new(Native + "ExportedFunctionsPrefix", "Disassembler", 72, 8, FieldKind.FunctionPointer),
		new(Native + "ExportedFunctionsPrefix", "ChangeRegistersAtAddress", 80, 8, FieldKind.FunctionPointer),
		new(Native + "ExportedFunctionsPrefix", "InjectDll", 88, 8, FieldKind.FunctionPointer),
		new(Native + "ExportedFunctionsPrefix", "FreezeMemory", 96, 8, FieldKind.FunctionPointer),
		new(Native + "ExportedFunctionsPrefix", "UnfreezeMemory", 104, 8, FieldKind.FunctionPointer),
		new(Native + "ExportedFunctionsPrefix", "FixMemory", 112, 8, FieldKind.OpaquePointer),
		new(Native + "ExportedFunctionsPrefix", "ProcessList", 120, 8, FieldKind.FunctionPointer),
		new(Native + "ExportedFunctionsPrefix", "ReloadSettings", 128, 8, FieldKind.FunctionPointer),
		new(Native + "ExportedFunctionsPrefix", "GetAddressFromPointer", 136, 8, FieldKind.OpaquePointer),

		new(Native + "PluginType0Record", "InterpretedAddress", 0, 8, FieldKind.Pointer),
		new(Native + "PluginType0Record", "Address", 8, 8, FieldKind.Integer),
		new(Native + "PluginType0Record", "IsPointer", 16, 4, FieldKind.AbiBoolean),
		new(Native + "PluginType0Record", "CountOffsets", 20, 4, FieldKind.Integer),
		new(Native + "PluginType0Record", "Offsets", 24, 8, FieldKind.Pointer),
		new(Native + "PluginType0Record", "Description", 32, 8, FieldKind.Pointer),
		new(Native + "PluginType0Record", "ValueType", 40, 1, FieldKind.Integer),
		new(Native + "PluginType0Record", "Size", 41, 1, FieldKind.Integer),

		new(Native + "RegisterModificationInfo", "Address", 0, 8, FieldKind.Integer),
		new(Native + "RegisterModificationInfo", "ChangeEax", 8, 4, FieldKind.AbiBoolean),
		new(Native + "RegisterModificationInfo", "ChangeEbx", 12, 4, FieldKind.AbiBoolean),
		new(Native + "RegisterModificationInfo", "ChangeEcx", 16, 4, FieldKind.AbiBoolean),
		new(Native + "RegisterModificationInfo", "ChangeEdx", 20, 4, FieldKind.AbiBoolean),
		new(Native + "RegisterModificationInfo", "ChangeEsi", 24, 4, FieldKind.AbiBoolean),
		new(Native + "RegisterModificationInfo", "ChangeEdi", 28, 4, FieldKind.AbiBoolean),
		new(Native + "RegisterModificationInfo", "ChangeEbp", 32, 4, FieldKind.AbiBoolean),
		new(Native + "RegisterModificationInfo", "ChangeEsp", 36, 4, FieldKind.AbiBoolean),
		new(Native + "RegisterModificationInfo", "ChangeEip", 40, 4, FieldKind.AbiBoolean),
		new(Native + "RegisterModificationInfo", "ChangeR8", 44, 4, FieldKind.AbiBoolean),
		new(Native + "RegisterModificationInfo", "ChangeR9", 48, 4, FieldKind.AbiBoolean),
		new(Native + "RegisterModificationInfo", "ChangeR10", 52, 4, FieldKind.AbiBoolean),
		new(Native + "RegisterModificationInfo", "ChangeR11", 56, 4, FieldKind.AbiBoolean),
		new(Native + "RegisterModificationInfo", "ChangeR12", 60, 4, FieldKind.AbiBoolean),
		new(Native + "RegisterModificationInfo", "ChangeR13", 64, 4, FieldKind.AbiBoolean),
		new(Native + "RegisterModificationInfo", "ChangeR14", 68, 4, FieldKind.AbiBoolean),
		new(Native + "RegisterModificationInfo", "ChangeR15", 72, 4, FieldKind.AbiBoolean),
		new(Native + "RegisterModificationInfo", "ChangeCf", 76, 4, FieldKind.AbiBoolean),
		new(Native + "RegisterModificationInfo", "ChangePf", 80, 4, FieldKind.AbiBoolean),
		new(Native + "RegisterModificationInfo", "ChangeAf", 84, 4, FieldKind.AbiBoolean),
		new(Native + "RegisterModificationInfo", "ChangeZf", 88, 4, FieldKind.AbiBoolean),
		new(Native + "RegisterModificationInfo", "ChangeSf", 92, 4, FieldKind.AbiBoolean),
		new(Native + "RegisterModificationInfo", "ChangeOf", 96, 4, FieldKind.AbiBoolean),
		new(Native + "RegisterModificationInfo", "NewEax", 104, 8, FieldKind.Integer),
		new(Native + "RegisterModificationInfo", "NewEbx", 112, 8, FieldKind.Integer),
		new(Native + "RegisterModificationInfo", "NewEcx", 120, 8, FieldKind.Integer),
		new(Native + "RegisterModificationInfo", "NewEdx", 128, 8, FieldKind.Integer),
		new(Native + "RegisterModificationInfo", "NewEsi", 136, 8, FieldKind.Integer),
		new(Native + "RegisterModificationInfo", "NewEdi", 144, 8, FieldKind.Integer),
		new(Native + "RegisterModificationInfo", "NewEbp", 152, 8, FieldKind.Integer),
		new(Native + "RegisterModificationInfo", "NewEsp", 160, 8, FieldKind.Integer),
		new(Native + "RegisterModificationInfo", "NewEip", 168, 8, FieldKind.Integer),
		new(Native + "RegisterModificationInfo", "NewR8", 176, 8, FieldKind.Integer),
		new(Native + "RegisterModificationInfo", "NewR9", 184, 8, FieldKind.Integer),
		new(Native + "RegisterModificationInfo", "NewR10", 192, 8, FieldKind.Integer),
		new(Native + "RegisterModificationInfo", "NewR11", 200, 8, FieldKind.Integer),
		new(Native + "RegisterModificationInfo", "NewR12", 208, 8, FieldKind.Integer),
		new(Native + "RegisterModificationInfo", "NewR13", 216, 8, FieldKind.Integer),
		new(Native + "RegisterModificationInfo", "NewR14", 224, 8, FieldKind.Integer),
		new(Native + "RegisterModificationInfo", "NewR15", 232, 8, FieldKind.Integer),
		new(Native + "RegisterModificationInfo", "NewCf", 240, 4, FieldKind.AbiBoolean),
		new(Native + "RegisterModificationInfo", "NewPf", 244, 4, FieldKind.AbiBoolean),
		new(Native + "RegisterModificationInfo", "NewAf", 248, 4, FieldKind.AbiBoolean),
		new(Native + "RegisterModificationInfo", "NewZf", 252, 4, FieldKind.AbiBoolean),
		new(Native + "RegisterModificationInfo", "NewSf", 256, 4, FieldKind.AbiBoolean),
		new(Native + "RegisterModificationInfo", "NewOf", 260, 4, FieldKind.AbiBoolean),

		new(Native + "ClassicSlotObservation", "Slot", 0, 4, FieldKind.Integer),
		new(Native + "ClassicSlotObservation", "RawValue", 8, 8, FieldKind.Integer),

		new(Native + "ClassicDebugEventDispatcher+DebugEventHeader", "EventCode", 0, 4, FieldKind.Integer),
		new(Native + "ClassicDebugEventDispatcher+DebugEventHeader", "ProcessId", 4, 4, FieldKind.Integer),
		new(Native + "ClassicDebugEventDispatcher+DebugEventHeader", "ThreadId", 8, 4, FieldKind.Integer)
	];
}
