using System.Text;
using CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.Infrastructure;

namespace CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.Generator;

/// <summary>
///     Runs the generated code for real: the output compilation is emitted, loaded, and its entry point invoked against
///     the instrumented stub of <c>PluginHost</c>.
/// </summary>
public sealed class BootstrapExecutionTests(RoslynFixture roslyn) : IClassFixture<RoslynFixture>
{
    [Fact]
    public void Entry_point_called_by_name_forwards_factory_to_the_host()
    {
        var run = roslyn.Run(PluginSources.Nominal);
        using var bootstrap = LoadedBootstrap.Load(roslyn.Environment, run.OutputCompilation);

        var result = bootstrap.Initialize(IntPtr.Zero, 0);

        Assert.Equal(1, result);
        Assert.Equal(1, bootstrap.HostCallCount);
        Assert.Equal("Demo.DemoPlugin", bootstrap.LastPluginTypeName);
        Assert.Equal("Demo Plugin"u8.ToArray(), bootstrap.LastUtf8Name);
    }

    [Fact]
    public void Entry_point_called_twice_forwards_both_calls()
    {
        // Cheat Engine calls the entry point twice (name query, then load). Idempotency is the host runtime's
        // contract: the generated code must not cache or short-circuit anything.
        var run = roslyn.Run(PluginSources.Nominal);
        using var bootstrap = LoadedBootstrap.Load(roslyn.Environment, run.OutputCompilation);

        Assert.Equal(1, bootstrap.Initialize(IntPtr.Zero, 0));
        Assert.Equal(1, bootstrap.Initialize(IntPtr.Zero, 0));
        Assert.Equal(2, bootstrap.HostCallCount);
    }

    [Fact]
    public void Entry_point_host_throws_returns_zero_instead_of_propagating()
    {
        var run = roslyn.Run(PluginSources.Nominal);
        using var bootstrap = LoadedBootstrap.Load(roslyn.Environment, run.OutputCompilation);

        // A negative size makes the stub host throw.
        var result = bootstrap.Initialize(IntPtr.Zero, -1);

        Assert.Equal(0, result);
        Assert.Equal(1, bootstrap.HostCallCount);
    }

    [Fact]
    public void Entry_point_plugin_constructor_throws_returns_zero_instead_of_propagating()
    {
        var run = roslyn.Run($$"""
                               [CheatEngine.SDK.Annotations.Plugin.CheatEnginePlugin("Throws")]
                               public sealed class ThrowingPlugin : CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin
                               {
                                   public ThrowingPlugin() => throw new System.InvalidOperationException("constructor failure");

                                   {{PluginSources.LifecycleOverrides}}
                               }
                               """);
        using var bootstrap = LoadedBootstrap.Load(roslyn.Environment, run.OutputCompilation);

        Assert.Equal(0, bootstrap.Initialize(IntPtr.Zero, 0));
    }

    [Fact]
    public void Entry_point_nested_internal_plugin_is_constructed()
    {
        var run = roslyn.Run($$"""
                               namespace Demo
                               {
                                   internal static class Outer
                                   {
                                       [CheatEngine.SDK.Annotations.Plugin.CheatEnginePlugin("Nested \u00E9")]
                                       internal sealed class NestedPlugin : CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin
                                       {
                                           internal NestedPlugin()
                                           {
                                           }

                                           {{PluginSources.LifecycleOverrides}}
                                       }
                                   }
                               }
                               """);
        using var bootstrap = LoadedBootstrap.Load(roslyn.Environment, run.OutputCompilation);

        Assert.Equal(1, bootstrap.Initialize(IntPtr.Zero, 0));
        Assert.Equal("Demo.Outer+NestedPlugin", bootstrap.LastPluginTypeName);
        Assert.Equal(Encoding.UTF8.GetBytes("Nested \u00E9"), bootstrap.LastUtf8Name);
    }
}
