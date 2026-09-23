using CheatEngine.SDK.Abi.Native;
using CheatEngine.SDK.Abi.Tests.Support;

namespace CheatEngine.SDK.Abi.Tests.Native;

/// <summary>
///     Structural regression tests for the selection record of a classic type-0 callback. The oracle is the host type
///     <c>TPlugin0_SelectedRecord</c> of the pinned <c>plugin.pas</c> (L726-735), which agrees field by field with the C
///     header <c>PLUGINTYPE0_RECORD</c> (<c>cepluginsdk.h</c> L27-37); the two divergent Pascal kit mirrors are covered
///     by <c>SelectedRecordOracleTests</c>.
/// </summary>
public sealed unsafe class PluginType0RecordTests
{
	[Fact]
	public void PluginType0Record_on_64_bit_matches_the_installed_C_header_layout()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
		PluginType0Record record = default;
		void* origin = &record;

		Assert.Equal(48, Layout.SizeOf<PluginType0Record>());
		Assert.Equal(0, Layout.OffsetOf(origin, &record.InterpretedAddress));
		Assert.Equal(8, Layout.OffsetOf(origin, &record.Address));
		Assert.Equal(16, Layout.OffsetOf(origin, &record.IsPointer));
		Assert.Equal(20, Layout.OffsetOf(origin, &record.CountOffsets));
		Assert.Equal(24, Layout.OffsetOf(origin, &record.Offsets));
		Assert.Equal(32, Layout.OffsetOf(origin, &record.Description));
		Assert.Equal(40, Layout.OffsetOf(origin, &record.ValueType));
		Assert.Equal(41, Layout.OffsetOf(origin, &record.Size));
	}

	[Fact]
	public void PluginType0Record_fields_have_the_host_widths()
	{
		Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
		PluginType0Record record = default;

		Assert.Equal(8, sizeof(nuint));
		Assert.Equal(typeof(nuint), TypeOf(nameof(PluginType0Record.Address)));
		Assert.Equal(4, Layout.SizeOf<Bool32>());
		Assert.Equal(typeof(Bool32), TypeOf(nameof(PluginType0Record.IsPointer)));
		Assert.Equal(typeof(int), TypeOf(nameof(PluginType0Record.CountOffsets)));
		Assert.Equal(8, sizeof(uint*));
		Assert.Equal(typeof(uint*), TypeOf(nameof(PluginType0Record.Offsets)));
		Assert.Equal(4, sizeof(uint));
		Assert.Equal(typeof(byte), TypeOf(nameof(PluginType0Record.ValueType)));
		Assert.Equal(typeof(byte), TypeOf(nameof(PluginType0Record.Size)));

		// Width by address arithmetic: each field ends where the next begins (or at the 42-byte data end).
		void* origin = &record;
		Assert.Equal(8, Layout.OffsetOf(origin, &record.IsPointer) - Layout.OffsetOf(origin, &record.Address));
		Assert.Equal(4, Layout.OffsetOf(origin, &record.CountOffsets) - Layout.OffsetOf(origin, &record.IsPointer));
		Assert.Equal(4, Layout.OffsetOf(origin, &record.Offsets) - Layout.OffsetOf(origin, &record.CountOffsets));
		Assert.Equal(8, Layout.OffsetOf(origin, &record.Description) - Layout.OffsetOf(origin, &record.Offsets));
		Assert.Equal(1, Layout.OffsetOf(origin, &record.Size) - Layout.OffsetOf(origin, &record.ValueType));
	}

	private static Type TypeOf(string fieldName)
	{
		return (typeof(PluginType0Record).GetField(fieldName)
				?? throw new InvalidOperationException($"PluginType0Record has no field {fieldName}.")).FieldType;
	}
}
