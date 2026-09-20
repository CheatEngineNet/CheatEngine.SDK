using System.Diagnostics.CodeAnalysis;
using CheatEngine.SDK.Abi.Managed;
using CheatEngine.SDK.Hosting.Bootstrap;
using CheatEngine.SDK.Hosting.Context;
using CheatEngine.SDK.Hosting.Diagnostics;
using CheatEngine.SDK.Hosting.Tests.Support;
using CheatEngine.SDK.Hosting.Threading;
using CheatEngine.SDK.Lua.Interop.Api;
using CheatEngine.SDK.Lua.Interop.Loading;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Hosting.Tests.Lifecycle;

/// <summary>
///     The enable callback, called through the record's pointer with a fake exports record on the caller's stack. The
///     failures that happen before the Lua API is touched are DLL-free; everything from the binding on needs the fixture.
/// </summary>
public sealed unsafe class EnablePluginTests
{
    [Fact]
    public void A_null_exports_record_fails_without_touching_Lua()
    {
        var sink = HostingTest.Reset();
        using HostSimulator host = new();
        HostingTest.Bootstrap(host);

        var result = host.CallEnable(null, 1);

        Assert.False(result.IsTrue);
        Assert.False(PluginHost.IsEnabled);
        Assert.False(LuaRuntime.IsAttached);
        Assert.NotEmpty(sink.Errors("address is zero"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(24)]
    [InlineData(40)]
    [InlineData(47)]
    [InlineData(-48)]
    public void An_undersized_exports_record_fails_cleanly(int reportedSize)
    {
        var sink = HostingTest.Reset();
        using HostSimulator host = new();
        HostingTest.Bootstrap(host);
        var exports = FakeExports.Create();
        exports.SizeOfExportedFunctions = reportedSize;

        var result = host.CallEnable(&exports, 1);

        Assert.False(result.IsTrue);
        Assert.False(PluginHost.IsEnabled);
        Assert.False(LuaRuntime.IsAttached);
        Assert.Equal(0, RecordingPlugin.ConstructorCalls);
        Assert.NotEmpty(sink.Errors(reportedSize + "-byte exports record"));
    }

    [Fact]
    public void A_record_without_GetLuaState_fails()
    {
        var sink = HostingTest.Reset();
        using HostSimulator host = new();
        HostingTest.Bootstrap(host);
        var exports = FakeExports.Create();
        exports.GetLuaState = null;

        Assert.False(host.CallEnable(&exports, 1).IsTrue);
        Assert.NotEmpty(sink.Errors("no GetLuaState"));
    }

    [Fact]
    public void Enable_before_the_bootstrap_fails()
    {
        var sink = HostingTest.Reset();
        using HostSimulator host = new();
        HostingTest.Bootstrap(host);
        var enable = host.Record.EnablePlugin;
        PluginHost.ResetForTests();
        var exports = FakeExports.Create();

        Assert.False(enable(&exports, 1).IsTrue);
        Assert.NotEmpty(sink.Errors("bootstrap has not run"));
    }

    [Fact]
    public void Without_a_Lua_module_in_the_process_the_enable_fails_before_any_plugin_code()
    {
        var sink = HostingTest.Reset();
        HostingTest.UseNoModule();
        using HostSimulator host = new();
        HostingTest.Bootstrap(host);
        var exports = FakeExports.Create();

        var result = host.CallEnable(&exports, 1);

        Assert.False(result.IsTrue);
        Assert.False(PluginHost.IsEnabled);
        Assert.Equal(0, RecordingPlugin.ConstructorCalls);
        Assert.Equal(0, FakeExports.GetLuaStateCalls);
        Assert.NotEmpty(sink.Errors("resolver returned no Lua module"));
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    [SuppressMessage("Meziantou.Analyzer", "MA0051", Justification = "This test verifies every lifecycle invariant after a successful enable and intentionally keeps the assertions together.")]
    public void Enables_the_plugin_binds_Lua_attaches_the_runtime_and_runs_OnEnable_on_this_thread()
    {
        HostingTest.RequireNativeLua();
        var sink = HostingTest.Reset();
        using NativeLuaState state = new();
        using HostSimulator host = new();
        HostingTest.UseFixture(state);
        HostingTest.Bootstrap(host);
        var exports = FakeExports.Create();

        var result = host.CallEnable(&exports, 42);

        Assert.True(result.IsTrue);
        Assert.True(PluginHost.IsEnabled);
        Assert.True(LuaApi.IsInitialized);
        Assert.True(LuaRuntime.IsAttached);
        Assert.Equal(FakeExports.GetLuaStateAddress, LuaRuntime.CurrentBinding.StateProvider);
        Assert.Equal(FakeExports.PusherAddress, LuaRuntime.CurrentBinding.HostObjectPusher);
        Assert.Equal(Environment.CurrentManagedThreadId, LuaRuntime.CurrentBinding.MainThreadId);

        var plugin = Assert.IsType<RecordingPlugin>(PluginHost.PluginForTests);
        Assert.Equal(1, RecordingPlugin.ConstructorCalls);
        Assert.False(RecordingPlugin.RuntimeAttachedInConstructor);
        Assert.False(RecordingPlugin.HostEnabledInConstructor);
        Assert.Equal(1, plugin.EnableCalls);
        Assert.Equal(Environment.CurrentManagedThreadId, plugin.EnableThreadId);
        Assert.True(plugin.RuntimeAttachedInOnEnable);
        Assert.False(plugin.HostEnabledInOnEnable);
        Assert.True(plugin.MainThreadInOnEnable);
        Assert.Equal(42, plugin.LuaResultInOnEnable);

        var context = Assert.IsType<PluginContext>(PluginHost.Context);
        Assert.Same(context, plugin.ContextInOnEnable);
        Assert.Equal(42u, context.PluginId);
        Assert.Equal(LuaRuntime.Epoch, context.Epoch);
        Assert.Equal(Environment.CurrentManagedThreadId, context.MainThreadId);
        Assert.True(context.IsCurrent);
        Assert.True(context.IsMainThread);
        Assert.Equal(48, context.ReportedExportsSize);
        Assert.True(context.HasProcessMessages);
        Assert.True(context.HasCheckSynchronize);
        Assert.Equal(LuaRuntime.CurrentBinding, context.HostBinding);
        Assert.False(context.ShutdownToken.IsCancellationRequested);
        Assert.Equal(PluginHostLifecyclePhase.Enabled, PluginHost.Phase);

        Assert.Equal(0, FakeExports.LuaRegisterCalls);
        Assert.True(sink.HasEntry(HostLogLevel.Information, "Plugin 42 enabled"));
        Assert.Equal(0, LuaApi.lua_gettop(state.L));
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void The_exports_record_is_copied_during_the_call_not_referenced()
    {
        HostingTest.RequireNativeLua();
        HostingTest.Reset();
        using NativeLuaState state = new();
        using HostSimulator host = new();
        HostingTest.UseFixture(state);
        HostingTest.Bootstrap(host);
        var exports = FakeExports.Create();

        Assert.True(host.CallEnable(&exports, 1).IsTrue);
        exports = default; // the host's stack local dies after the call

        MainThread.ProcessMessages();
        Assert.Equal(1, FakeExports.ProcessMessagesCalls);
        Assert.Equal(FakeExports.GetLuaStateAddress, LuaRuntime.CurrentBinding.StateProvider);
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void A_longer_exports_record_is_accepted_and_its_tail_ignored()
    {
        HostingTest.RequireNativeLua();
        HostingTest.Reset();
        using NativeLuaState state = new();
        using HostSimulator host = new();
        HostingTest.UseFixture(state);
        HostingTest.Bootstrap(host);
        var buffer = stackalloc byte[64];
        new Span<byte>(buffer, 64).Fill(0xFF);
        *(ManagedExportedFunctions*)buffer = FakeExports.Create(64);

        Assert.True(host.CallEnable((ManagedExportedFunctions*)buffer, 3).IsTrue);

        Assert.Equal(64, PluginHost.Context!.ReportedExportsSize);
        Assert.Equal(0xFF, buffer[48]);
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void A_state_provider_that_returns_null_fails_the_self_check()
    {
        HostingTest.RequireNativeLua();
        var sink = HostingTest.Reset();
        using NativeLuaState state = new();
        using HostSimulator host = new();
        HostingTest.UseFixture(state);
        FakeExports.UseState(null);
        HostingTest.Bootstrap(host);
        var exports = FakeExports.Create();

        Assert.False(host.CallEnable(&exports, 1).IsTrue);

        Assert.False(PluginHost.IsEnabled);
        Assert.Equal(PluginHostLifecyclePhase.Registered, PluginHost.Phase);
        Assert.False(LuaRuntime.IsAttached);
        Assert.Equal(0, RecordingPlugin.ConstructorCalls);
        Assert.NotEmpty(sink.Errors("returned no state"));
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void OnEnable_throwing_makes_the_enable_fail_and_detaches_the_runtime()
    {
        HostingTest.RequireNativeLua();
        var sink = HostingTest.Reset();
        using NativeLuaState state = new();
        using HostSimulator host = new();
        HostingTest.UseFixture(state);
        HostingTest.Bootstrap(host);
        RecordingPlugin.ThrowInOnEnable = true;
        var exports = FakeExports.Create();

        var result = host.CallEnable(&exports, 1);

        Assert.False(result.IsTrue);
        Assert.False(PluginHost.IsEnabled);
        Assert.Equal(PluginHostLifecyclePhase.Registered, PluginHost.Phase);
        Assert.Null(PluginHost.Context);
        Assert.False(LuaRuntime.IsAttached);
        Assert.Equal(1, RecordingPlugin.LastConstructed!.EnableCalls);
        Assert.True(RecordingPlugin.LastConstructed.ContextInOnEnable!.ShutdownToken.IsCancellationRequested);
        (HostLogLevel, string, Exception?) entry = Assert.Single(sink.Errors("OnEnable threw"));
        var exception = Assert.IsType<InvalidOperationException>(entry.Item3);
        Assert.Contains("requested by the test", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void A_throwing_constructor_fails_the_enable_and_is_retried_on_the_next_enable()
    {
        HostingTest.RequireNativeLua();
        var sink = HostingTest.Reset();
        using NativeLuaState state = new();
        using HostSimulator host = new();
        HostingTest.UseFixture(state);
        HostingTest.Bootstrap(host);
        RecordingPlugin.ThrowInConstructor = true;
        var exports = FakeExports.Create();

        Assert.False(host.CallEnable(&exports, 1).IsTrue);

        Assert.False(PluginHost.IsEnabled);
        Assert.False(LuaRuntime.IsAttached);
        Assert.Null(PluginHost.PluginForTests);
        Assert.Equal(1, RecordingPlugin.ConstructorCalls);
        (HostLogLevel, string, Exception?) entry = Assert.Single(sink.Errors("constructor threw"));
        Assert.IsType<InvalidOperationException>(entry.Item3);

        RecordingPlugin.ThrowInConstructor = false;
        Assert.True(host.CallEnable(&exports, 1).IsTrue);
        Assert.Equal(2, RecordingPlugin.ConstructorCalls);
        Assert.True(PluginHost.IsEnabled);
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void A_factory_that_returns_null_fails_the_enable()
    {
        HostingTest.RequireNativeLua();
        var sink = HostingTest.Reset();
        using NativeLuaState state = new();
        using HostSimulator host = new();
        HostingTest.UseFixture(state);
        Assert.Equal(1, host.Initialize<NullReturningPluginFactory>());
        var exports = FakeExports.Create();

        Assert.False(host.CallEnable(&exports, 1).IsTrue);

        Assert.False(PluginHost.IsEnabled);
        Assert.False(LuaRuntime.IsAttached);
        Assert.NotEmpty(sink.Errors("returned null"));
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void Enabling_twice_without_a_disable_is_ignored_with_a_warning()
    {
        HostingTest.RequireNativeLua();
        var sink = HostingTest.Reset();
        using NativeLuaState state = new();
        using HostSimulator host = new();
        var plugin = HostingTest.Enable(host, state);
        var context = PluginHost.Context!;
        var exports = FakeExports.Create();

        Assert.True(host.CallEnable(&exports, 99).IsTrue);

        Assert.Equal(1, plugin.EnableCalls);
        Assert.Same(context, PluginHost.Context);
        Assert.Equal(7u, PluginHost.Context!.PluginId);
        Assert.True(sink.HasEntry(HostLogLevel.Warning, "already enabled"));
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void The_production_module_lookup_finds_the_fixture_when_it_is_Cheat_Engines_DLL()
    {
        HostingTest.RequireNativeLua();
        Assert.SkipUnless(
            string.Equals(Path.GetFileName(NativeLuaLibrary.LibraryPath), LuaModule.CheatEngine64ModuleName,
                StringComparison.OrdinalIgnoreCase),
            "The fixture is not named " + LuaModule.CheatEngine64ModuleName +
            ", so the loaded-module lookup cannot find it.");
        HostingTest.Reset();
        using NativeLuaState state = new();
        using HostSimulator host = new();
        FakeExports.UseState(state.L);
        LuaModuleLocator.Resolver = null; // production lookup: GetModuleHandleExW("lua53-64.dll")
        HostingTest.Bootstrap(host);
        var exports = FakeExports.Create();

        Assert.True(host.CallEnable(&exports, 1).IsTrue);
        Assert.Equal(NativeLuaLibrary.Handle, LuaApi.ModuleHandle);
    }
}
