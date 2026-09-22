using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using CheatEngine.SDK.Abi;
using CheatEngine.SDK.Abi.Managed;
using CheatEngine.SDK.Hosting.Bootstrap;
using CheatEngine.SDK.Hosting.Context;
using CheatEngine.SDK.Hosting.Diagnostics;
using CheatEngine.SDK.Hosting.Tests.Support;
using CheatEngine.SDK.Hosting.Threading;
using CheatEngine.SDK.Lua.Callbacks;
using CheatEngine.SDK.Lua.Interop.Api;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Hosting.Tests.Lifecycle;

/// <summary>
///     Regression coverage for the exact admission boundary between the synchronous main-thread dispatcher and host
///     shutdown. These tests use barriers and explicit worker completion rather than timing-dependent sleeps.
/// </summary>
public sealed unsafe class AdmissionLifecycleTests
{
	[Fact]
	[Trait("Category", "NativeLua")]
	public void Disable_nested_in_inline_main_thread_work_is_refused_and_the_enable_stands()
	{
		HostingTest.RequireNativeLua();
		CapturingLogSink sink = HostingTest.Reset();
		using NativeLuaState state = new();
		using HostSimulator host = new();
		RecordingPlugin plugin = HostingTest.Enable(host, state);
		StrongBox<Bool32> nestedResult = new();

		MainThread.Invoke(static state => state.Result.Value = state.Host.CallDisable(),
			(Host: host, Result: nestedResult));

		Assert.False(nestedResult.Value.IsTrue);
		Assert.Equal(0, plugin.DisableCalls);
		Assert.True(PluginHost.IsEnabled);
		Assert.Equal(PluginHostLifecyclePhase.Enabled, PluginHost.Phase);
		Assert.True(LuaRuntime.IsAttached);
		Assert.NotEmpty(sink.Errors("inline main-thread work"));
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	public void A_stale_context_cannot_admit_work_after_reenable()
	{
		HostingTest.RequireNativeLua();
		HostingTest.Reset();
		using NativeLuaState state = new();
		using HostSimulator host = new();
		HostingTest.Enable(host, state, 1);
		PluginContext stale = PluginHost.Context!;

		Assert.True(host.CallDisable().IsTrue);
		ManagedExportedFunctions exports = FakeExports.Create();
		Assert.True(host.CallEnable(&exports, 2).IsTrue);
		PluginContext current = PluginHost.Context!;

		InvalidOperationException failure =
			Assert.Throws<InvalidOperationException>(() => PluginHost.AdmitMainThreadWork(stale));

		Assert.Contains("stopping or disabled", failure.Message, StringComparison.Ordinal);
		Assert.False(stale.IsCurrent);
		Assert.True(stale.ShutdownToken.IsCancellationRequested);
		Assert.True(current.IsCurrent);
		PluginHost.MainThreadWorkAdmission currentAdmission = PluginHost.AdmitMainThreadWork(current);
		Assert.NotNull(currentAdmission);
		currentAdmission.Dispose();
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	[SuppressMessage("xUnit.Analyzers", "xUnit1051",
		Justification = "The worker is joined against a deterministic admission-closed point in OnDisable.")]
	public void A_worker_cannot_queue_work_after_disable_has_closed_admission()
	{
		HostingTest.RequireNativeLua();
		HostingTest.Reset();
		using NativeLuaState state = new();
		using HostSimulator host = new();
		HostingTest.Enable(host, state);
		StrongBox<Exception?> workerFailure = new();
		StrongBox<bool> workerCompleted = new();
		int dispatchAttempts = 0;
		MainThreadDispatcher.DispatchOverrideForTests = _ => Interlocked.Increment(ref dispatchAttempts);
		RecordingPlugin.NestedCallInOnDisable = () => StartAdmissionClosedWorker(
			workerFailure,
			workerCompleted);

		try
		{
			Assert.True(host.CallDisable().IsTrue);

			Assert.True(workerCompleted.Value);
			InvalidOperationException failure = Assert.IsType<InvalidOperationException>(workerFailure.Value);
			Assert.Contains("no longer accepts new main-thread dispatch", failure.Message, StringComparison.Ordinal);
			Assert.Equal(0, dispatchAttempts);
		}
		finally
		{
			MainThreadDispatcher.DispatchOverrideForTests = null;
		}
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	public void A_throwing_shutdown_registration_is_neutralized_and_cleanup_completes()
	{
		HostingTest.RequireNativeLua();
		CapturingLogSink sink = HostingTest.Reset();
		using NativeLuaState state = new();
		using HostSimulator host = new();
		RecordingPlugin plugin = HostingTest.Enable(host, state);
		PluginContext context = PluginHost.Context!;
		using CancellationTokenRegistration registration = context.ShutdownToken.Register(static () =>
			throw new InvalidOperationException("shutdown registration failure requested by the test"));

		Assert.True(host.CallDisable().IsTrue);

		Assert.Equal(1, plugin.DisableCalls);
		Assert.True(context.ShutdownToken.IsCancellationRequested);
		Assert.False(PluginHost.IsEnabled);
		Assert.False(LuaRuntime.IsAttached);
		Assert.True(sink.HasEntry(HostLogLevel.Error, "shutdown callback threw"));
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	public void A_callback_from_the_previous_enable_fails_before_invoking_plugin_code()
	{
		HostingTest.RequireNativeLua();
		HostingTest.Reset();
		using NativeLuaState state = new();
		using HostSimulator host = new();
		HostingTest.Enable(host, state);
		LuaState L = LuaRuntime.AcquireState();
		CallbackInvocationCounter counter = new();
		Assert.True(LuaCallback.TryCreate(L, new LuaNativeFunction(&CountInvocation), counter,
			out LuaCallback<CallbackInvocationCounter>? callback).IsOk);
		Assert.NotNull(callback);
		Assert.True(callback.TryRegister(L, "staleLifecycleCallback"u8).IsOk);

		Assert.True(host.CallDisable().IsTrue);
		ManagedExportedFunctions exports = FakeExports.Create();
		Assert.True(host.CallEnable(&exports, 2).IsTrue);

		using (LuaFrame frame = new(L))
		{
			Assert.True(L.TryExecute("local ok = pcall(staleLifecycleCallback) return ok"u8, 1).IsOk);
			Assert.False(L.ToBoolean(-1));
		}

		Assert.True(callback.IsReleased);
		Assert.Null(callback.StateObject);
		Assert.Equal(0, counter.InvocationCount);
		Assert.Equal(0, LuaApi.lua_gettop(state.L));
	}

	private static Bool32 StartAdmissionClosedWorker(
		StrongBox<Exception?> workerFailure,
		StrongBox<bool> workerCompleted)
	{
		Thread worker = new(() =>
		{
			workerFailure.Value = Record.Exception(() => MainThread.Invoke(static _ =>
			{
			}, 0));
			workerCompleted.Value = true;
		});

		worker.Start();
		if (!worker.Join(TimeSpan.FromSeconds(5)))
		{
			throw new TimeoutException("The admission-closed worker did not return.");
		}

		return Bool32.True;
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	private static int CountInvocation(nint pointer)
	{
		LuaState state = new(pointer);
		try
		{
			if (!LuaThunk.TryGetState(state, out CallbackInvocationCounter? counter))
			{
				return LuaThunk.Fail(state, "callback released"u8);
			}

			counter.InvocationCount++;
			return 0;
		}
		catch (Exception exception)
		{
			return LuaThunk.Fail(state, exception);
		}
	}

	private sealed class CallbackInvocationCounter
	{
		public int InvocationCount
		{
			get;
			set;
		}
	}
}
