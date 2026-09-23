using System.Globalization;
using System.Text;

using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Scanning.Values;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

// These tests exercise the CESDK5010 experimental members on purpose; the opt-in is scoped to this file.
#pragma warning disable CESDK5010

namespace CheatEngine.SDK.Engine.Tests.Scanning.Values;

/// <summary>
///     The deadline, cooperative termination and host error text primitives of a scan session (spike D4.7, audit
///     A13-26, A13-28, Q29): CE's <c>waitTillDone(timeout)</c> with one integer argument and one boolean result,
///     <c>terminateScan(false)</c> followed by one bounded wait, and a bounded, unparsed copy of <c>ErrorString</c>.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class MemoryScanSessionDeadlineTests
{
	private static readonly TimeSpan Deadline = TimeSpan.FromMilliseconds(250);

	[Fact]
	[Trait("Qualification", "Q29")]
	public void TryWaitForCompletion_true_initializes_the_found_list_and_reports_completed()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using MemoryScanSession session = StartScanning(L);

		MemoryScanWaitStatus status = session.TryWaitForCompletion(Deadline);

		Assert.Equal(MemoryScanWaitStatus.Completed, status);
		Assert.Equal(MemoryScanState.ResultsReady, session.State);
		Assert.Equal(MemoryScanInvalidationReason.None, session.InvalidationReason);
		Assert.Equal("scan.wait:250,list.initialize", MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q29")]
	public void TryWaitForCompletion_false_keeps_scanning_without_initializing_and_reports_timed_out()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using MemoryScanSession session = StartScanning(L);
		MemScanTestHost.Run(L, "scan_wait_mode = 'false'");

		MemoryScanWaitStatus status = session.TryWaitForCompletion(Deadline);

		Assert.Equal(MemoryScanWaitStatus.TimedOut, status);
		Assert.Equal(MemoryScanState.Scanning, session.State);
		Assert.Equal(MemoryScanInvalidationReason.None, session.InvalidationReason);
		Assert.Throws<MemoryScanStateException>(() => session.Results);
		Assert.Equal("scan.wait:250", MemScanTestHost.ReadTrace(L));

		MemScanTestHost.Run(L, "scan_wait_mode = 'true'");
		Assert.Equal(MemoryScanWaitStatus.Completed, session.TryWaitForCompletion(Deadline));
		Assert.Equal(MemoryScanState.ResultsReady, session.State);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q29")]
	public void TryWaitForCompletion_passes_one_integer_millisecond_argument_and_reads_one_result()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using MemoryScanSession session = StartScanning(L);
		MemScanTestHost.Run(L, "scan_wait_mode = 'false'");

		Assert.Equal(MemoryScanWaitStatus.TimedOut, session.TryWaitForCompletion(TimeSpan.FromSeconds(1.5)));
		MemScanTestHost.AssertLua(L, "wait_argument_count == 1");
		MemScanTestHost.AssertLua(L, "wait_argument_type == 'integer' and wait_argument == 1500");

		using FakeHost.PCallProbe probe =
			FakeHost.ReplaceWaitTillDoneWithPCallProbe(L, session.Scanner.Handle);
		MemoryScanWaitStatus probed = session.TryWaitForCompletion(TimeSpan.FromSeconds(1.5));

		Assert.Equal(1, probe.WaitCallCount);
		Assert.Equal(1, probe.WaitArgumentCount);
		Assert.Equal(1, probe.WaitResultCount);
		Assert.Equal(MemoryScanWaitStatus.InvalidResult, probed);
		Assert.Equal(0, L.Top);
	}

	[Theory]
	[Trait("Qualification", "Q29")]
	[InlineData(0L)]
	[InlineData(-10_000L)]
	[InlineData(-1L)]
	[InlineData((int.MaxValue + 1L) * TimeSpan.TicksPerMillisecond)]
	[InlineData(long.MaxValue)]
	public void TryWaitForCompletion_validates_timeouts_before_any_CE_call(long ticks)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using MemoryScanSession session = StartScanning(L);
		// -10 000 ticks is Timeout.InfiniteTimeSpan; long.MaxValue ticks is TimeSpan.MaxValue.
		TimeSpan timeout = TimeSpan.FromTicks(ticks);

		ArgumentOutOfRangeException failure =
			Assert.Throws<ArgumentOutOfRangeException>(() => session.TryWaitForCompletion(timeout));

		Assert.Equal("timeout", failure.ParamName);
		Assert.Equal(MemoryScanState.Scanning, session.State);
		Assert.Equal(string.Empty, MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Theory]
	[Trait("Qualification", "Q29")]
	[InlineData(4_000L, 1L)]
	[InlineData(10_001L, 2L)]
	[InlineData(int.MaxValue * TimeSpan.TicksPerMillisecond, int.MaxValue)]
	public void TryWaitForCompletion_rounds_a_partial_millisecond_up_and_accepts_int_max_milliseconds(long ticks,
		long expectedMilliseconds)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using MemoryScanSession session = StartScanning(L);
		MemScanTestHost.Run(L, "scan_wait_mode = 'false'");

		Assert.Equal(MemoryScanWaitStatus.TimedOut, session.TryWaitForCompletion(TimeSpan.FromTicks(ticks)));

		Assert.Equal(expectedMilliseconds, MemScanTestHost.ReadInteger(L, "wait_argument"));
		MemScanTestHost.AssertLua(L, "wait_argument_type == 'integer'");
	}

	[Theory]
	[Trait("Qualification", "Q29")]
	[InlineData("nil")]
	[InlineData("none")]
	[InlineData("number")]
	[InlineData("string")]
	public void TryWaitForCompletion_non_boolean_result_is_invalid_result_and_invalidates(string mode)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using MemoryScanSession session = StartScanning(L);
		MemScanTestHost.Run(L, "scan_wait_mode = '" + mode + "'");

		MemoryScanWaitStatus status = session.TryWaitForCompletion(Deadline);

		Assert.Equal(MemoryScanWaitStatus.InvalidResult, status);
		Assert.Equal(MemoryScanState.Invalidated, session.State);
		Assert.Equal(MemoryScanInvalidationReason.ProtectedLuaFailure, session.InvalidationReason);
		Assert.Equal("scan.wait:250", MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q29")]
	public void TryWaitForCompletion_lua_error_is_lua_failure_and_invalidates()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using MemoryScanSession session = StartScanning(L);
		MemScanTestHost.Run(L, "scan_wait_mode = 'raise'");

		MemoryScanWaitStatus status = session.TryWaitForCompletion(Deadline);

		Assert.Equal(MemoryScanWaitStatus.LuaFailure, status);
		Assert.Equal(MemoryScanState.Invalidated, session.State);
		Assert.Equal(MemoryScanInvalidationReason.ProtectedLuaFailure, session.InvalidationReason);
		Assert.Equal("scan.wait:250", MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q26")]
	public void TryWaitForCompletion_initialize_failure_after_completion_is_initialization_failed_and_never_exposes_results()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using MemoryScanSession session = StartScanning(L);
		MemScanTestHost.Run(L, "list_initialize_raises = true");

		MemoryScanWaitStatus status = session.TryWaitForCompletion(Deadline);

		Assert.Equal(MemoryScanWaitStatus.InitializationFailed, status);
		Assert.Equal(MemoryScanState.Invalidated, session.State);
		Assert.Throws<MemoryScanStateException>(() => session.Results);
		Assert.Throws<MemoryScanStateException>(() => session.TryCopyResults(new MemoryScanResult[4], out _, out _));
		Assert.Equal("scan.wait:250,list.initialize", MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q26")]
	public void TryWaitForCompletion_after_a_target_replacement_reports_the_mismatch_without_calling_CE()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		MemoryScanSession session = StartScanning(L);
		MemScanTestHost.Run(L, "opened_process_id = " + MemScanTestHost.FindOtherQualifiedProcessId().ToString(CultureInfo.InvariantCulture));

		MemoryScanWaitStatus status = session.TryWaitForCompletion(Deadline);

		Assert.Equal(MemoryScanWaitStatus.TargetIdentityMismatch, status);
		Assert.Equal(MemoryScanState.Invalidated, session.State);
		Assert.Equal(MemoryScanInvalidationReason.TargetChanged, session.InvalidationReason);
		Assert.Equal(TargetIdentityCheckKind.TargetChanged, session.LastTargetCheck!.Value.Kind);
		Assert.Equal(string.Empty, MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
		session.Abandon();
	}

	[Fact]
	[Trait("Qualification", "Q29")]
	public void TryTerminateScan_requests_cooperative_termination_then_waits_and_invalidates_as_scan_terminated()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using MemoryScanSession session = StartScanning(L);

		MemoryScanTerminationStatus status = session.TryTerminateScan(TimeSpan.FromSeconds(2));

		Assert.Equal(MemoryScanTerminationStatus.Confirmed, status);
		Assert.Equal(MemoryScanState.Invalidated, session.State);
		Assert.Equal(MemoryScanInvalidationReason.ScanTerminated, session.InvalidationReason);
		Assert.Throws<MemoryScanStateException>(() => session.Results);
		Assert.Equal("scan.terminate:false,scan.wait:2000", MemScanTestHost.ReadTrace(L));

		MemScanTestHost.ClearTrace(L);
		session.Reset();

		Assert.Equal(MemoryScanState.New, session.State);
		Assert.Equal(MemoryScanInvalidationReason.None, session.InvalidationReason);
		Assert.Equal("list.deinitialize,scan.new", MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q29")]
	public void TryTerminateScan_passes_an_explicit_false_force_argument()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using MemoryScanSession session = StartScanning(L);

		Assert.Equal(MemoryScanTerminationStatus.Confirmed, session.TryTerminateScan(TimeSpan.FromMilliseconds(40)));

		MemScanTestHost.AssertLua(L, "terminate_argument_count == 1");
		MemScanTestHost.AssertLua(L, "type(terminate_argument) == 'boolean' and terminate_argument == false");
		MemScanTestHost.AssertLua(L,
			"wait_argument_count == 1 and wait_argument_type == 'integer' and wait_argument == 40");
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q29")]
	public void TryTerminateScan_failed_terminate_skips_the_wait_and_reports_terminate_failed()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using MemoryScanSession session = StartScanning(L);
		MemScanTestHost.Run(L, "scan_terminate_raises = true");

		MemoryScanTerminationStatus status = session.TryTerminateScan(Deadline);

		Assert.Equal(MemoryScanTerminationStatus.TerminateFailed, status);
		Assert.Equal(MemoryScanState.Invalidated, session.State);
		Assert.Equal(MemoryScanInvalidationReason.ScanTerminated, session.InvalidationReason);
		Assert.Equal("scan.terminate:false", MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Theory]
	[Trait("Qualification", "Q29")]
	[InlineData("false", MemoryScanTerminationStatus.WaitTimedOut)]
	[InlineData("raise", MemoryScanTerminationStatus.WaitFailed)]
	[InlineData("nil", MemoryScanTerminationStatus.WaitFailed)]
	[InlineData("number", MemoryScanTerminationStatus.WaitFailed)]
	public void TryTerminateScan_unconfirmed_wait_blocks_reset_but_allows_release(string waitMode,
		MemoryScanTerminationStatus expected)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		MemoryScanSession session = StartScanning(L);
		MemScanTestHost.Run(L, "scan_wait_mode = '" + waitMode + "'");

		MemoryScanTerminationStatus status = session.TryTerminateScan(Deadline);

		Assert.Equal(expected, status);
		MemoryScanStateException refused = Assert.Throws<MemoryScanStateException>(session.Reset);
		Assert.Equal("Reset", refused.Operation);
		Assert.Throws<MemoryScanStateException>(() => session.TryTerminateScan(Deadline));
		Assert.Equal("scan.terminate:false,scan.wait:250", MemScanTestHost.ReadTrace(L));

		MemScanTestHost.ClearTrace(L);
		MemoryScanReleaseOutcome outcome = session.ReleaseWithOutcome();

		Assert.Equal(MemoryScanState.Disposed, session.State);
		Assert.Equal(expected, outcome.Termination);
		Assert.Equal(TargetReleaseStatus.Released, outcome.FoundList.Status);
		Assert.Equal(TargetReleaseStatus.Released, outcome.MemScan.Status);
		Assert.Equal("list.destroy,scan.destroy", MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q29")]
	public void TryTerminateScan_unconfirmed_stop_is_kept_by_a_refused_release_without_any_CE_call()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		MemoryScanSession session = StartScanning(L);
		MemScanTestHost.Run(L, "scan_wait_mode = 'false'");
		Assert.Equal(MemoryScanTerminationStatus.WaitTimedOut, session.TryTerminateScan(Deadline));
		MemScanTestHost.Run(L, "opened_process_id = " +
							   MemScanTestHost.FindOtherQualifiedProcessId().ToString(CultureInfo.InvariantCulture));
		MemScanTestHost.ClearTrace(L);

		MemoryScanReleaseOutcome outcome = session.ReleaseWithOutcome();

		// The release is refused on the replaced target, and it reports the stop that was requested, not NotInvoked.
		Assert.Equal(MemoryScanTerminationStatus.WaitTimedOut, outcome.Termination);
		Assert.Equal(TargetReleaseStatus.RefusedTargetChanged, outcome.FoundList.Status);
		Assert.Equal(TargetReleaseStatus.RefusedTargetChanged, outcome.MemScan.Status);
		Assert.Equal(string.Empty, MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q29")]
	public void TryTerminateScan_without_a_started_scan_is_a_state_error_without_a_CE_call()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using MemoryScanSession session = MemScanTestHost.CreateSession(L);

		MemoryScanStateException beforeScan =
			Assert.Throws<MemoryScanStateException>(() => session.TryTerminateScan(Deadline));
		session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
		session.WaitForCompletion();
		MemScanTestHost.ClearTrace(L);
		MemoryScanStateException afterCompletion =
			Assert.Throws<MemoryScanStateException>(() => session.TryTerminateScan(Deadline));

		Assert.Equal(MemoryScanState.New, beforeScan.State);
		Assert.Equal(MemoryScanState.ResultsReady, afterCompletion.State);
		Assert.Equal(MemoryScanState.ResultsReady, session.State);
		Assert.Equal(string.Empty, MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q29")]
	public void TryTerminateScan_with_a_replaced_target_is_not_invoked_and_makes_no_scanner_call()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		MemoryScanSession session = StartScanning(L);
		MemScanTestHost.Run(L, "opened_process_id = " + MemScanTestHost.FindOtherQualifiedProcessId().ToString(CultureInfo.InvariantCulture));

		MemoryScanTerminationStatus status = session.TryTerminateScan(Deadline);

		Assert.Equal(MemoryScanTerminationStatus.NotInvoked, status);
		Assert.Equal(MemoryScanState.Invalidated, session.State);
		Assert.Equal(MemoryScanInvalidationReason.TargetChanged, session.InvalidationReason);
		Assert.Equal(string.Empty, MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
		session.Abandon();
	}

	[Fact]
	[Trait("Qualification", "Q29")]
	public void TryGetHostErrorText_copies_at_most_the_bound_at_a_utf8_boundary_and_reports_truncation()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using MemoryScanSession session = StartScanning(L);
		string full = "a" + string.Concat(Enumerable.Repeat("é€😀", 200));
		MemScanTestHost.Run(L, "scan_error_string = '" + full + "'");

		Assert.True(session.TryGetHostErrorText(out string? text, out bool truncated));

		Assert.True(truncated);
		Assert.StartsWith(text, full, StringComparison.Ordinal);
		Assert.True(Encoding.UTF8.GetByteCount(text) <= MemoryScanSession.HostErrorTextMaximumUtf8Bytes);
		Assert.True(Encoding.UTF8.GetByteCount(text) > MemoryScanSession.HostErrorTextMaximumUtf8Bytes - 4);
		Assert.DoesNotContain('�', text);
		Assert.Equal(MemoryScanState.Scanning, session.State);

		MemScanTestHost.Run(L, "scan_error_string = 'short'");
		Assert.True(session.TryGetHostErrorText(out string? shortText, out bool shortTruncated));
		Assert.Equal("short", shortText);
		Assert.False(shortTruncated);
		Assert.Equal("scan.ErrorString,scan.ErrorString", MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Theory]
	[Trait("Qualification", "Q29")]
	[InlineData(new byte[] { 0x41, 0xC3, 0xA9, 0x42 }, 2, "A")]
	[InlineData(new byte[] { 0x41, 0xE2, 0x82, 0xAC, 0x42 }, 3, "A")]
	[InlineData(new byte[] { 0x41, 0xF0, 0x9F, 0x98, 0x80, 0x42 }, 4, "A")]
	[InlineData(new byte[] { 0x41, 0xF0, 0x9F, 0x98, 0x80, 0x42 }, 5, "A😀")]
	[InlineData(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80 }, 4, "����")]
	[InlineData(new byte[] { 0x41, 0x00, 0x42, 0x43 }, 3, "A\0B")]
	public void TryGetHostErrorText_cuts_back_to_a_utf8_sequence_start_or_at_the_bound_for_malformed_text(
		byte[] utf8, int bound, string expected)
	{
		string text = MemoryScanSession.DecodeBoundedUtf8(utf8, bound, out bool truncated);

		Assert.Equal(expected, text);
		Assert.True(truncated);
	}

	[Fact]
	[Trait("Qualification", "Q29")]
	public void TryGetHostErrorText_keeps_embedded_nul_and_non_ascii_text_exact()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using MemoryScanSession session = StartScanning(L);
		MemScanTestHost.Run(L, "scan_error_string = 'A\\0B — Échec de lecture'");

		Assert.True(session.TryGetHostErrorText(out string? text, out bool truncated));

		Assert.Equal("A\0B — Échec de lecture", text);
		Assert.False(truncated);
		Assert.Equal(0, L.Top);
	}

	[Theory]
	[Trait("Qualification", "Q29")]
	[InlineData("nil")]
	[InlineData("number")]
	[InlineData("raise")]
	public void TryGetHostErrorText_non_string_value_is_unreadable_and_keeps_the_session_state(string mode)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using MemoryScanSession session = StartScanning(L);
		MemScanTestHost.Run(L, "scan_error_mode = '" + mode + "'");

		bool read = session.TryGetHostErrorText(out string? text, out bool truncated);

		Assert.False(read);
		Assert.Null(text);
		Assert.False(truncated);
		Assert.Equal(MemoryScanState.Scanning, session.State);
		Assert.Equal(MemoryScanInvalidationReason.None, session.InvalidationReason);
		Assert.Equal("scan.ErrorString", MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q29")]
	public void TryGetHostErrorText_with_a_replaced_target_returns_false_without_a_scanner_call_and_invalidates()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		MemoryScanSession session = StartScanning(L);
		MemScanTestHost.Run(L, "scan_error_string = 'unread'; opened_process_id = " +
							   MemScanTestHost.FindOtherQualifiedProcessId().ToString(CultureInfo.InvariantCulture));

		bool read = session.TryGetHostErrorText(out string? text, out bool truncated);

		// Only the target check reached CE (getOpenedProcessID); the scanner's ErrorString was never read.
		Assert.False(read);
		Assert.Null(text);
		Assert.False(truncated);
		Assert.Equal(MemoryScanState.Invalidated, session.State);
		Assert.Equal(MemoryScanInvalidationReason.TargetChanged, session.InvalidationReason);
		Assert.Equal(string.Empty, MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
		session.Abandon();
	}

	[Fact]
	[Trait("Qualification", "Q29")]
	public void A_disposed_session_refuses_the_deadline_termination_and_error_text_members_without_a_CE_call()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		MemoryScanSession session = StartScanning(L);
		session.Dispose();
		MemScanTestHost.ClearTrace(L);

		Assert.Throws<ObjectDisposedException>(() => session.TryWaitForCompletion(Deadline));
		Assert.Throws<ObjectDisposedException>(() => session.TryTerminateScan(Deadline));
		Assert.Throws<ObjectDisposedException>(() => session.TryGetHostErrorText(out _, out _));

		Assert.Equal(MemoryScanState.Disposed, session.State);
		Assert.Equal(string.Empty, MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q26")]
	public void TryWaitForCompletion_disposed_from_inside_the_wait_releases_once_after_it_returned()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		MemoryScanSession session = StartScanning(L);
		MemoryScanReleaseOutcome? inner = null;
		using FakeHost.ManagedHookScope hook = FakeHost.InstallManagedHook(L, () => inner = session.ReleaseWithOutcome());
		InstallWaitHook(L);

		Assert.Throws<ObjectDisposedException>(() => session.TryWaitForCompletion(Deadline));

		Assert.Null(hook.Failure);
		Assert.Equal(default(MemoryScanReleaseOutcome), inner);
		Assert.Equal("scan.wait:250,hook.returned,list.destroy,scan.destroy", MemScanTestHost.ReadTrace(L));
		MemoryScanReleaseOutcome outcome = session.LastReleaseOutcome;
		Assert.Equal(MemoryScanState.Disposed, session.State);
		Assert.Equal(MemoryScanTerminationStatus.NotRequired, outcome.Termination);
		Assert.Equal(TargetReleaseStatus.Released, outcome.FoundList.Status);
		Assert.Equal(TargetReleaseStatus.Released, outcome.MemScan.Status);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q26")]
	public void TryWaitForCompletion_disposed_from_inside_a_timed_out_wait_stops_the_scan_once_after_it_returned()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		MemoryScanSession session = StartScanning(L);
		List<MemoryScanReleaseOutcome> inner = [];
		using FakeHost.ManagedHookScope hook = FakeHost.InstallManagedHook(L, () => inner.Add(session.ReleaseWithOutcome()));
		InstallWaitHook(L);
		MemScanTestHost.Run(L, "scan_wait_modes = { 'false', 'true' }");

		Assert.Throws<ObjectDisposedException>(() => session.TryWaitForCompletion(Deadline));

		// The deferred release stops the still-running scan once; its own settle wait runs the hook again, and that
		// second re-entrant release starts nothing either.
		Assert.Null(hook.Failure);
		Assert.Equal(2, hook.CallCount);
		Assert.Equal([default, default], inner);
		Assert.Equal(
			"scan.wait:250,hook.returned,scan.terminate:false,scan.wait:5000,hook.returned,list.destroy,scan.destroy",
			MemScanTestHost.ReadTrace(L));
		MemoryScanReleaseOutcome outcome = session.LastReleaseOutcome;
		Assert.Equal(MemoryScanTerminationStatus.Confirmed, outcome.Termination);
		Assert.Equal(TargetReleaseStatus.Released, outcome.FoundList.Status);
		Assert.Equal(TargetReleaseStatus.Released, outcome.MemScan.Status);
		Assert.Equal(0, L.Top);
	}

	[Theory]
	[Trait("Qualification", "Q26")]
	[InlineData("true", MemoryScanTerminationStatus.NotRequired)]
	[InlineData("false", MemoryScanTerminationStatus.WaitTimedOut)]
	public void TryTerminateScan_disposed_from_inside_the_settle_wait_releases_once_without_a_second_stop(
		string waitMode, MemoryScanTerminationStatus expected)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		MemoryScanSession session = StartScanning(L);
		using FakeHost.ManagedHookScope hook = FakeHost.InstallManagedHook(L, session.Dispose);
		InstallWaitHook(L);
		MemScanTestHost.Run(L, "scan_wait_mode = '" + waitMode + "'");

		Assert.Throws<ObjectDisposedException>(() => session.TryTerminateScan(Deadline));

		// A confirmed stop ended the scan (nothing left to stop); an unconfirmed one is reported, never repeated.
		Assert.Null(hook.Failure);
		Assert.Equal(1, hook.CallCount);
		Assert.Equal("scan.terminate:false,scan.wait:250,hook.returned,list.destroy,scan.destroy",
			MemScanTestHost.ReadTrace(L));
		MemoryScanReleaseOutcome outcome = session.LastReleaseOutcome;
		Assert.Equal(MemoryScanState.Disposed, session.State);
		Assert.Equal(expected, outcome.Termination);
		Assert.Equal(TargetReleaseStatus.Released, outcome.FoundList.Status);
		Assert.Equal(TargetReleaseStatus.Released, outcome.MemScan.Status);
		Assert.Equal(0, L.Top);
	}

	// Every wait runs the managed hook (CE pumping queued main-thread work), then records that the hook returned.
	private static void InstallWaitHook(LuaState state)
	{
		MemScanTestHost.Run(state, "scan_wait_hook = function() managed_hook(); table.insert(trace, 'hook.returned') end");
	}

	private static MemoryScanSession StartScanning(LuaState state)
	{
		MemoryScanSession session = MemScanTestHost.CreateSession(state);
		session.StartFirstScan(FirstScanRequest.ByteArray("90 90", new Address(0x1000), new Address(0x2000)));
		MemScanTestHost.ClearTrace(state);
		return session;
	}
}
