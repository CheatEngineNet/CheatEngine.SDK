using CheatEngine.SDK.Abi.Native;
using CheatEngine.SDK.Abi.Tests.Support;

namespace CheatEngine.SDK.Abi.Tests.Native;

/// <summary>
///     Structural regression tests for the C-header <c>PLUGINTYPE0_RECORD</c> from the installed CE 7.7.0.10621 x64
///     SDK (SHA-256 <c>9C0E31BB753D782CE20710D19828F4E97B4371C8733ABD0C5C6F7F485306FB28</c>).
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
    public void PluginType0Record_preserves_the_header_boolean_width_and_32_bit_offset_element_width()
    {
        Assert.Equal(4, Layout.SizeOf<Bool32>());
        Assert.Equal(4, sizeof(uint));
    }
}
