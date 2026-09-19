using System.Runtime.InteropServices;
using CESDK.Abi;
using CESDK.Abi.Managed;
using CESDK.Hosting.Bootstrap;
using CESDK.Hosting.Diagnostics;
using CESDK.Hosting.Tests.Support;

namespace CESDK.Hosting.Tests.Bootstrap;

/// <summary>
///     The bootstrap as Cheat Engine drives it: a 36-byte host buffer, called twice, nothing written past it. No Lua
///     needed.
/// </summary>
public sealed unsafe class InitializeManagedTests
{
    [Fact]
    public void Writes_exactly_the_36_byte_record_and_nothing_past_it()
    {
        HostingTest.Reset();
        using HostSimulator host = new();

        var result = host.Initialize<RecordingPluginFactory>();

        Assert.Equal(ManagedEntryPoint.Success, result);
        Assert.True(host.GuardIntact);
        Assert.False(host.RecordUntouched);
        Assert.Equal(36, HostSimulator.RecordSize);
        Assert.True(PluginHost.IsInitialized);

        ref var record = ref host.Record;
        Assert.True(record.Name is not null);
        Assert.True(record.GetVersion is not null);
        Assert.True(record.EnablePlugin is not null);
        Assert.True(record.DisablePlugin is not null);
        Assert.NotEqual((nint)record.GetVersion, (nint)record.EnablePlugin);
        Assert.NotEqual((nint)record.EnablePlugin, (nint)record.DisablePlugin);
        Assert.Equal((uint)AbiConstants.SdkVersion, record.Version);
        Assert.Equal(36, PluginHost.LastInitRecordSize);
    }

    [Fact]
    public void Writes_the_record_at_an_odd_address_without_touching_the_guard()
    {
        HostingTest.Reset();
        using HostSimulator host = new(true);

        Assert.Equal(1, host.Initialize<RecordingPluginFactory>());

        Assert.True(host.GuardIntact);
        Assert.Equal((uint)AbiConstants.SdkVersion, host.Record.Version);
        Assert.Equal(RecordingPluginFactory.Name, Marshal.PtrToStringAnsi((nint)host.Record.Name));
    }

    [Fact]
    public void Second_call_is_idempotent_and_writes_the_same_bytes_including_the_name_pointer()
    {
        HostingTest.Reset();
        using HostSimulator first = new();
        using HostSimulator second = new();

        Assert.Equal(1, first.Initialize<RecordingPluginFactory>());
        Assert.Equal(1, second.Initialize<RecordingPluginFactory>());

        Assert.True(first.RecordBytes.SequenceEqual(second.RecordBytes));
        Assert.Equal((nint)first.Record.Name, (nint)second.Record.Name);
        Assert.True(second.GuardIntact);
    }

    [Fact]
    public void Name_is_the_ASCII_bytes_of_the_factory_name_NUL_terminated()
    {
        HostingTest.Reset();
        using HostSimulator host = new();

        Assert.Equal(1, host.Initialize<RecordingPluginFactory>());

        var written = AnsiNameBuffer.Read(host.Record.Name);
        Assert.True(written.SequenceEqual(RecordingPluginFactory.Utf8Name));
        Assert.Equal(0, host.Record.Name[written.Length]);
    }

    [Fact]
    public void Non_ASCII_name_is_converted_to_the_process_ANSI_code_page()
    {
        HostingTest.Reset();
        using HostSimulator host = new();

        Assert.Equal(1, host.Initialize<NonAsciiNamePluginFactory>());

        // The reference conversion is the one the official bootstrap uses; the buffer must match it byte for byte.
        // What U+00E9 becomes depends on the machine's ANSI code page (0xE9, a best-fit 'e', '?', or two UTF-8 bytes
        // under code page 65001), so nothing here asserts a particular byte or a round trip back to the string:
        // only that the ASCII prefix survived unchanged and the character produced at least one byte.
        var reference = Marshal.StringToHGlobalAnsi(NonAsciiNamePluginFactory.Name);
        try
        {
            var expected = MemoryMarshal.CreateReadOnlySpanFromNullTerminated((byte*)reference);
            var written = AnsiNameBuffer.Read(host.Record.Name);
            Assert.True(expected.SequenceEqual(written));
            Assert.True(written.StartsWith("Plugin "u8));
            Assert.True(written.Length > "Plugin "u8.Length);
            Assert.Equal(0, host.Record.Name[written.Length]);
        }
        finally
        {
            Marshal.FreeHGlobal(reference);
        }
    }

