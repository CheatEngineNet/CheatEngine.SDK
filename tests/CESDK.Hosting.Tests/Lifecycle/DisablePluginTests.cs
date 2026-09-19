using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CESDK.Hosting.Bootstrap;
using CESDK.Hosting.Diagnostics;
using CESDK.Hosting.Tests.Support;
using CESDK.Hosting.Threading;
using CESDK.Lua.Callbacks;
using CESDK.Lua.Interop.Api;
using CESDK.Lua.Runtime;
using CESDK.Tests.Shared.NativeLua;

namespace CESDK.Hosting.Tests.Lifecycle;

/// <summary>The disable callback and the enable-disable-enable cycle Cheat Engine's plugin dialog produces.</summary>
public sealed unsafe class DisablePluginTests
{
    [Fact]
    public void Disabling_while_disabled_is_a_no_op_reported_as_TRUE_with_a_warning()
    {
        var sink = HostingTest.Reset();
        using HostSimulator host = new();
        HostingTest.Bootstrap(host);

        var result = host.CallDisable();

        Assert.True(result.IsTrue);
        Assert.False(PluginHost.IsEnabled);
        Assert.True(sink.HasEntry(HostLogLevel.Warning, "not enabled"));
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void Disable_runs_OnDisable_while_attached_then_detaches_and_withdraws_the_context()
    {
        HostingTest.RequireNativeLua();
        var sink = HostingTest.Reset();
        using NativeLuaState state = new();
        using HostSimulator host = new();
        var plugin = HostingTest.Enable(host, state, 5);
        var context = PluginHost.Context!;

        var result = host.CallDisable();

        Assert.True(result.IsTrue);
        Assert.Equal(1, plugin.DisableCalls);
        Assert.True(plugin.RuntimeAttachedInOnDisable);
        Assert.True(plugin.HostEnabledInOnDisable);
        Assert.False(PluginHost.IsEnabled);
        Assert.Null(PluginHost.Context);
        Assert.False(LuaRuntime.IsAttached);
        Assert.False(context.IsCurrent);
        Assert.False(MainThread.IsMainThread);
        Assert.True(sink.HasEntry(HostLogLevel.Information, "Plugin 5 disabled"));
        Assert.Equal(0, LuaApi.lua_gettop(state.L));
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void Disable_neutralizes_the_callbacks_the_plugin_forgot()
    {
        HostingTest.RequireNativeLua();
        HostingTest.Reset();
        using NativeLuaState state = new();
        using HostSimulator host = new();
        HostingTest.Enable(host, state);
        var L = LuaRuntime.AcquireState();
        LuaCallback<object>? callback = null;
        try
        {
            Assert.True(LuaCallback.TryCreate(L, new LuaNativeFunction(&NoOpThunk), new object(), out callback).IsOk);
            Assert.NotNull(callback);
            Assert.True(callback.IsCurrent);

            Assert.True(host.CallDisable().IsTrue);

            Assert.True(callback.IsReleased);
        }
        finally
        {
            callback?.Dispose();
        }
    }

    // The test-infrastructure counterpart of the previous test: a test that ends with the plugin still enabled and a
    // callback still alive must not turn into a use-after-free in whichever test runs next. The simulator's disposal
    // is the teardown, and it runs before the state (declared first) is closed.
    [Fact]
    [Trait("Category", "NativeLua")]
    public void A_callback_forgotten_by_a_test_is_released_when_the_simulator_is_disposed_while_the_state_is_open()
    {
        HostingTest.RequireNativeLua();
        HostingTest.Reset();
        using NativeLuaState state = new();
        using HostSimulator host = new();
        HostingTest.Enable(host, state);
        var L = LuaRuntime.AcquireState();
        Assert.True(LuaCallback.TryCreate(L, new LuaNativeFunction(&NoOpThunk), new object(), out var forgotten).IsOk);
        Assert.NotNull(forgotten);

        host.Dispose(); // what the using statement does at the end of a test, with the state still open

        Assert.True(forgotten.IsReleased);
        Assert.Null(forgotten.StateObject);
        Assert.True(FakeExports.Create().GetLuaState() is null); // the provider hands out nothing any more
        Assert.False(PluginHost.IsEnabled);
        Assert.False(PluginHost.IsInitialized);
        Assert.False(LuaRuntime.IsAttached);
        Assert.Equal(0, LuaApi.lua_gettop(state.L)); // the state is still open and balanced
        host.Dispose(); // idempotent: the using statement disposes it again
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void OnDisable_throwing_is_logged_but_reports_TRUE_after_the_plugin_is_disabled()
    {
        HostingTest.RequireNativeLua();
        var sink = HostingTest.Reset();
        using NativeLuaState state = new();
        using HostSimulator host = new();
        var plugin = HostingTest.Enable(host, state);
        RecordingPlugin.ThrowInOnDisable = true;

        var result = host.CallDisable();

        Assert.True(result.IsTrue);
        Assert.Equal(1, plugin.DisableCalls);
        Assert.False(PluginHost.IsEnabled);
        Assert.False(LuaRuntime.IsAttached);
        (HostLogLevel, string, Exception?) entry = Assert.Single(sink.Errors("OnDisable threw"));
        Assert.IsType<InvalidOperationException>(entry.Item3);
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void Enable_disable_enable_reuses_the_instance_and_attaches_with_a_new_epoch()
    {
        HostingTest.RequireNativeLua();
        HostingTest.Reset();
        using NativeLuaState state = new();
        using HostSimulator host = new();
        var plugin = HostingTest.Enable(host, state, 1);
        var first = PluginHost.Context!;

        Assert.True(host.CallDisable().IsTrue);
        var exports = FakeExports.Create();
        Assert.True(host.CallEnable(&exports, 2).IsTrue);
        var second = PluginHost.Context!;

        Assert.Same(plugin, PluginHost.PluginForTests);
        Assert.Equal(1, RecordingPlugin.ConstructorCalls);
        Assert.Equal(2, plugin.EnableCalls);
        Assert.Equal(1, plugin.DisableCalls);
        Assert.NotSame(first, second);
        Assert.Equal(first.Epoch + 1, second.Epoch);
        Assert.Equal(LuaRuntime.Epoch, second.Epoch);
        Assert.Equal(2u, second.PluginId);
        Assert.False(first.IsCurrent);
        Assert.True(second.IsCurrent);
        Assert.True(LuaRuntime.IsAttached);
        Assert.Equal(42, plugin.LuaResultInOnEnable);
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void A_reference_cached_in_one_enable_is_stale_in_the_next()
    {
        HostingTest.RequireNativeLua();
        HostingTest.Reset();
        using NativeLuaState state = new();
        using HostSimulator host = new();
        HostingTest.Enable(host, state);
        var L = LuaRuntime.AcquireState();
        L.PushInteger(1);
        var reference = L.CreateRef();
        Assert.True(reference.IsCurrent);

        Assert.True(host.CallDisable().IsTrue);
        var exports = FakeExports.Create();
        Assert.True(host.CallEnable(&exports, 1).IsTrue);

        Assert.False(reference.IsCurrent);
        Assert.False(LuaRuntime.AcquireState().TryPushRef(reference));
        reference.Dispose();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int NoOpThunk(nint handle)
    {
        return 0;
    }
}
