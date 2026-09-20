using CheatEngine.SDK.Abi;
using CheatEngine.SDK.Abi.Native;
using CheatEngine.SDK.Hosting.Bootstrap;
using CheatEngine.SDK.Hosting.Tests.Support;

namespace CheatEngine.SDK.Hosting.Tests.Bootstrap;

/// <summary>The version query, called through the record's function pointer as the host calls it. No Lua needed.</summary>
public sealed unsafe class GetVersionTests
{
    [Fact]
    public void Fills_version_6_and_the_bootstrap_name_pointer()
    {
        HostingTest.Reset();
        using HostSimulator host = new();
        HostingTest.Bootstrap(host);
        PluginVersion version = default;

        var result = host.CallGetVersion(&version, sizeof(PluginVersion));

        Assert.True(result.IsTrue);
        Assert.Equal((uint)AbiConstants.SdkVersion, version.Version);
        Assert.Equal((nint)host.Record.Name, (nint)version.PluginName);
        Assert.Equal(sizeof(PluginVersion), PluginHost.LastVersionRecordSize);
    }

    [Fact]
    public void A_larger_host_record_is_accepted_and_only_the_known_fields_are_written()
    {
        HostingTest.Reset();
        using HostSimulator host = new();
        HostingTest.Bootstrap(host);
        var buffer = stackalloc byte[64];
        new Span<byte>(buffer, 64).Fill(0xEE);

        var result = host.CallGetVersion((PluginVersion*)buffer, 64);

        Assert.True(result.IsTrue);
        Assert.Equal((uint)AbiConstants.SdkVersion, ((PluginVersion*)buffer)->Version);
        Assert.Equal(0xEE, buffer[sizeof(PluginVersion)]);
        Assert.Equal(0xEE, buffer[63]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(15)]
    public void A_positive_size_smaller_than_16_bytes_is_refused_and_left_untouched(int size)
    {
        var sink = HostingTest.Reset();
        using HostSimulator host = new();
        HostingTest.Bootstrap(host);
        var buffer = stackalloc byte[16];
        new Span<byte>(buffer, 16).Fill(0xEE);

        var result = host.CallGetVersion((PluginVersion*)buffer, size);

        Assert.False(result.IsTrue);
        Assert.True(new ReadOnlySpan<byte>(buffer, 16).IndexOfAnyExcept((byte)0xEE) < 0);
        Assert.NotEmpty(sink.Errors("reserved"));
        Assert.Equal(size, PluginHost.LastVersionRecordSize);
    }

    // This version-query record has its own host size contract: a host that claims nothing (zero or negative) is not
    // refused, and a host that claims enough is written. It is unrelated to InitializeManaged's opaque second integer.
    [Theory]
    [InlineData(0)]
    [InlineData(-16)]
    [InlineData(16)]
    [InlineData(64)]
    public void An_unknown_or_sufficient_size_writes_the_record(int size)
    {
        HostingTest.Reset();
        using HostSimulator host = new();
        HostingTest.Bootstrap(host);
        PluginVersion version = default;

        var result = host.CallGetVersion(&version, size);

        Assert.True(result.IsTrue);
        Assert.Equal((uint)AbiConstants.SdkVersion, version.Version);
        Assert.Equal((nint)host.Record.Name, (nint)version.PluginName);
        Assert.Equal(size, PluginHost.LastVersionRecordSize);
    }

    [Fact]
    public void A_null_record_is_refused()
    {
        var sink = HostingTest.Reset();
        using HostSimulator host = new();
        HostingTest.Bootstrap(host);

        Assert.False(host.CallGetVersion(null, sizeof(PluginVersion)).IsTrue);
        Assert.NotEmpty(sink.Errors("address is zero"));
    }

    [Fact]
    public void Without_a_bootstrap_there_is_no_name_to_report()
    {
        var sink = HostingTest.Reset();
        using HostSimulator host = new();
        HostingTest.Bootstrap(host);
        var getVersion = host.Record.GetVersion;
        PluginHost.ResetForTests();
        PluginVersion version = default;

        var result = getVersion(&version, sizeof(PluginVersion));

        Assert.False(result.IsTrue);
        Assert.True(version.PluginName is null);
        Assert.NotEmpty(sink.Errors("bootstrap has not run"));
    }
}