    [Fact]
    public void Null_record_address_fails_and_is_logged()
    {
        var sink = HostingTest.Reset();

        var result = PluginHost.InitializeManaged<RecordingPluginFactory>(0, 36);

        Assert.Equal(ManagedEntryPoint.Failure, result);
        Assert.False(PluginHost.IsInitialized);
        Assert.NotEmpty(sink.Errors("address is zero"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(35)]
    public void A_positive_size_smaller_than_the_record_refuses_to_write(int size)
    {
        var sink = HostingTest.Reset();
        using HostSimulator host = new();

        var result = host.Initialize<RecordingPluginFactory>(size);

        Assert.Equal(0, result);
        Assert.True(host.RecordUntouched);
        Assert.True(host.GuardIntact);
        Assert.NotEmpty(sink.Errors("smaller than the 36-byte record"));
        Assert.Equal(size, PluginHost.LastInitRecordSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(36)]
    [InlineData(40)]
    [InlineData(4096)]
    public void An_unknown_or_sufficient_size_writes_the_record(int size)
    {
        HostingTest.Reset();
        using HostSimulator host = new();

        Assert.Equal(1, host.Initialize<RecordingPluginFactory>(size));

        Assert.False(host.RecordUntouched);
        Assert.True(host.GuardIntact);
        Assert.Equal(size, PluginHost.LastInitRecordSize);
    }

    [Fact]
    public void A_second_factory_type_is_rejected_deterministically_and_the_first_keeps_working()
    {
        var sink = HostingTest.Reset();
        using HostSimulator first = new();
        using HostSimulator other = new();
        using HostSimulator again = new();

        Assert.Equal(1, first.Initialize<RecordingPluginFactory>());
        Assert.Equal(0, other.Initialize<AlternatePluginFactory>());
        Assert.Equal(0, other.Initialize<AlternatePluginFactory>());
        Assert.Equal(1, again.Initialize<RecordingPluginFactory>());

        Assert.True(other.RecordUntouched);
        Assert.True(first.RecordBytes.SequenceEqual(again.RecordBytes));
        Assert.Equal(2, sink.Errors("already registered").Count);
        Assert.Contains(nameof(AlternatePluginFactory), sink.Errors("already registered")[0].Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_factory_whose_name_getter_throws_fails_with_0_and_nothing_registered_so_a_good_factory_still_can()
    {
        var sink = HostingTest.Reset();
        using HostSimulator broken = new();
        using HostSimulator good = new();

        var result = broken.Initialize<ThrowingNamePluginFactory>();

        Assert.Equal(ManagedEntryPoint.Failure, result);
        Assert.True(broken.RecordUntouched);
        Assert.True(broken.GuardIntact);
        Assert.False(PluginHost.IsInitialized);
        Assert.Equal(36, PluginHost.LastInitRecordSize);
        (HostLogLevel, string, Exception?) entry = Assert.Single(sink.Errors("InitializeManaged failed."));
        var exception = Assert.IsType<NotSupportedException>(entry.Item3);
        Assert.Contains("requested by the test", exception.Message, StringComparison.Ordinal);

        Assert.Equal(ManagedEntryPoint.Success, good.Initialize<RecordingPluginFactory>());
        Assert.True(PluginHost.IsInitialized);
        Assert.True(AnsiNameBuffer.Read(good.Record.Name).SequenceEqual(RecordingPluginFactory.Utf8Name));
    }

    [Fact]
    public void Trace_entries_carry_the_arguments()
    {
        var sink = HostingTest.Reset();
        using HostSimulator host = new();

        host.Initialize<RecordingPluginFactory>(40);

        Assert.True(sink.HasEntry(HostLogLevel.Trace, "size 40"));
    }
}
