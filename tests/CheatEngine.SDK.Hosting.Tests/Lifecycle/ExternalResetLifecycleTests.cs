using CheatEngine.SDK.Abi.Managed;
using CheatEngine.SDK.Hosting.Bootstrap;
using CheatEngine.SDK.Hosting.Tests.Support;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Hosting.Tests.Lifecycle;

/// <summary>
///     WI-3 (A08-21, A08-22, A20-Q18-2): the host replacing its Lua state outside the SDK's controlled reset path is
///     detected, logged once, and the plugin recovers cleanly through the ordinary disable/enable cycle.
/// </summary>
public sealed unsafe class ExternalResetLifecycleTests
{
	[Fact]
	[Trait("Category", "NativeLua")]
	[Trait("Qualification", "Q18")]
	public void An_external_reset_during_enable_logs_one_LuaStateReplacedExternally_error()
	{
		HostingTest.RequireNativeLua();
		CapturingLogSink sink = HostingTest.Reset();
		using NativeLuaState state = new();
		using NativeLuaState replacement = new();
		using HostSimulator host = new();
		bool triggeringAcquisitionSucceeded = true;
		RecordingPlugin.ActionInOnEnable = () =>
		{
			// Simulate the host swapping the Lua state behind the SDK's back while this very enable's OnEnable is
			// still running, then take one admitted operation to trigger detection.
			FakeExports.UseState(replacement.L);
			triggeringAcquisitionSucceeded = LuaRuntime.TryAcquireOperation(out LuaRuntimeOperation operation);
			operation.Dispose();
		};

		RecordingPlugin plugin = HostingTest.Enable(host, state);

		Assert.False(triggeringAcquisitionSucceeded);
		Assert.True(PluginHost.IsEnabled);
		Assert.True(LuaRuntime.ExternalStateResetDetected);
		Assert.Single(sink.Errors("LuaStateReplacedExternally"));
		Assert.Equal(1, plugin.EnableCalls);
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	[Trait("Qualification", "Q18")]
	public void Disable_after_an_external_reset_reports_TRUE_after_abandoning_and_the_next_enable_works()
	{
		HostingTest.RequireNativeLua();
		HostingTest.Reset();
		using NativeLuaState state = new();
		using NativeLuaState replacement = new();
		using HostSimulator host = new();
		RecordingPlugin.CreateCallbacksInOnEnable = true;
		RecordingPlugin plugin = HostingTest.Enable(host, state);
		Assert.NotNull(plugin.CallbackOne);

		FakeExports.UseState(replacement.L);
		Assert.False(LuaRuntime.TryAcquireOperation(out LuaRuntimeOperation triggering));
		triggering.Dispose();
		Assert.True(LuaRuntime.ExternalStateResetDetected);

		Assert.True(host.CallDisable().IsTrue);

		Assert.True(plugin.CallbackOne!.IsReleased);
		Assert.True(plugin.CallbackTwo!.IsReleased);
		Assert.False(PluginHost.IsEnabled);

		// Re-enable on the original state works again: Attach clears the external-reset flag and restamps.
		FakeExports.UseState(state.L);
		ManagedExportedFunctions exports = FakeExports.Create();
		Assert.True(host.CallEnable(&exports, 1).IsTrue);

		Assert.True(PluginHost.IsEnabled);
		Assert.False(LuaRuntime.ExternalStateResetDetected);
	}
}
