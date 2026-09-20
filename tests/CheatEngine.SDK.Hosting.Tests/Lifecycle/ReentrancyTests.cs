using System.Diagnostics.CodeAnalysis;
using CheatEngine.SDK.Abi;
using CheatEngine.SDK.Hosting.Bootstrap;
using CheatEngine.SDK.Hosting.Diagnostics;
using CheatEngine.SDK.Hosting.Tests.Support;
using CheatEngine.SDK.Hosting.Threading;
using CheatEngine.SDK.Lua.Interop.Api;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Hosting.Tests.Lifecycle;

/// <summary>
///     A lifecycle callback re-entered from inside <c>OnEnable</c> or <c>OnDisable</c> on the same thread, which is what
///     happens when plugin code pumps the host's message loop (<c>MainThread.ProcessMessages</c>) and the user toggles
///     the plugin in Cheat Engine's dialog meanwhile. The transition lock is reentrant, so without a guard the nested
///     call would run a second transition under the first one and the host would be told a state the outer call then
///     overturns. The nested call must be refused with <c>FALSE</c> and a log entry, and the outer transition must stand.
/// </summary>
public sealed unsafe class ReentrancyTests
{
    [Fact]
    [Trait("Category", "NativeLua")]
    [SuppressMessage("xUnit.Analyzers", "xUnit1051",
        Justification = "The bounded lifecycle barrier is a deterministic host-thread synchronization point.")]
    public void A_concurrent_disable_during_OnEnable_fails_immediately_and_the_outer_enable_decides_the_state()
    {
        HostingTest.RequireNativeLua();
        var sink = HostingTest.Reset();
        using NativeLuaState state = new();
        using HostSimulator host = new();
        var cancellationToken = TestContext.Current.CancellationToken;
        using ManualResetEventSlim entered = new(initialState: false);
        using ManualResetEventSlim continueEnable = new(initialState: false);
        HostingTest.UseFixture(state);
        HostingTest.Bootstrap(host);
        RecordingPlugin.OnEnableEntered = entered;
        RecordingPlugin.ContinueOnEnable = continueEnable;
        Bool32 outer = default;
        Exception? workerFailure = null;
        Thread enabling = new(() =>
        {
            try
            {
                var exports = FakeExports.Create();
                outer = host.CallEnable(&exports, 1);
            }
            catch (Exception exception)
            {
                workerFailure = exception;
            }
        });

        enabling.Start();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5), cancellationToken),
            "OnEnable did not reach its deterministic wait point.");
        Assert.Equal(PluginHostLifecyclePhase.Enabling, PluginHost.Phase);
        Assert.False(PluginHost.IsEnabled);
        var earlyDispatch = Record.Exception(() => MainThread.Invoke(static _ => { }, 0));
        var earlyDispatchFailure = Assert.IsType<InvalidOperationException>(earlyDispatch);
        Assert.Contains("no longer accepts new main-thread dispatch", earlyDispatchFailure.Message,
            StringComparison.Ordinal);

        // This call returns while the outer OnEnable is still blocked. It therefore proves that the lifecycle gate
        // does not wait behind plugin code, instead of relying on a timing threshold.
        Assert.False(host.CallDisable().IsTrue);
        Assert.Equal(PluginHostLifecyclePhase.Enabling, PluginHost.Phase);
        Assert.NotEmpty(sink.Errors("lifecycle is in Enabling"));

        continueEnable.Set();
        Assert.True(enabling.Join(TimeSpan.FromSeconds(5)), "The outer enable did not complete.");
        Assert.Null(workerFailure);
        Assert.True(outer.IsTrue);
        Assert.Equal(PluginHostLifecyclePhase.Enabled, PluginHost.Phase);
        Assert.True(PluginHost.IsEnabled);
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void Disable_nested_in_OnEnable_is_refused_and_the_enable_stands()
    {
        HostingTest.RequireNativeLua();
        var sink = HostingTest.Reset();
        using NativeLuaState state = new();
        using HostSimulator host = new();
        HostingTest.UseFixture(state);
        HostingTest.Bootstrap(host);
        RecordingPlugin.NestedCallInOnEnable = () => host.CallDisable();
        var exports = FakeExports.Create();

        var outer = host.CallEnable(&exports, 1);

        var plugin = RecordingPlugin.LastConstructed!;
        Assert.True(outer.IsTrue);
        Assert.False(plugin.NestedResultInOnEnable!.Value.IsTrue);
        Assert.Equal(0, plugin.DisableCalls);
        Assert.False(plugin.HostEnabledAfterNestedCall);
        Assert.True(plugin.RuntimeAttachedAfterNestedCall);
        Assert.True(PluginHost.IsEnabled);
        Assert.True(LuaRuntime.IsAttached);
        Assert.Equal(1u, PluginHost.Context!.PluginId);
        Assert.NotEmpty(sink.Errors("lifecycle is in Enabling"));
        Assert.False(sink.HasEntry(HostLogLevel.Information, "disabled"));
        Assert.True(sink.HasEntry(HostLogLevel.Information, "Plugin 1 enabled"));

        // The host's next real disable is the one that runs OnDisable.
        Assert.True(host.CallDisable().IsTrue);
        Assert.Equal(1, plugin.DisableCalls);
        Assert.False(PluginHost.IsEnabled);
        Assert.False(LuaRuntime.IsAttached);
        Assert.Equal(0, LuaApi.lua_gettop(state.L));
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void Enable_nested_in_OnEnable_is_refused_and_does_not_replace_the_context()
    {
        HostingTest.RequireNativeLua();
        var sink = HostingTest.Reset();
        using NativeLuaState state = new();
        using HostSimulator host = new();
        HostingTest.UseFixture(state);
        HostingTest.Bootstrap(host);
        RecordingPlugin.NestedCallInOnEnable = () => NestedEnable(host, 99);
        var exports = FakeExports.Create();

        var outer = host.CallEnable(&exports, 1);

        var plugin = RecordingPlugin.LastConstructed!;
        Assert.True(outer.IsTrue);
        Assert.False(plugin.NestedResultInOnEnable!.Value.IsTrue);
        Assert.Equal(1, plugin.EnableCalls);
        Assert.Equal(1, RecordingPlugin.ConstructorCalls);
        Assert.True(PluginHost.IsEnabled);
        Assert.Equal(1u, PluginHost.Context!.PluginId);
        Assert.Same(plugin.ContextInOnEnable, PluginHost.Context);
        Assert.NotEmpty(sink.Errors("lifecycle is in Enabling"));
        Assert.False(sink.HasEntry(HostLogLevel.Warning, "already enabled"));
        Assert.True(sink.HasEntry(HostLogLevel.Information, "Plugin 1 enabled"));
        Assert.False(sink.HasEntry(HostLogLevel.Information, "Plugin 99 enabled"));
        Assert.Equal(0, LuaApi.lua_gettop(state.L));
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void Enable_nested_in_OnDisable_is_refused_and_the_disable_stands()
    {
        HostingTest.RequireNativeLua();
        var sink = HostingTest.Reset();
        using NativeLuaState state = new();
        using HostSimulator host = new();
        var plugin = HostingTest.Enable(host, state, 1);
        RecordingPlugin.NestedCallInOnDisable = () => NestedEnable(host, 99);

        var outer = host.CallDisable();

        Assert.True(outer.IsTrue);
        Assert.False(plugin.NestedResultInOnDisable!.Value.IsTrue);
        Assert.Equal(1, plugin.EnableCalls);
        Assert.Equal(1, plugin.DisableCalls);
        Assert.False(plugin
            .HostEnabledAfterNestedCall); // Disabling keeps the context but IsEnabled is stable-state only.
        Assert.True(plugin.RuntimeAttachedAfterNestedCall);
        Assert.False(PluginHost.IsEnabled);
        Assert.Null(PluginHost.Context);
        Assert.False(LuaRuntime.IsAttached);
        Assert.NotEmpty(sink.Errors("lifecycle is in Disabling"));
        Assert.False(sink.HasEntry(HostLogLevel.Warning, "already enabled"));
        Assert.True(sink.HasEntry(HostLogLevel.Information, "Plugin 1 disabled"));

        // The host's next real enable works as usual.
        var exports = FakeExports.Create();
        Assert.True(host.CallEnable(&exports, 2).IsTrue);
        Assert.Equal(2, plugin.EnableCalls);
        Assert.Equal(2u, PluginHost.Context!.PluginId);
        Assert.Equal(0, LuaApi.lua_gettop(state.L));
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void Disable_nested_in_OnDisable_is_refused_and_OnDisable_runs_once()
    {
        HostingTest.RequireNativeLua();
        var sink = HostingTest.Reset();
        using NativeLuaState state = new();
        using HostSimulator host = new();
        var plugin = HostingTest.Enable(host, state, 1);
        RecordingPlugin.NestedCallInOnDisable = () => host.CallDisable();

        var outer = host.CallDisable();

        Assert.True(outer.IsTrue);
        Assert.False(plugin.NestedResultInOnDisable!.Value.IsTrue);
        Assert.Equal(1, plugin.DisableCalls);
        Assert.False(PluginHost.IsEnabled);
        Assert.False(LuaRuntime.IsAttached);
        Assert.NotEmpty(sink.Errors("lifecycle is in Disabling"));
        Assert.Equal(1, CountEntries(sink, HostLogLevel.Information, "Plugin 1 disabled"));
        Assert.Equal(0, LuaApi.lua_gettop(state.L));
    }

    private static Bool32 NestedEnable(HostSimulator host, uint pluginId)
    {
        var exports = FakeExports.Create();
        return host.CallEnable(&exports, pluginId);
    }

    private static int CountEntries(CapturingLogSink sink, HostLogLevel level, string fragment)
    {
        var count = 0;
        foreach (var (entryLevel, message, _) in sink.Entries)
            if (entryLevel == level && message.Contains(fragment, StringComparison.Ordinal))
                count++;

        return count;
    }
}
