using CESDK.Abi.Managed;
using CESDK.Abi.Native;

namespace CESDK.Abi.Tests;

public sealed class AbiConstantsTests
{
    [Fact]
    public void SdkVersion_matches_upstream_sdk_is_6()
    {
        Assert.Equal(6, AbiConstants.SdkVersion);
    }

    [Fact]
    public void SdkVersion_assigned_to_the_unsigned_version_fields_keeps_its_value()
    {
        PluginVersion version = default;
        PluginInitRecord record = default;

        version.Version = AbiConstants.SdkVersion;
        record.Version = AbiConstants.SdkVersion;

        Assert.Equal(6u, version.Version);
        Assert.Equal(6u, record.Version);
    }
}
