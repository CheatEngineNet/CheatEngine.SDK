using CESDK.Abi.Native;
using CESDK.Abi.Tests.Support;

namespace CESDK.Abi.Tests.Native;

public sealed class PluginTypeTests
{
    [Theory]
    [InlineData(PluginType.AddressList, 0)]
    [InlineData(PluginType.MemoryView, 1)]
    [InlineData(PluginType.OnDebugEvent, 2)]
    [InlineData(PluginType.ProcessWatcherEvent, 3)]
    [InlineData(PluginType.FunctionPointerChange, 4)]
    [InlineData(PluginType.MainMenu, 5)]
    [InlineData(PluginType.DisassemblerContext, 6)]
    [InlineData(PluginType.DisassemblerRenderLine, 7)]
    [InlineData(PluginType.AutoAssembler, 8)]
    public void Member_has_the_upstream_numeric_value(PluginType member, int expected)
    {
        Assert.Equal(expected, (int)member);
    }

    [Fact]
    public void Enum_has_exactly_the_nine_upstream_members()
    {
        Assert.Equal(9, Enum.GetValues<PluginType>().Length);
    }

    [Fact]
    public void Enum_is_four_bytes_wide_like_the_c_enumeration()
    {
        Assert.Equal(typeof(int), Enum.GetUnderlyingType(typeof(PluginType)));
        Assert.Equal(4, Layout.SizeOf<PluginType>());
    }
}
