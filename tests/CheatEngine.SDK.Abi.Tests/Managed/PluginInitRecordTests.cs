using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CheatEngine.SDK.Abi.Managed;
using CheatEngine.SDK.Abi.Native;
using CheatEngine.SDK.Abi.Tests.Support;

namespace CheatEngine.SDK.Abi.Tests.Managed;

public sealed unsafe class PluginInitRecordTests
{
    private const byte Guard = 0xCC;

    private static readonly byte* FakeName = (byte*)0x1111_2222_3333_4444;

    private static int s_disableCalls;

    [Fact]
    public void Size_on_64_bit_is_36_bytes_packed()
    {
        Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);

        Assert.Equal(36, Layout.SizeOf<PluginInitRecord>());
    }

    [Fact]
    public void Field_offsets_on_64_bit_match_the_host_record()
    {
        Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
        PluginInitRecord record = default;
        void* origin = &record;

        Assert.Equal(0, Layout.OffsetOf(origin, &record.Name));
        Assert.Equal(8, Layout.OffsetOf(origin, &record.GetVersion));
        Assert.Equal(16, Layout.OffsetOf(origin, &record.EnablePlugin));
        Assert.Equal(24, Layout.OffsetOf(origin, &record.DisablePlugin));
        Assert.Equal(32, Layout.OffsetOf(origin, &record.Version));
    }

    /// <summary>
    ///     The defect this guards against: an unpacked mirror is 40 bytes and writes 4 bytes past the host's 36-byte
    ///     variable.
    ///     An odd start offset additionally proves that access through the packed type is unaligned-safe.
    /// </summary>
    [Theory]
    [InlineData(16)]
    [InlineData(13)]
    public void Write_through_a_pointer_on_64_bit_touches_exactly_36_bytes(int start)
    {
        Assert.SkipUnless(Layout.Is64BitProcess, Layout.Requires64BitProcess);
        const int RecordSize = 36;
        const int Total = 80;
        var buffer = stackalloc byte[Total];
        Span<byte> bytes = new(buffer, Total);
        bytes.Fill(Guard);

        var record = (PluginInitRecord*)(buffer + start);
        *record = default;
        record->Name = FakeName;
        record->GetVersion = (delegate* unmanaged[Stdcall]<PluginVersion*, int, Bool32>)0x0101_0101_0101_0101;
        record->EnablePlugin =
            (delegate* unmanaged[Stdcall]<ManagedExportedFunctions*, uint, Bool32>)0x0202_0202_0202_0202;
        record->DisablePlugin = (delegate* unmanaged[Stdcall]<Bool32>)0x0303_0303_0303_0303;
        record->Version = AbiConstants.SdkVersion;

        Assert.All(bytes[..start].ToArray(), static b => Assert.Equal(Guard, b));
        Assert.All(bytes[(start + RecordSize)..].ToArray(), static b => Assert.Equal(Guard, b));
        Assert.Equal(0x1111_2222_3333_4444UL, Unsafe.ReadUnaligned<ulong>(buffer + start));
        Assert.Equal(0x0101_0101_0101_0101UL, Unsafe.ReadUnaligned<ulong>(buffer + start + 8));
        Assert.Equal(0x0202_0202_0202_0202UL, Unsafe.ReadUnaligned<ulong>(buffer + start + 16));
        Assert.Equal(0x0303_0303_0303_0303UL, Unsafe.ReadUnaligned<ulong>(buffer + start + 24));
        Assert.Equal(6u, Unsafe.ReadUnaligned<uint>(buffer + start + 32));
    }

    /// <summary>
    ///     Plays the host: the callbacks are real <c>[UnmanagedCallersOnly]</c> stdcall functions whose addresses only
    ///     fit the fields if the declared signatures are exactly right, and they are invoked through the record.
    /// </summary>
    [Fact]
    public void Callbacks_stored_in_the_record_are_callable_with_the_declared_shapes()
    {
        PluginInitRecord record = default;
        record.GetVersion = &FakeGetVersion;
        record.EnablePlugin = &FakeEnablePlugin;
        record.DisablePlugin = &FakeDisablePlugin;

        PluginVersion version = default;
        var versionResult = record.GetVersion(&version, Layout.SizeOf<PluginVersion>());
        Assert.True(versionResult.IsTrue);
        Assert.Equal(6u, version.Version);
        Assert.Equal((nint)FakeName, (nint)version.PluginName);

        ManagedExportedFunctions exports = default;
        exports.SizeOfExportedFunctions = Layout.SizeOf<ManagedExportedFunctions>();
        Assert.True(record.EnablePlugin(&exports, 0xFFFF_FFF0u).IsTrue);
        Assert.False(record.EnablePlugin(&exports, 7u).IsTrue);

        var before = s_disableCalls;
        Assert.True(record.DisablePlugin().IsTrue);
        Assert.Equal(before + 1, s_disableCalls);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static Bool32 FakeGetVersion(PluginVersion* version, int size)
    {
        if (version is null || size < sizeof(PluginVersion)) return Bool32.False;

        version->Version = AbiConstants.SdkVersion;
        version->PluginName = FakeName;
        return Bool32.True;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static Bool32 FakeEnablePlugin(ManagedExportedFunctions* exports, uint pluginId)
    {
        return exports is not null && exports->SizeOfExportedFunctions == sizeof(ManagedExportedFunctions) &&
               pluginId == 0xFFFF_FFF0u;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static Bool32 FakeDisablePlugin()
    {
        s_disableCalls++;
        return Bool32.True;
    }
}
