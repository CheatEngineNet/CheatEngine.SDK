using CheatEngine.SDK.Hosting.Bootstrap;
using CheatEngine.SDK.Hosting.Diagnostics;
using CheatEngine.SDK.Hosting.Tests.Support;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Hosting.Tests.Diagnostics;

/// <summary>
///     WI-6 (A24-21, SRC02-06, A02-11): a faulty or reentrant <see cref="IHostLogSink" /> is contained during every
///     ABI callback. It cannot recurse into <see cref="HostLog.Write" />, cannot re-enter a lifecycle transition, and
///     cannot acquire Lua while a transition owns admission; none of that waits for the sink.
/// </summary>
public sealed class HostLogContainmentTests : IDisposable
{
	public HostLogContainmentTests()
	{
		HostLog.ResetForTests();
	}

	public void Dispose()
	{
		HostLog.ResetForTests();
	}

	[Fact]
	public void A_sink_that_writes_to_HostLog_from_its_own_Write_is_not_reentered_and_is_counted()
	{
		int outerWrites = 0;
		int innerWrites = 0;
		HostLog.Sink = new CallbackLogSink(message =>
		{
			outerWrites++;
			if (string.Equals(message, "outer", StringComparison.Ordinal))
			{
				HostLog.Write(HostLogLevel.Error, "inner");
			}
			else if (string.Equals(message, "inner", StringComparison.Ordinal))
			{
				innerWrites++;
			}
		});

		HostLog.Write(HostLogLevel.Error, "outer");

		Assert.Equal(1, outerWrites);
		Assert.Equal(0, innerWrites);
		Assert.Equal(1, HostLog.DroppedReentrantEntries);

		// Other threads are never affected by this thread's guard.
		int otherThreadWrites = 0;
		Thread other = new(() =>
		{
			HostLog.Sink = new CallbackLogSink(_ => Interlocked.Increment(ref otherThreadWrites));
			HostLog.Write(HostLogLevel.Error, "from another thread");
		});
		other.Start();
		Assert.True(other.Join(TimeSpan.FromSeconds(5)));
		Assert.Equal(1, otherThreadWrites);
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	public void A_throwing_sink_during_EnablePlugin_and_DisablePlugin_never_escapes_and_results_match_the_no_sink_case()
	{
		HostingTest.RequireNativeLua();
		HostingTest.Reset();
		using NativeLuaState state = new();
		using HostSimulator host = new();
		HostLog.Sink = new ThrowingLogSink();
		HostLog.MinimumLevel = HostLogLevel.Trace;

		Exception? enableFailure = Record.Exception(() => HostingTest.Enable(host, state));
		Assert.Null(enableFailure);
		Assert.True(PluginHost.IsEnabled);

		Exception? disableFailure = Record.Exception(() => Assert.True(host.CallDisable().IsTrue));
		Assert.Null(disableFailure);
		Assert.False(PluginHost.IsEnabled);
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	public void A_reentrant_sink_during_EnablePlugin_cannot_recurse_and_the_enable_result_stands()
	{
		HostingTest.RequireNativeLua();
		HostingTest.Reset();
		using NativeLuaState state = new();
		using HostSimulator host = new();
		int sinkEntries = 0;
		HostLog.MinimumLevel = HostLogLevel.Trace;
		HostLog.Sink = new CallbackLogSink(message =>
		{
			sinkEntries++;
			// A sink calling back into HostLog.Write from here would previously recurse; now it is dropped and
			// counted instead, and this Write call returns normally either way.
			HostLog.Write(HostLogLevel.Trace, "reentrant from " + message);
		});

		Exception? failure = Record.Exception(() => HostingTest.Enable(host, state));

		Assert.Null(failure);
		Assert.True(PluginHost.IsEnabled);
		Assert.True(sinkEntries > 0);
		Assert.True(HostLog.DroppedReentrantEntries > 0);
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	public void A_sink_that_acquires_a_Lua_operation_during_a_lifecycle_transition_is_contained()
	{
		HostingTest.RequireNativeLua();
		HostingTest.Reset();
		using NativeLuaState state = new();
		using HostSimulator host = new();
		HostingTest.Enable(host, state);
		HostLog.MinimumLevel = HostLogLevel.Trace;
		bool? acquiredDuringOnDisable = null;
		Exception? acquisitionFailure = null;
		HostLog.Sink = new CallbackLogSink(message =>
		{
			if (string.Equals(message, "RecordingPlugin.OnDisable observed", StringComparison.Ordinal)
				&& acquiredDuringOnDisable is null)
			{
				// OnDisable still runs on the captured main thread while the runtime is attached (operation
				// admission itself does not close until after OnDisable returns): the acquisition succeeds and
				// completes immediately. The containment being proven is that a sink doing this from inside a
				// native callback neither crashes nor deadlocks the transition.
				acquisitionFailure = Record.Exception(() =>
				{
					acquiredDuringOnDisable = LuaRuntime.TryAcquireOperation(out LuaRuntimeOperation operation);
					operation.Dispose();
				});
			}
		});

		Assert.True(host.CallDisable().IsTrue);

		Assert.Null(acquisitionFailure);
		Assert.NotNull(acquiredDuringOnDisable);
		Assert.True(acquiredDuringOnDisable);
		Assert.False(PluginHost.IsEnabled);
		Assert.False(LuaRuntime.IsAttached);
	}

	[Fact]
	public void A_second_factory_rejection_is_logged_outside_the_registration_lock()
	{
		HostingTest.Reset();
		using HostSimulator first = new();
		using HostSimulator other = new();
		Assert.Equal(1, first.Initialize<RecordingPluginFactory>());
		bool? gateHeldWhileLogging = null;
		HostLog.Sink = new CallbackLogSink(message =>
		{
			if (message.Contains("already registered", StringComparison.Ordinal) && gateHeldWhileLogging is null)
			{
				gateHeldWhileLogging = PluginHost.IsRegistrationGateHeldByCurrentThreadForTesting;
			}
		});

		Assert.Equal(0, other.Initialize<AlternatePluginFactory>());

		Assert.NotNull(gateHeldWhileLogging);
		Assert.False(gateHeldWhileLogging);
	}

	private sealed class CallbackLogSink(Action<string> onWrite) : IHostLogSink
	{
		public void Write(HostLogLevel level, string message, Exception? exception)
		{
			onWrite(message);
		}
	}

	private sealed class ThrowingLogSink : IHostLogSink
	{
		public void Write(HostLogLevel level, string message, Exception? exception)
		{
			throw new NotSupportedException("sink failure requested by the test");
		}
	}
}
