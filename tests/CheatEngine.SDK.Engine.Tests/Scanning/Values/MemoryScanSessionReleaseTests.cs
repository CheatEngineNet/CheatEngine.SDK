using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Scanning.Values;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Scanning.Values;

/// <summary>
///     Releasing a session whose scan may still be running (audit A13-26, A13-25, A18-26, Q26): one cooperative
///     <c>terminateScan(false)</c>, one bounded <c>waitTillDone(5000)</c>, then the found list and the scanner destroyed
///     once each, child before parent, even when the stop is not confirmed; no CE call when cleanup cannot safely begin.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class MemoryScanSessionReleaseTests
{
	[Fact]
	[Trait("Qualification", "Q26")]
	public void Dispose_while_scanning_terminates_waits_then_destroys_child_before_parent_once()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		MemoryScanSession session = StartScanning(L);

		session.Dispose();
		session.Dispose();

		Assert.Equal(MemoryScanState.Disposed, session.State);
		Assert.Equal("scan.terminate:false,scan.wait:5000,list.destroy,scan.destroy", MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q26")]
	public void ReleaseWithOutcome_while_scanning_reports_a_confirmed_termination()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		MemoryScanSession session = StartScanning(L);

		MemoryScanReleaseOutcome outcome = session.ReleaseWithOutcome();

		Assert.Equal(MemoryScanTerminationStatus.Confirmed, outcome.Termination);
		Assert.Equal(TargetReleaseStatus.Released, outcome.FoundList.Status);
		Assert.Equal(TargetReleaseStatus.Released, outcome.MemScan.Status);
		Assert.True(outcome.OwnershipConsumed);
		MemScanTestHost.AssertLua(L, "terminate_argument_count == 1 and terminate_argument == false");
		MemScanTestHost.AssertLua(L, "wait_argument_count == 1 and wait_argument_type == 'integer'");
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q26")]
	public void ReleaseWithOutcome_while_scanning_with_a_failing_terminate_is_unconfirmed_and_still_destroys_once()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		MemoryScanSession session = StartScanning(L);
		MemScanTestHost.Run(L, "scan_terminate_raises = true");

		MemoryScanReleaseOutcome outcome = session.ReleaseWithOutcome();

		Assert.Equal(MemoryScanTerminationStatus.TerminateFailed, outcome.Termination);
		Assert.Equal(TargetReleaseStatus.Released, outcome.FoundList.Status);
		Assert.Equal(TargetReleaseStatus.Released, outcome.MemScan.Status);
		Assert.Equal("scan.terminate:false,list.destroy,scan.destroy", MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Theory]
	[Trait("Qualification", "Q26")]
	[InlineData("false", MemoryScanTerminationStatus.WaitTimedOut)]
	[InlineData("raise", MemoryScanTerminationStatus.WaitFailed)]
	[InlineData("none", MemoryScanTerminationStatus.WaitFailed)]
	public void ReleaseWithOutcome_while_scanning_with_an_expired_settle_wait_is_unconfirmed_and_still_destroys_once(
		string waitMode, MemoryScanTerminationStatus expected)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		MemoryScanSession session = StartScanning(L);
		MemScanTestHost.Run(L, "scan_wait_mode = '" + waitMode + "'");

		MemoryScanReleaseOutcome outcome = session.ReleaseWithOutcome();

		Assert.Equal(expected, outcome.Termination);
		Assert.Equal(TargetReleaseStatus.Released, outcome.FoundList.Status);
		Assert.Equal(TargetReleaseStatus.Released, outcome.MemScan.Status);
		Assert.Equal("scan.terminate:false,scan.wait:5000,list.destroy,scan.destroy", MemScanTestHost.ReadTrace(L));
		Assert.Equal(1L, MemScanTestHost.ReadInteger(L, "wait_calls"));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q26")]
	public void ReleaseWithOutcome_after_a_failed_wait_terminates_because_the_scan_may_still_run()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		MemoryScanSession session = StartScanning(L);
		MemScanTestHost.Run(L, "scan_wait_modes = { 'raise', 'true' }");
		Assert.Throws<MemoryScanException>(session.WaitForCompletion);
		Assert.Equal(MemoryScanState.Invalidated, session.State);
		MemScanTestHost.ClearTrace(L);

		MemoryScanReleaseOutcome outcome = session.ReleaseWithOutcome();

		Assert.Equal(MemoryScanTerminationStatus.Confirmed, outcome.Termination);
		Assert.Equal("scan.terminate:false,scan.wait:5000,list.destroy,scan.destroy", MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q26")]
	public void ReleaseWithOutcome_after_a_failed_first_scan_call_terminates_because_ce_may_have_started()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		MemoryScanSession session = MemScanTestHost.CreateSession(L);
		MemScanTestHost.Run(L, "scan_first_raises = true; scan_first_error_payload = 'first scan rejected'");
		Assert.Throws<MemoryScanException>(() =>
			session.StartFirstScan(FirstScanRequest.ByteArray("90", new Address(0x1000), new Address(0x2000))));
		MemScanTestHost.ClearTrace(L);

		MemoryScanReleaseOutcome outcome = session.ReleaseWithOutcome();

		Assert.Equal(MemoryScanTerminationStatus.Confirmed, outcome.Termination);
		Assert.Equal("scan.terminate:false,scan.wait:5000,list.destroy,scan.destroy", MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q26")]
	public void ReleaseWithOutcome_in_new_or_results_ready_never_calls_terminate_scan()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		MemoryScanSession fresh = MemScanTestHost.CreateSession(L);

		MemoryScanReleaseOutcome newOutcome = fresh.ReleaseWithOutcome();

		Assert.Equal(MemoryScanTerminationStatus.NotRequired, newOutcome.Termination);
		Assert.Equal("list.destroy,scan.destroy", MemScanTestHost.ReadTrace(L));

		MemoryScanSession completed = MemScanTestHost.CreateSession(L);
		completed.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
		completed.WaitForCompletion();
		MemScanTestHost.ClearTrace(L);

		MemoryScanReleaseOutcome readyOutcome = completed.ReleaseWithOutcome();

		Assert.Equal(MemoryScanTerminationStatus.NotRequired, readyOutcome.Termination);
		Assert.Equal("list.deinitialize,list.destroy,scan.destroy", MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q26")]
	public void Second_release_while_scanning_returns_the_same_outcome_without_any_CE_call()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		MemoryScanSession session = StartScanning(L);
		MemScanTestHost.Run(L, "scan_wait_mode = 'false'");
		MemoryScanReleaseOutcome first = session.ReleaseWithOutcome();
		MemScanTestHost.ClearTrace(L);

		MemoryScanReleaseOutcome second = session.ReleaseWithOutcome();
		session.Dispose();

		Assert.Equal(first, second);
		Assert.Equal(MemoryScanTerminationStatus.WaitTimedOut, second.Termination);
		Assert.Equal(first, session.LastReleaseOutcome);
		Assert.Equal(string.Empty, MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q26")]
	public void Detach_during_scanning_consumes_both_owners_without_CE_calls_and_reports_not_invoked()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		MemScanTestHost.HostObjects objects = MemScanTestHost.Install(L);
		Assert.Equal(MemoryScanCreationStatus.Success,
			MemoryScanSessions.TryCreateWithOutcome(out MemoryScanSession? created).Status);
		MemoryScanSession session = Assert.IsType<MemoryScanSession>(created);
		session.StartFirstScan(FirstScanRequest.ByteArray("90", new Address(0x1000), new Address(0x2000)));
		MemScanTestHost.ClearTrace(L);

		LuaRuntime.Detach();
		try
		{
			session.Dispose();
		}
		finally
		{
			LuaRuntime.Attach(scope.Binding);
		}

		MemoryScanReleaseOutcome outcome = session.LastReleaseOutcome;
		Assert.Equal(MemoryScanState.Disposed, session.State);
		Assert.Equal(MemoryScanTerminationStatus.NotInvoked, outcome.Termination);
		Assert.Equal(TargetReleaseStatus.NotInvoked, outcome.FoundList.Status);
		Assert.Equal(TargetReleaseStatus.NotInvoked, outcome.MemScan.Status);
		Assert.True(outcome.OwnershipConsumed);
		Assert.False(FakeHost.IsDestroyed(L, objects.FoundList));
		Assert.False(FakeHost.IsDestroyed(L, objects.Scanner));
		Assert.Equal(string.Empty, MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q26")]
	public void Release_on_a_worker_while_scanning_never_calls_CE()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		MemoryScanSession session = StartScanning(L);

		Exception? failure = EngineTest.RunOnWorker(session.Dispose);

		Assert.Null(failure);
		MemoryScanReleaseOutcome outcome = session.LastReleaseOutcome;
		Assert.Equal(MemoryScanState.Disposed, session.State);
		Assert.Equal(MemoryScanTerminationStatus.NotInvoked, outcome.Termination);
		Assert.Equal(TargetReleaseStatus.NotInvoked, outcome.FoundList.Status);
		Assert.Equal(EngineFailureKind.BindingFailure, outcome.FoundList.FailureKind);
		Assert.Equal(string.Empty, MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q26")]
	public void Release_on_a_replaced_target_while_scanning_refuses_every_CE_call_and_reports_not_invoked()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		MemoryScanSession session = StartScanning(L);
		MemScanTestHost.Run(L, "opened_process_id = " + MemScanTestHost.FindOtherQualifiedProcessId());

		MemoryScanReleaseOutcome outcome = session.ReleaseWithOutcome();

		Assert.Equal(MemoryScanTerminationStatus.NotInvoked, outcome.Termination);
		Assert.Equal(TargetReleaseStatus.RefusedTargetChanged, outcome.FoundList.Status);
		Assert.Equal(TargetReleaseStatus.RefusedTargetChanged, outcome.MemScan.Status);
		Assert.Equal(string.Empty, MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	private static MemoryScanSession StartScanning(LuaState state)
	{
		MemoryScanSession session = MemScanTestHost.CreateSession(state);
		session.StartFirstScan(FirstScanRequest.ByteArray("90 90", new Address(0x1000), new Address(0x2000)));
		MemScanTestHost.ClearTrace(state);
		return session;
	}
}
