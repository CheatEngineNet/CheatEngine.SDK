using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using CheatEngine.SDK.Abi.Managed;
using CheatEngine.SDK.Abi.Native;
using CheatEngine.SDK.Abi.Tests.Support;

namespace CheatEngine.SDK.Abi.Tests;

/// <summary>
///     Boundary values at the ABI edge (audit analyses/03 exit tests, A03-21 and A03-22): non-canonical booleans produced
///     by a host (2, all bits set) read as true through every lifecycle signature and record field, and addresses above
///     32 bits survive every address-carrying field and callback parameter. The host side is played by
///     <c>[UnmanagedCallersOnly]</c> stdcall functions whose raw results are chosen per test.
/// </summary>
public sealed unsafe class AbiBoundaryValueTests
{
	private const ulong HighAddress = 0xFFFF_8000_0000_0000;

	[ThreadStatic] private static int t_rawResult;

	[Theory]
	[InlineData(2)]
	[InlineData(-1)]
	public void Bool32_results_2_and_0xFFFFFFFF_read_as_true_through_every_lifecycle_signature(int raw)
	{
		t_rawResult = raw;
		PluginVersion version = default;
		ManagedExportedFunctions managedExports = default;
		ExportedFunctionsPrefix classicExports = default;

		// Managed bootstrap record: the three callbacks CE calls through TPluginDotNetInitResult.
		PluginInitRecord record = default;
		record.GetVersion = (delegate* unmanaged[Stdcall]<PluginVersion*, int, Bool32>)
			(delegate* unmanaged[Stdcall]<PluginVersion*, int, int>) &RawGetVersion;
		record.EnablePlugin = (delegate* unmanaged[Stdcall]<ManagedExportedFunctions*, uint, Bool32>)
			(delegate* unmanaged[Stdcall]<ManagedExportedFunctions*, uint, int>) &RawEnablePlugin;
		record.DisablePlugin = (delegate* unmanaged[Stdcall]<Bool32>) (delegate* unmanaged[Stdcall]<int>) &RawDisablePlugin;

		AssertTrueWithRawBits(record.GetVersion(&version, sizeof(PluginVersion)), raw);
		AssertTrueWithRawBits(record.EnablePlugin(&managedExports, 7u), raw);
		AssertTrueWithRawBits(record.DisablePlugin(), raw);

		// Classic native exports (NativeExportNames): same results, classic argument shapes.
		delegate* unmanaged[Stdcall]<PluginVersion*, int, Bool32> nativeGetVersion =
			(delegate* unmanaged[Stdcall]<PluginVersion*, int, Bool32>)
			(delegate* unmanaged[Stdcall]<PluginVersion*, int, int>) &RawGetVersion;
		delegate* unmanaged[Stdcall]<ExportedFunctionsPrefix*, int, Bool32> nativeInitialize =
			(delegate* unmanaged[Stdcall]<ExportedFunctionsPrefix*, int, Bool32>)
			(delegate* unmanaged[Stdcall]<ExportedFunctionsPrefix*, int, int>) &RawInitializePlugin;
		delegate* unmanaged[Stdcall]<Bool32> nativeDisable =
			(delegate* unmanaged[Stdcall]<Bool32>) (delegate* unmanaged[Stdcall]<int>) &RawDisablePlugin;

		AssertTrueWithRawBits(nativeGetVersion(&version, sizeof(PluginVersion)), raw);
		AssertTrueWithRawBits(nativeInitialize(&classicExports, 0x1020_3040), raw);
		AssertTrueWithRawBits(nativeDisable(), raw);
	}

	[Theory]
	[InlineData(2)]
	[InlineData(-1)]
	public void PluginType0Record_IsPointer_2_and_0xFFFFFFFF_read_as_true(int raw)
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
		byte* buffer = stackalloc byte[48];
		new Span<byte>(buffer, 48).Clear();
		Unsafe.WriteUnaligned(buffer + 16, raw);

		PluginType0Record* record = (PluginType0Record*) buffer;

		AssertTrueWithRawBits(record->IsPointer, raw);
		Assert.Equal(0, record->CountOffsets);
	}

	[Fact]
	public void Address_0xFFFF800000000000_round_trips_through_every_address_field()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);

		ulong high = HighAddress;
		nuint address = (nuint) high;

		// Selection record: the host writes the address, the SDK reads it through the record.
		byte* selection = stackalloc byte[48];
		new Span<byte>(selection, 48).Clear();
		Unsafe.WriteUnaligned(selection + 8, high);
		Assert.Equal(address, ((PluginType0Record*) selection)->Address);
		Assert.Equal(0, ((PluginType0Record*) selection)->IsPointer.RawValue);

		// Register change request: the address and a replacement register value land at their byte offsets.
		RegisterModificationInfo info = default;
		info.Address = address;
		info.NewR15 = address + 8;
		Assert.Equal(high, Unsafe.ReadUnaligned<ulong>((byte*) &info));
		Assert.Equal(high + 8, Unsafe.ReadUnaligned<ulong>((byte*) &info + 232));

		// Memory view callback: three addresses by reference, in and out.
		MemoryViewPluginInit memoryView = default;
		memoryView.Callback = &SwapMemoryViewAddresses;
		nuint disassembler = address;
		nuint selected = address + 1;
		nuint hexView = address + 2;
		Assert.True(memoryView.Callback(&disassembler, &selected, &hexView).IsTrue);
		Assert.Equal(address + 2, disassembler);
		Assert.Equal(address, hexView);
		Assert.Equal(address + 1, selected);

		// Classic ChangeRegistersAtAddress: the address argument and the NewR15 written back by the host.
		ExportedFunctionsPrefix exports = default;
		exports.ChangeRegistersAtAddress = &FakeChangeRegistersAtAddress;
		RegisterModificationInfo request = default;
		request.ChangeR15 = Bool32.True;
		Assert.True(exports.ChangeRegistersAtAddress(address, &request).IsTrue);
		Assert.Equal(address + 0x10, request.NewR15);
		Assert.Equal(address, request.Address);
	}

	private static void AssertTrueWithRawBits(Bool32 value, int raw)
	{
		Assert.True(value.IsTrue);
		Assert.True(value == Bool32.True, "Equality is on truthiness, so a non-canonical true equals Bool32.True.");
		Assert.Equal(raw, value.RawValue);
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static int RawGetVersion(PluginVersion* version, int size)
	{
		_ = version;
		_ = size;
		return t_rawResult;
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static int RawEnablePlugin(ManagedExportedFunctions* exports, uint pluginId)
	{
		_ = exports;
		_ = pluginId;
		return t_rawResult;
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static int RawInitializePlugin(ExportedFunctionsPrefix* exports, int pluginId)
	{
		_ = exports;
		_ = pluginId;
		return t_rawResult;
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static int RawDisablePlugin()
	{
		return t_rawResult;
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static Bool32 SwapMemoryViewAddresses(nuint* disassemblerAddress, nuint* selectedAddress, nuint* hexViewAddress)
	{
		_ = selectedAddress;
		(*disassemblerAddress, *hexViewAddress) = (*hexViewAddress, *disassemblerAddress);
		return Bool32.True;
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static Bool32 FakeChangeRegistersAtAddress(nuint address, RegisterModificationInfo* changes)
	{
		changes->Address = address;
		changes->NewR15 = address + 0x10;
		return Bool32.True;
	}
}
