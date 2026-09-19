using System.Runtime.CompilerServices;
using CheatEngine.SDK.Abi.Native;
using CheatEngine.SDK.Abi.Tests.Support;

namespace CheatEngine.SDK.Abi.Tests.Native;

public sealed unsafe class PluginVersionTests
{
    [Fact]
    public void Size_on_64_bit_is_16_bytes()
    {
        Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);

        Assert.Equal(16, Layout.SizeOf<PluginVersion>());
    }

    [Fact]
    public void Field_offsets_on_64_bit_match_the_c_structure()
    {
        Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
        PluginVersion version = default;
        void* origin = &version;

        Assert.Equal(0, Layout.OffsetOf(origin, &version.Version));
        Assert.Equal(8, Layout.OffsetOf(origin, &version.PluginName));
    }

    [Fact]
    public void Write_through_a_pointer_on_64_bit_leaves_the_padding_alone_and_places_the_name_at_8()
    {
        Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
        var raw = stackalloc byte[16];
        new Span<byte>(raw, 16).Fill(0xCC);

        var version = (PluginVersion*)raw;
        version->Version = AbiConstants.SdkVersion;
        version->PluginName = (byte*)0x0A0B_0C0D_0E0F_1011;

        Assert.Equal(6u, Unsafe.ReadUnaligned<uint>(raw));
        Assert.Equal(0xCCCC_CCCCu, Unsafe.ReadUnaligned<uint>(raw + 4));
        Assert.Equal(0x0A0B_0C0D_0E0F_1011UL, Unsafe.ReadUnaligned<ulong>(raw + 8));
    }
}
