using System.Reflection;

using CheatEngine.SDK.Abi.Native;
using CheatEngine.SDK.Abi.Tests.Support;

namespace CheatEngine.SDK.Abi.Tests.Fixture;

/// <summary>
///     Managed counterparts of the small, independently compiled native fixture contract. The fixture itself is
///     opt-in because it needs MSVC; these tests keep normal CI independent of that toolchain and of any installed
///     Cheat Engine binary.
/// </summary>
public sealed class NativeAbiFixtureContractTests
{
	[Fact]
	[Trait("Qualification", "Q01")]
	public void Header_derived_classic_records_have_the_fixture_x64_sizes()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);

		Assert.Equal(16, Layout.SizeOf<PluginVersion>());
		Assert.Equal(16, Layout.SizeOf<AddressListPluginInit>());
		Assert.Equal(24, Layout.SizeOf<MemoryViewPluginInit>());
		Assert.Equal(8, Layout.SizeOf<DebugEventPluginInit>());
		Assert.Equal(8, Layout.SizeOf<ProcessWatcherPluginInit>());
		Assert.Equal(8, Layout.SizeOf<FunctionPointerChangePluginInit>());
		Assert.Equal(24, Layout.SizeOf<MainMenuPluginInit>());
		Assert.Equal(32, Layout.SizeOf<DisassemblerContextPluginInit>());
		Assert.Equal(8, Layout.SizeOf<DisassemblerRenderLinePluginInit>());
		Assert.Equal(8, Layout.SizeOf<AutoAssemblerPluginInit>());
		Assert.Equal(48, Layout.SizeOf<PluginType0Record>());
		Assert.Equal(264, Layout.SizeOf<RegisterModificationInfo>());
		Assert.Equal(144, Layout.SizeOf<ExportedFunctionsPrefix>());
	}

	[Fact]
	[Trait("Qualification", "Q01")]
	public void Header_derived_classic_records_have_the_fixture_x64_alignments()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);

		Assert.Equal(8, Layout.AlignmentOf<PluginVersion>());
		Assert.Equal(8, Layout.AlignmentOf<AddressListPluginInit>());
		Assert.Equal(8, Layout.AlignmentOf<MemoryViewPluginInit>());
		Assert.Equal(8, Layout.AlignmentOf<DebugEventPluginInit>());
		Assert.Equal(8, Layout.AlignmentOf<ProcessWatcherPluginInit>());
		Assert.Equal(8, Layout.AlignmentOf<FunctionPointerChangePluginInit>());
		Assert.Equal(8, Layout.AlignmentOf<MainMenuPluginInit>());
		Assert.Equal(8, Layout.AlignmentOf<DisassemblerContextPluginInit>());
		Assert.Equal(8, Layout.AlignmentOf<DisassemblerRenderLinePluginInit>());
		Assert.Equal(8, Layout.AlignmentOf<AutoAssemblerPluginInit>());
		Assert.Equal(8, Layout.AlignmentOf<PluginType0Record>());
		Assert.Equal(8, Layout.AlignmentOf<RegisterModificationInfo>());
		Assert.Equal(8, Layout.AlignmentOf<ExportedFunctionsPrefix>());
	}

	[Fact]
	public void Header_and_pinned_Pascal_popup_contract_conflict_keeps_the_slot_opaque_until_a_live_canary()
	{
		FieldInfo popup = typeof(DisassemblerContextPluginInit).GetField(
			                  nameof(DisassemblerContextPluginInit.CallbackOnPopup))
		                  ?? throw new InvalidOperationException("The popup callback field was not found.");

		Type fieldType = popup.GetModifiedFieldType().UnderlyingSystemType;

		Assert.True(fieldType.IsPointer);
		Assert.Equal("System.Void", fieldType.GetElementType()?.UnderlyingSystemType.FullName);
	}

	[Fact]
	public void Fixture_dll_export_names_are_the_three_classic_header_exports_in_order()
	{
		string[] expected =
		[
			"CEPlugin_GetVersion",
			"CEPlugin_InitializePlugin",
			"CEPlugin_DisablePlugin"
		];

		string[] actual =
		[
			NativeExportNames.GetVersion,
			NativeExportNames.InitializePlugin,
			NativeExportNames.DisablePlugin
		];

		Assert.Equal(expected, actual);
	}
}
