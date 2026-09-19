using CheatEngine.SDK.Abi.Managed;

namespace CheatEngine.SDK.Abi.Tests.Managed;

public sealed class ManagedEntryPointTests
{
    [Fact]
    public void Names_match_what_cheat_engine_looks_up()
    {
        // Cheat Engine demands CESDK.CESDK, so these literals stay independent of the CheatEngine.SDK naming.
        Assert.Equal("CESDK", ManagedEntryPoint.Namespace);
        Assert.Equal("CESDK", ManagedEntryPoint.TypeName);
        Assert.Equal("CESDK.CESDK", ManagedEntryPoint.FullTypeName);
        Assert.Equal("CEPluginInitialize", ManagedEntryPoint.MethodName);
    }

    [Fact]
    public void Result_codes_match_the_official_bootstrap()
    {
        Assert.Equal(1, ManagedEntryPoint.Success);
        Assert.Equal(0, ManagedEntryPoint.Failure);
    }
}
