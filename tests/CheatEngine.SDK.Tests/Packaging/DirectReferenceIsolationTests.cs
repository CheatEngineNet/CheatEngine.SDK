using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Packaging;

/// <summary>
///     A package that merely depends on CheatEngine.SDK must not change compiler, bootstrap, or deployment behavior in
///     a project that references that package. Those behaviors belong only to a direct CheatEngine.SDK PackageReference.
/// </summary>
[Collection(PackagedUmbrellaSuite.Name)]
public sealed class DirectReferenceIsolationTests(PackagedUmbrellaFixture fixture)
{
    [Theory]
    [InlineData("AllowUnsafeBlocks", "false")]
    [InlineData("EnableDynamicLoading", "")]
    [InlineData("CheatEngineSdkGenerateEntryPoint", "")]
    public void Indirect_consumer_does_not_receive_the_package_build_property(string propertyName, string expectedValue)
    {
        Assert.Equal(expectedValue, fixture.IndirectProperties[propertyName]);
    }

    [Fact]
    public void Indirect_consumer_does_not_receive_a_generated_bootstrap()
    {
        Assert.False(fixture.IndirectEntryPointTypeExists,
            "CESDK.CESDK was generated in a project without a direct CheatEngine.SDK PackageReference.");
    }

    [Fact]
    public void Indirect_consumer_does_not_receive_the_direct_only_native_bridge()
    {
        Assert.False(File.Exists(fixture.IndirectNativeBridgePath),
            $"The direct-only bridge was copied to '{fixture.IndirectNativeBridgePath}'.");
        Assert.False(File.Exists(fixture.IndirectPublishedNativeBridgePath),
            $"The direct-only bridge was published to '{fixture.IndirectPublishedNativeBridgePath}'.");
    }
}
