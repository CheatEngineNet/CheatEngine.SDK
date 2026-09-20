using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Packaging;

/// <summary>
///     The direct package reference produces the complete folder a plugin author deploys: plugin, SDK assemblies,
///     manifests, and Lua bridge. The fixture also cleans and rebuilds this folder before these facts are observed.
/// </summary>
[Collection(PackagedUmbrellaSuite.Name)]
public sealed class DeploymentLayoutTests(PackagedUmbrellaFixture fixture)
{
    private static readonly string[] ExpectedSdkAssemblies =
    [
        "CheatEngine.SDK.Abi.dll", "CheatEngine.SDK.Annotations.dll", "CheatEngine.SDK.Engine.dll",
        "CheatEngine.SDK.Hosting.dll", "CheatEngine.SDK.Lua.dll", "CheatEngine.SDK.Lua.Interop.dll"
    ];

    [Fact]
    public void Direct_consumer_build_produces_an_atomic_plugin_deployment_folder()
    {
        Assert.True(File.Exists(Path.Combine(fixture.DefaultDeploymentDirectory, "DefaultConsumer.dll")));
        Assert.True(File.Exists(fixture.DefaultRuntimeConfigPath));
        Assert.True(File.Exists(fixture.DefaultDepsJsonPath));
        Assert.True(File.Exists(fixture.DefaultNativeBridgePath));

        foreach (var assemblyName in ExpectedSdkAssemblies)
            Assert.True(File.Exists(Path.Combine(fixture.DefaultDeploymentDirectory, assemblyName)),
                $"The SDK assembly '{assemblyName}' was not copied beside the plugin.");
    }

    [Fact]
    public void Direct_consumer_clean_removes_the_native_bridge_before_the_rebuild_restores_it()
    {
        Assert.True(fixture.DefaultNativeBridgeWasRemovedByClean,
            "dotnet clean left the direct-only native bridge in the consumer output instead of using normal MSBuild file tracking.");
        Assert.True(File.Exists(fixture.DefaultNativeBridgePath),
            "The clean/rebuild sequence did not restore the native bridge beside the direct consumer.");
    }
}
