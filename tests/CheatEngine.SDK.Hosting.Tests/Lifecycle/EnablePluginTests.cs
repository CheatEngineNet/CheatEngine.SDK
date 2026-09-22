using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using CheatEngine.SDK.Abi;
using CheatEngine.SDK.Abi.Managed;
using CheatEngine.SDK.Hosting.Bootstrap;
using CheatEngine.SDK.Hosting.Context;
using CheatEngine.SDK.Hosting.Diagnostics;
using CheatEngine.SDK.Hosting.Tests.Support;
using CheatEngine.SDK.Hosting.Threading;
using CheatEngine.SDK.Lua.Callbacks;
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
		CapturingLogSink sink = HostingTest.Reset();
		using HostSimulator host = new();
		HostingTest.Bootstrap(host);

		Bool32 result = host.CallEnable(null, 1);

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
		CapturingLogSink sink = HostingTest.Reset();
		using HostSimulator host = new();
		HostingTest.Bootstrap(host);
		ManagedExportedFunctions exports = FakeExports.Create();
		exports.SizeOfExportedFunctions = reportedSize;

		Bool32 result = host.CallEnable(&exports, 1);

		Assert.False(result.IsTrue);
		Assert.False(PluginHost.IsEnabled);
		Assert.False(LuaRuntime.IsAttached);
		Assert.Equal(0, RecordingPlugin.ConstructorCalls);
		Assert.NotEmpty(sink.Errors(reportedSize.ToString(CultureInfo.InvariantCulture) + "-byte exports record"));
	}

	[Fact]
	public void A_record_without_GetLuaState_fails()
	{
		CapturingLogSink sink = HostingTest.Reset();
		using HostSimulator host = new();
		HostingTest.Bootstrap(host);
		ManagedExportedFunctions exports = FakeExports.Create();
		exports.GetLuaState = null;

		Assert.False(host.CallEnable(&exports, 1).IsTrue);
		Assert.NotEmpty(sink.Errors("no GetLuaState"));
	}

	[Fact]
	public void Enable_before_the_bootstrap_fails()
	{
		CapturingLogSink sink = HostingTest.Reset();
		using HostSimulator host = new();
		HostingTest.Bootstrap(host);
		delegate* unmanaged[Stdcall]<ManagedExportedFunctions*, uint, Bool32> enable = host.Record.EnablePlugin;
		PluginHost.ResetForTests();
		ManagedExportedFunctions exports = FakeExports.Create();

		Assert.False(enable(&exports, 1).IsTrue);
		Assert.NotEmpty(sink.Errors("bootstrap has not run"));
	}

	[Fact]
	public void Without_a_Lua_module_in_the_process_the_enable_fails_before_any_plugin_code()
	{
		CapturingLogSink sink = HostingTest.Reset();
		HostingTest.UseNoModule();
		using HostSimulator host = new();
		HostingTest.Bootstrap(host);
		ManagedExportedFunctions exports = FakeExports.Create();

		Bool32 result = host.CallEnable(&exports, 1);

		Assert.False(result.IsTrue);
		Assert.False(PluginHost.IsEnabled);
		Assert.Equal(0, RecordingPlugin.ConstructorCalls);
		Assert.Equal(0, FakeExports.GetLuaStateCalls);
		Assert.NotEmpty(sink.Errors("resolver returned no Lua module"));
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	[SuppressMessage("Meziantou.Analyzer", "MA0051",
		Justification =
			"This test verifies every lifecycle invariant after a successful enable and intentionally keeps the assertions together.")]
	public void Enables_the_plugin_binds_Lua_attaches_the_runtime_and_runs_OnEnable_on_this_thread()
	{
		HostingTest.RequireNativeLua();
		CapturingLogSink sink = HostingTest.Reset();
		using NativeLuaState state = new();
		using HostSimulator host = new();
		HostingTest.UseFixture(state);
		HostingTest.Bootstrap(host);
		ManagedExportedFunctions exports = FakeExports.Create();

		Bool32 result = host.CallEnable(&exports, 42);

		Assert.True(result.IsTrue);
		Assert.True(PluginHost.IsEnabled);
		Assert.True(LuaApi.IsInitialized);
		Assert.True(LuaRuntime.IsAttached);
		Assert.Equal(FakeExports.GetLuaStateAddress, LuaRuntime.CurrentBinding.StateProvider);
		Assert.Equal(FakeExports.PusherAddress, LuaRuntime.CurrentBinding.HostObjectPusher);
		Assert.Equal(Environment.CurrentManagedThreadId, LuaRuntime.CurrentBinding.MainThreadId);

		RecordingPlugin plugin = Assert.IsType<RecordingPlugin>(PluginHost.PluginForTests);
		Assert.Equal(1, RecordingPlugin.ConstructorCalls);
		Assert.False(RecordingPlugin.RuntimeAttachedInConstructor);
		Assert.False(RecordingPlugin.HostEnabledInConstructor);
		Assert.Equal(1, plugin.EnableCalls);
		Assert.Equal(Environment.CurrentManagedThreadId, plugin.EnableThreadId);
		Assert.True(plugin.RuntimeAttachedInOnEnable);
		Assert.False(plugin.HostEnabledInOnEnable);
		Assert.True(plugin.MainThreadInOnEnable);
		Assert.Equal(42, plugin.LuaResultInOnEnable);

		PluginContext context = Assert.IsType<PluginContext>(PluginHost.Context);
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
		ManagedExportedFunctions exports = FakeExports.Create();

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
		byte* buffer = stackalloc byte[64];
		new Span<byte>(buffer, 64).Fill(0xFF);
		*(ManagedExportedFunctions*) buffer = FakeExports.Create(64);

		Assert.True(host.CallEnable((ManagedExportedFunctions*) buffer, 3).IsTrue);

		Assert.Equal(64, PluginHost.Context!.ReportedExportsSize);
		Assert.Equal(0xFF, buffer[48]);
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	public void A_state_provider_that_returns_null_fails_the_self_check()
	{
		HostingTest.RequireNativeLua();
		CapturingLogSink sink = HostingTest.Reset();
		using NativeLuaState state = new();
		using HostSimulator host = new();
		HostingTest.UseFixture(state);
		FakeExports.UseState(null);
		HostingTest.Bootstrap(host);
		ManagedExportedFunctions exports = FakeExports.Create();

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
		CapturingLogSink sink = HostingTest.Reset();
		using NativeLuaState state = new();
		using HostSimulator host = new();
		HostingTest.UseFixture(state);
		HostingTest.Bootstrap(host);
		RecordingPlugin.ThrowInOnEnable = true;
		ManagedExportedFunctions exports = FakeExports.Create();

		Bool32 result = host.CallEnable(&exports, 1);

		Assert.False(result.IsTrue);
		Assert.False(PluginHost.IsEnabled);
		Assert.Equal(PluginHostLifecyclePhase.Registered, PluginHost.Phase);
		Assert.Null(PluginHost.Context);
		Assert.False(LuaRuntime.IsAttached);
		Assert.Equal(1, RecordingPlugin.LastConstructed!.EnableCalls);
		Assert.True(RecordingPlugin.LastConstructed.ContextInOnEnable!.ShutdownToken.IsCancellationRequested);
		(HostLogLevel, string, Exception?) entry = Assert.Single(sink.Errors("OnEnable threw"));
		InvalidOperationException exception = Assert.IsType<InvalidOperationException>(entry.Item3);
		Assert.Contains("requested by the test", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	public void OnEnable_failure_with_detach_failure_keeps_incomplete_cleanup_retryable()
	{
		HostingTest.RequireNativeLua();
		CapturingLogSink sink = HostingTest.Reset();
		using NativeLuaState state = new();
		using HostSimulator host = new();
		HostingTest.UseFixture(state);
		HostingTest.Bootstrap(host);
		RecordingPlugin.ThrowInOnEnable = true;
		RecordingPlugin.CreateCallbacksInOnEnable = true;
		int releases = 0;
		LuaCallbackRegistry.AfterReleaseForTesting = () =>
		{
			if (++releases == 1)
			{
				throw new InvalidOperationException("deterministic failed-enable cleanup failure");
			}
		};
		ManagedExportedFunctions exports = FakeExports.Create();

		try
		{
			Assert.False(host.CallEnable(&exports, 1).IsTrue);

			RecordingPlugin plugin = RecordingPlugin.LastConstructed!;
			Assert.NotNull(plugin);
			Assert.True(LuaRuntime.IsAttached);
			Assert.Equal(PluginHostLifecyclePhase.Disabling, PluginHost.Phase);
			Assert.NotNull(PluginHost.Context);
			Assert.True(plugin.CallbackTwo!.IsReleased);
			Assert.False(plugin.CallbackOne!.IsReleased);
			Assert.NotEmpty(sink.Errors("shutdown remains incomplete"));

			LuaCallbackRegistry.AfterReleaseForTesting = null;
			Assert.True(host.CallDisable().IsTrue);
			Assert.False(LuaRuntime.IsAttached);
			Assert.Null(PluginHost.Context);
			Assert.Equal(PluginHostLifecyclePhase.Registered, PluginHost.Phase);
			Assert.True(plugin.CallbackOne.IsReleased);
		}
		finally
		{
			LuaCallbackRegistry.AfterReleaseForTesting = null;
		}
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	public void A_failed_cleanup_retry_rejects_nested_disable_until_the_retry_unwinds()
	{
		HostingTest.RequireNativeLua();
		CapturingLogSink sink = HostingTest.Reset();
		using NativeLuaState state = new();
		using HostSimulator host = new();
		HostingTest.UseFixture(state);
		HostingTest.Bootstrap(host);
		RecordingPlugin.ThrowInOnEnable = true;
		RecordingPlugin.CreateCallbacksInOnEnable = true;
		int releases = 0;
		LuaCallbackRegistry.AfterReleaseForTesting = () =>
		{
			if (++releases == 1)
			{
				throw new InvalidOperationException("deterministic failed-enable cleanup failure");
			}
		};
		bool nestedRequested = false;
		bool nestedResult = true;
		ManagedExportedFunctions exports = FakeExports.Create();

		try
		{
			Assert.False(host.CallEnable(&exports, 1).IsTrue);

			releases = 0;
			sink.OnMessage = message =>
			{
				if (!nestedRequested && message.Contains("shutdown remains incomplete", StringComparison.Ordinal))
				{
					nestedRequested = true;
					nestedResult = host.CallDisable().IsTrue;
				}
			};
			Assert.False(host.CallDisable().IsTrue);
			Assert.True(nestedRequested);
			Assert.False(nestedResult);
			Assert.True(LuaRuntime.IsAttached);
			Assert.Equal(PluginHostLifecyclePhase.Disabling, PluginHost.Phase);
			Assert.True(sink.HasEntry(HostLogLevel.Error, "a disable transition is already completing"));

			LuaCallbackRegistry.AfterReleaseForTesting = null;
			sink.OnMessage = null;
			Assert.True(host.CallDisable().IsTrue);
			Assert.False(LuaRuntime.IsAttached);
			Assert.Equal(PluginHostLifecyclePhase.Registered, PluginHost.Phase);
		}
		finally
		{
			LuaCallbackRegistry.AfterReleaseForTesting = null;
			sink.OnMessage = null;
		}
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	public void A_throwing_constructor_fails_the_enable_and_is_retried_on_the_next_enable()
	{
		HostingTest.RequireNativeLua();
		CapturingLogSink sink = HostingTest.Reset();
		using NativeLuaState state = new();
		using HostSimulator host = new();
		HostingTest.UseFixture(state);
		HostingTest.Bootstrap(host);
		RecordingPlugin.ThrowInConstructor = true;
		ManagedExportedFunctions exports = FakeExports.Create();

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
		CapturingLogSink sink = HostingTest.Reset();
		using NativeLuaState state = new();
		using HostSimulator host = new();
		HostingTest.UseFixture(state);
		Assert.Equal(1, host.Initialize<NullReturningPluginFactory>());
		ManagedExportedFunctions exports = FakeExports.Create();

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
		CapturingLogSink sink = HostingTest.Reset();
		using NativeLuaState state = new();
		using HostSimulator host = new();
		RecordingPlugin plugin = HostingTest.Enable(host, state);
		PluginContext context = PluginHost.Context!;
		ManagedExportedFunctions exports = FakeExports.Create();

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
		ManagedExportedFunctions exports = FakeExports.Create();

		Assert.True(host.CallEnable(&exports, 1).IsTrue);
		Assert.Equal(NativeLuaLibrary.Handle, LuaApi.ModuleHandle);
	}
}
