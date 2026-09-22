using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Scanning.Values;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Scanning;

/// <summary>
///     Contract tests for the stateful MemScan/FoundList slice. The fixture is a Lua model with the exact CE member
///     names and argument counts, not a live Cheat Engine process; CE 7.7 live ownership remains separately opt-in.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class MemoryScanSessionTests
{
	[Fact]
	public void First_scan_wait_and_read_follow_the_documented_CE_sequence()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		using MemoryScanSession session = CreateSession(scope.State);

		session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
		Assert.Equal(MemoryScanState.Scanning, session.State);
		session.WaitForCompletion();
		Assert.Equal(MemoryScanState.ResultsReady, session.State);
		Assert.Equal(2UL, session.ResultCount);

		Assert.True(session.TryGetAddress(0, out Address first));
		Assert.Equal(new Address(0x1234), first);
		Assert.True(session.TryGetAddress(1, out Address second));
		Assert.Equal(new Address(0xFFFF_FFFF_FFFF_FFFF), second);
		Assert.False(session.TryGetAddress(2, out _));
		Assert.True(session.TryGetValue(0, out string? value));
		Assert.Equal("100", value);
		Assert.Equal(
			"scan.first:14,scan.wait,list.initialize,results.getCount,results.getCount,results.getAddress:0,results.getCount,results.getAddress:1,results.getCount,results.getCount,results.getValue:0",
			ReadTrace(scope.State));
	}

	[Fact]
	public void Next_scan_is_rejected_until_a_first_scan_completed_and_omits_an_unspecified_optional_argument()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		using MemoryScanSession session = CreateSession(scope.State);

		MemoryScanStateException beforeFirst = Assert.Throws<MemoryScanStateException>(() =>
			session.StartNextScan(NextScanRequest.ExactValue("90")));
		Assert.Equal(MemoryScanState.New, beforeFirst.State);

		session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
		session.WaitForCompletion();
		EngineTest.Run(scope.State, "trace = {}"u8);

		session.StartNextScan(NextScanRequest.ExactValue("90"));

		Assert.Equal(MemoryScanState.Scanning, session.State);
		Assert.Equal("list.deinitialize,scan.next:9", ReadTrace(scope.State));
	}

	[Fact]
	public void Next_scan_passes_a_present_saved_result_name_as_the_tenth_argument()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		using MemoryScanSession session = CreateSession(scope.State);

		session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
		session.WaitForCompletion();
		EngineTest.Run(scope.State, "trace = {}"u8);

		session.StartNextScan(new NextScanRequest(
			ScanOption.ExactValue,
			RoundingType.Rounded,
			"90",
			string.Empty,
			false,
			false,
			false,
			false,
			false,
			"baseline"));

		Assert.Equal(MemoryScanState.Scanning, session.State);
		Assert.Equal("list.deinitialize,scan.next:10", ReadTrace(scope.State));
	}

	[Fact]
	public void Wait_error_invalidates_the_session_so_reset_can_recover()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		using MemoryScanSession session = CreateSession(scope.State, waitRaises: true);

		session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Grouped, "100"));

		MemoryScanException failure = Assert.Throws<MemoryScanException>(session.WaitForCompletion);

		Assert.Equal(MemoryScanFailureKind.LuaError, failure.FailureKind);
		Assert.Equal("MemoryScan.WaitForCompletion", failure.Operation);
		Assert.Equal(MemoryScanState.Invalidated, session.State);
		session.Reset();
		Assert.Equal(MemoryScanState.New, session.State);
		Assert.Equal("scan.first:14,scan.wait,list.deinitialize,scan.new", ReadTrace(scope.State));
	}

	[Fact]
	public void ReleaseWithOutcome_confirms_and_consumes_the_child_before_the_parent_without_a_retry()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		MemoryScanSession session = CreateSession(scope.State);

		session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
		session.WaitForCompletion();
		EngineTest.Run(scope.State, "trace = {}"u8);

		MemoryScanReleaseOutcome outcome = session.ReleaseWithOutcome();
		MemoryScanReleaseOutcome repeated = session.ReleaseWithOutcome();

		Assert.Equal(MemoryScanState.Disposed, session.State);
		Assert.True(outcome.OwnershipConsumed);
		Assert.Equal(TargetReleaseStatus.Released, outcome.FoundList.Status);
		Assert.Equal(TargetReleaseStatus.Released, outcome.MemScan.Status);
		Assert.Equal(outcome, repeated);
		Assert.Equal("list.deinitialize,list.destroy,scan.destroy", ReadTrace(scope.State));
		Assert.Throws<ObjectDisposedException>(() => _ = session.Scanner);
	}

	[Fact]
	public void ReleaseWithOutcome_reports_unknown_child_cleanup_and_still_attempts_the_parent_once()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		MemoryScanSession session = CreateSession(scope.State, foundListDestroyRaises: true);
		session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
		session.WaitForCompletion();
		CEObject scanner = session.Scanner.Handle;
		EngineTest.Run(scope.State, "trace = {}"u8);

		MemoryScanReleaseOutcome outcome = session.ReleaseWithOutcome();

		Assert.Equal(MemoryScanState.Disposed, session.State);
		Assert.True(outcome.OwnershipConsumed);
		Assert.Equal(TargetReleaseStatus.UnconfirmedAfterInvocation, outcome.FoundList.Status);
		Assert.Equal(EngineFailureKind.ProtectedLuaFailure, outcome.FoundList.FailureKind);
		Assert.Equal(TargetReleaseStatus.Released, outcome.MemScan.Status);
		Assert.True(FakeHost.IsDestroyed(scope.State, scanner));
		Assert.Equal("list.deinitialize,list.destroy,scan.destroy", ReadTrace(scope.State));
	}

	[Fact]
	public void Dispose_after_a_detach_consumes_ownership_without_throwing_or_using_the_prior_runtime()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		MemoryScanSession session = CreateSession(scope.State);

		session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
		session.WaitForCompletion();
		CEObject scanner = session.Scanner.Handle;
		CEObject foundList = session.Results.Handle;

		MemoryScanReleaseOutcome outcome;
		LuaRuntime.Detach();
		try
		{
			session.Dispose();
			outcome = session.LastReleaseOutcome;
			Assert.Equal(MemoryScanState.Disposed, session.State);
			Assert.True(outcome.OwnershipConsumed);
			Assert.Equal(TargetReleaseStatus.NotInvoked, outcome.FoundList.Status);
			Assert.Equal(TargetReleaseStatus.NotInvoked, outcome.MemScan.Status);
			Assert.False(FakeHost.IsDestroyed(scope.State, scanner));
			Assert.False(FakeHost.IsDestroyed(scope.State, foundList));
		}
		finally
		{
			LuaRuntime.Attach(scope.Binding);
		}

		Assert.Equal(MemoryScanState.Disposed, session.State);
		Assert.False(FakeHost.IsDestroyed(scope.State, foundList));
		Assert.False(FakeHost.IsDestroyed(scope.State, scanner));
	}

	[Fact]
	public void A_large_found_list_count_is_preserved_without_widening_row_indices()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		using MemoryScanSession session = CreateSession(scope.State, resultCountLiteral: "3000000000");

		session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
		session.WaitForCompletion();

		Assert.Equal(3_000_000_000UL, session.ResultCount);
		Assert.True(session.Results.TryGetCount(out ulong rawCount));
		Assert.Equal(3_000_000_000UL, rawCount);
		Assert.True(session.TryGetValue(0, out string? value));
		Assert.Equal("100", value);
	}

	[Fact]
	public void A_negative_found_list_count_is_rejected_by_raw_and_session_reads()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		using MemoryScanSession session = CreateSession(scope.State, resultCountLiteral: "-1");

		session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
		session.WaitForCompletion();

		Assert.False(session.Results.TryGetCount(out ulong rawCount));
		Assert.Equal(0UL, rawCount);
		MemoryScanException failure = Assert.Throws<MemoryScanException>(() => _ = session.ResultCount);
		Assert.Equal(MemoryScanFailureKind.UnexpectedResult, failure.FailureKind);
		Assert.Equal("MemoryScan.ResultCount", failure.Operation);
	}

	[Fact]
	public void An_empty_found_list_does_not_call_address_or_value_methods_out_of_range()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		using MemoryScanSession session = CreateSession(scope.State, resultCountLiteral: "0");

		session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
		session.WaitForCompletion();

		Assert.Equal(0UL, session.ResultCount);
		Assert.True(session.Results.TryGetCount(out ulong rawCount));
		Assert.Equal(0UL, rawCount);
		EngineTest.Run(scope.State, "trace = {}"u8);

		Assert.False(session.TryGetAddress(0, out Address address));
		Assert.Equal(default, address);
		Assert.False(session.TryGetValue(0, out string? value));
		Assert.Null(value);
		Assert.Equal("results.getCount,results.getCount", ReadTrace(scope.State));
	}

	[Fact]
	public void Wait_for_completion_requests_zero_Lua_results_from_wait_till_done()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		using MemoryScanSession session = CreateSession(scope.State);

		session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
		using FakeHost.PCallProbe probe =
			FakeHost.ReplaceWaitTillDoneWithPCallProbe(scope.State, session.Scanner.Handle);

		session.WaitForCompletion();

		Assert.Equal(MemoryScanState.ResultsReady, session.State);
		Assert.Equal(1, probe.WaitCallCount);
		Assert.Equal(0, probe.WaitArgumentCount);
		Assert.Equal(0, probe.WaitResultCount);
	}

	[Fact]
	public void Failed_protected_scan_invalidates_the_session_until_reset_succeeds()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		using MemoryScanSession session = CreateSession(scope.State, true);

		MemoryScanException failure = Assert.Throws<MemoryScanException>(() =>
			session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100")));

		Assert.Equal(MemoryScanFailureKind.LuaError, failure.FailureKind);
		Assert.Equal("MemoryScan.FirstScan", failure.Operation);
		Assert.DoesNotContain("first scan rejected", failure.Message, StringComparison.Ordinal);
		Assert.IsType<LuaException>(failure.InnerException);
		Assert.Equal(MemoryScanState.Invalidated, session.State);
		Assert.Throws<MemoryScanStateException>(() =>
			session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100")));

		session.Reset();

		Assert.Equal(MemoryScanState.New, session.State);
		Assert.Equal("scan.first:14,list.deinitialize,scan.new", ReadTrace(scope.State));
	}

	[Fact]
	public void A_main_thread_only_scan_operation_is_rejected_before_it_touches_CE()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		using MemoryScanSession session = CreateSession(scope.State);

		Exception? failure = EngineTest.RunOnWorker(() =>
			session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100")));

		InvalidOperationException exception = Assert.IsType<InvalidOperationException>(failure);
		Assert.Contains("main thread", exception.Message, StringComparison.Ordinal);
		Assert.Equal(MemoryScanState.New, session.State);
		Assert.Equal(string.Empty, ReadTrace(scope.State));
	}

	[Fact]
	public void Disposal_on_a_worker_never_throws_or_retries_unsafe_child_or_parent_cleanup()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		MemoryScanSession session = CreateSession(scope.State);

		session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
		session.WaitForCompletion();
		EngineTest.Run(scope.State, "trace = {}"u8);

		Exception? failure = EngineTest.RunOnWorker(session.Dispose);

		Assert.Null(failure);
		Assert.Equal(MemoryScanState.Disposed, session.State);
		Assert.True(session.LastReleaseOutcome.OwnershipConsumed);
		Assert.Equal(TargetReleaseStatus.NotInvoked, session.LastReleaseOutcome.FoundList.Status);
		Assert.Equal(TargetReleaseStatus.NotInvoked, session.LastReleaseOutcome.MemScan.Status);
		Assert.Equal(string.Empty, ReadTrace(scope.State));
	}

	[Fact]
	public void Adopt_transfers_the_source_owners_and_keeps_borrowed_handle_identity()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		InstallCurrentTarget(scope.State);
		CEObject scan = FakeHost.CreateObject(scope.State, "Object",
			ScanInitializer(false, false));
		CEObject foundList = FakeHost.CreateObject(scope.State, "Object", FoundListInitializer());
		Owned<MemScan> scanOwner = new(MemScan.FromHandle(scan));
		Owned<FoundList> foundListOwner = new(FoundList.FromHandle(foundList));

		using MemoryScanSession session = MemoryScanSession.Adopt(scanOwner, foundListOwner);

		Assert.True(scanOwner.IsDisposed);
		Assert.True(foundListOwner.IsDisposed);
		Assert.Equal(scan, session.Scanner.Handle);
	}

	[Fact]
	public void Adopt_when_session_publication_fails_keeps_the_source_owners_for_child_before_parent_cleanup()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		EngineTest.Run(scope.State, "trace = {}"u8);
		CEObject scan = FakeHost.CreateObject(scope.State, "Object", ScanInitializer(false, false));
		CEObject foundList = FakeHost.CreateObject(scope.State, "Object", FoundListInitializer());
		Owned<MemScan> scanOwner = new(MemScan.FromHandle(scan));
		Owned<FoundList> foundListOwner = new(FoundList.FromHandle(foundList));

		InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
			MemoryScanSession.AdoptCore(scanOwner,
				foundListOwner,
				static (_, _) => throw new InvalidOperationException("injected session publication failure")));

		Assert.Equal("injected session publication failure", exception.Message);
		Assert.False(scanOwner.IsDisposed);
		Assert.False(foundListOwner.IsDisposed);
		foundListOwner.Dispose();
		scanOwner.Dispose();
		Assert.Equal("list.destroy,scan.destroy", ReadTrace(scope.State));
	}

	[Fact]
	public void A_non_hexadecimal_address_text_is_a_stable_unexpected_host_result()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		using MemoryScanSession session = CreateSession(scope.State, invalidAddress: true);

		session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
		session.WaitForCompletion();

		MemoryScanException failure = Assert.Throws<MemoryScanException>(() => session.TryGetAddress(0, out _));

		Assert.Equal(MemoryScanFailureKind.UnexpectedResult, failure.FailureKind);
		Assert.Equal("MemoryScan.ResultAddress", failure.Operation);
	}

	[Fact]
	public void TryCopyResults_refuses_an_insufficient_destination_before_reading_any_row()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		using MemoryScanSession session = CreateSession(scope.State);
		session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
		session.WaitForCompletion();
		EngineTest.Run(scope.State, "trace = {}"u8);
		MemoryScanResult retained = new(new Address(0xA11CE), "retained");
		MemoryScanResult[] destination = [retained];

		MemoryScanMaterializationStatus status =
			session.TryCopyResults(destination, out ulong totalCount, out int written);

		Assert.Equal(MemoryScanMaterializationStatus.DestinationTooSmall, status);
		Assert.Equal(2UL, totalCount);
		Assert.Equal(0, written);
		Assert.Equal(retained, destination[0]);
		Assert.Equal("results.getCount", ReadTrace(scope.State));
	}

	[Fact]
	public void TryCopyResults_creates_a_complete_non_streaming_snapshot_within_the_caller_bound()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		using MemoryScanSession session = CreateSession(scope.State);
		session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
		session.WaitForCompletion();
		EngineTest.Run(scope.State, "trace = {}"u8);
		MemoryScanResult[] destination = new MemoryScanResult[2];

		MemoryScanMaterializationStatus status =
			session.TryCopyResults(destination, out ulong totalCount, out int written);

		Assert.Equal(MemoryScanMaterializationStatus.Success, status);
		Assert.Equal(2UL, totalCount);
		Assert.Equal(2, written);
		Assert.Equal(new MemoryScanResult(new Address(0x1234), "100"), destination[0]);
		Assert.Equal(new MemoryScanResult(new Address(0xFFFF_FFFF_FFFF_FFFF), "100"), destination[1]);
		Assert.Equal("results.getCount,results.getAddress:0,results.getValue:0,results.getAddress:1,results.getValue:1",
			ReadTrace(scope.State));
	}

	[Fact]
	public void TryCopyResults_keeps_an_empty_found_list_distinct_from_an_invalid_result()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		using MemoryScanSession session = CreateSession(scope.State, resultCountLiteral: "0");
		session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
		session.WaitForCompletion();
		EngineTest.Run(scope.State, "trace = {}"u8);
		MemoryScanResult retained = new(new Address(0xA11CE), "retained");
		MemoryScanResult[] destination = [retained];

		MemoryScanMaterializationStatus status =
			session.TryCopyResults(destination, out ulong totalCount, out int written);

		Assert.Equal(MemoryScanMaterializationStatus.NoResults, status);
		Assert.Equal(0UL, totalCount);
		Assert.Equal(0, written);
		Assert.Equal(retained, destination[0]);
		Assert.Equal("results.getCount", ReadTrace(scope.State));
	}

	[Fact]
	public void TryCopyResults_reports_a_malformed_row_without_publishing_a_partial_snapshot()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		using MemoryScanSession session = CreateSession(scope.State, invalidAddress: true);
		session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
		session.WaitForCompletion();
		EngineTest.Run(scope.State, "trace = {}"u8);
		MemoryScanResult retained = new(new Address(0xA11CE), "retained");
		MemoryScanResult[] destination = [retained, retained];

		MemoryScanMaterializationStatus status =
			session.TryCopyResults(destination, out ulong totalCount, out int written);

		Assert.Equal(MemoryScanMaterializationStatus.InvalidResult, status);
		Assert.Equal(2UL, totalCount);
		Assert.Equal(0, written);
		Assert.Equal(retained, destination[0]);
		Assert.Equal(retained, destination[1]);
		Assert.Equal("results.getCount,results.getAddress:0", ReadTrace(scope.State));
	}

	[Fact]
	[SuppressMessage("xUnit.Analyzers", "xUnit1051",
		Justification = "The fixture must start with a deliberately cancelled token to prove no Lua row call begins.")]
	public void TryCopyResults_reports_a_preexisting_cancellation_without_reading_or_publishing_rows()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		using MemoryScanSession session = CreateSession(scope.State);
		session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
		session.WaitForCompletion();
		EngineTest.Run(scope.State, "trace = {}"u8);
		MemoryScanResult retained = new(new Address(0xA11CE), "retained");
		MemoryScanResult[] destination = [retained, retained];
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();

		MemoryScanMaterializationStatus status = CopyWithCancellation(session, destination, cancellation.Token,
			out ulong totalCount, out int written);

		Assert.Equal(MemoryScanMaterializationStatus.Cancelled, status);
		Assert.Equal(0UL, totalCount);
		Assert.Equal(0, written);
		Assert.Equal(retained, destination[0]);
		Assert.Equal(retained, destination[1]);
		Assert.Equal(MemoryScanCancellationMilestone.CancelledBeforeNativeCall, session.LastCancellationMilestone);
		Assert.Equal(string.Empty, ReadTrace(scope.State));
	}

	[Fact]
	public void TryCopyResults_cancels_after_get_address_without_reading_a_value_or_publishing_a_snapshot()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		using MemoryScanSession session = CreateSession(scope.State);
		session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
		session.WaitForCompletion();
		EngineTest.Run(scope.State, "trace = {}"u8);
		MemoryScanResult retained = new(new Address(0xA11CE), "retained");
		MemoryScanResult[] destination = [retained, retained];
		using CancellationTokenSource cancellation = new();
		using FakeHost.PCallProbe probe = FakeHost.ReplaceFoundListGetAddressWithPCallProbe(scope.State,
			session.Results.Handle,
			cancellation.Cancel);

		MemoryScanMaterializationStatus status = CopyWithCancellation(session, destination, cancellation.Token,
			out ulong totalCount, out int written);

		Assert.Equal(MemoryScanMaterializationStatus.Cancelled, status);
		Assert.Equal(2UL, totalCount);
		Assert.Equal(0, written);
		Assert.Equal(retained, destination[0]);
		Assert.Equal(retained, destination[1]);
		Assert.Equal(1, probe.GetAddressCallCount);
		Assert.Equal(MemoryScanCancellationMilestone.ObservedAfterNativeCall, session.LastCancellationMilestone);
		Assert.Equal("results.getCount", ReadTrace(scope.State));
	}

	[Fact]
	public void TryCopyResultsPage_copies_only_the_requested_bounded_page_without_materializing_the_full_result_set()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		using MemoryScanSession session = CreateSession(scope.State, resultCountLiteral: "3");
		session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
		session.WaitForCompletion();
		EngineTest.Run(scope.State, "trace = {}"u8);
		MemoryScanResult[] destination = new MemoryScanResult[1];

		MemoryScanMaterializationStatus status = session.TryCopyResultsPage(1, destination, out ulong totalCount,
			out int written);

		Assert.Equal(MemoryScanMaterializationStatus.Success, status);
		Assert.Equal(3UL, totalCount);
		Assert.Equal(1, written);
		Assert.Equal(new MemoryScanResult(new Address(0xFFFF_FFFF_FFFF_FFFF), "100"), destination[0]);
		Assert.Equal("results.getCount,results.getAddress:1,results.getValue:1", ReadTrace(scope.State));
	}

	[Fact]
	public void TryCopyResultsPage_does_not_publish_a_prefix_when_a_page_row_is_invalid()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		using MemoryScanSession session = CreateSession(scope.State, invalidAddress: true);
		session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
		session.WaitForCompletion();
		EngineTest.Run(scope.State, "trace = {}"u8);
		MemoryScanResult retained = new(new Address(0xA11CE), "retained");
		MemoryScanResult[] destination = [retained];

		MemoryScanMaterializationStatus status = session.TryCopyResultsPage(0, destination, out ulong totalCount,
			out int written);

		Assert.Equal(MemoryScanMaterializationStatus.InvalidResult, status);
		Assert.Equal(2UL, totalCount);
		Assert.Equal(0, written);
		Assert.Equal(retained, destination[0]);
		Assert.Equal("results.getCount,results.getAddress:0", ReadTrace(scope.State));
	}

	[Fact]
	public void TryCopyResultsPage_reports_no_results_without_touching_the_destination()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		using MemoryScanSession session = CreateSession(scope.State, resultCountLiteral: "0");
		session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
		session.WaitForCompletion();
		EngineTest.Run(scope.State, "trace = {}"u8);
		MemoryScanResult retained = new(new Address(0xA11CE), "retained");
		MemoryScanResult[] destination = [retained];

		MemoryScanMaterializationStatus status = session.TryCopyResultsPage(0, destination, out ulong totalCount,
			out int written);

		Assert.Equal(MemoryScanMaterializationStatus.NoResults, status);
		Assert.Equal(0UL, totalCount);
		Assert.Equal(0, written);
		Assert.Equal(retained, destination[0]);
		Assert.Equal("results.getCount", ReadTrace(scope.State));
	}

	[Fact]
	public void TryCopyResultsPage_rejects_an_exact_end_start_without_reading_a_row()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		using MemoryScanSession session = CreateSession(scope.State);
		session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
		session.WaitForCompletion();
		EngineTest.Run(scope.State, "trace = {}"u8);
		MemoryScanResult retained = new(new Address(0xA11CE), "retained");
		MemoryScanResult[] destination = [retained];

		MemoryScanMaterializationStatus status = session.TryCopyResultsPage(2, destination, out ulong totalCount,
			out int written);

		Assert.Equal(MemoryScanMaterializationStatus.PageStartOutOfRange, status);
		Assert.Equal(2UL, totalCount);
		Assert.Equal(0, written);
		Assert.Equal(retained, destination[0]);
		Assert.Equal("results.getCount", ReadTrace(scope.State));
	}

	[Fact]
	public void TryCopyResultsPage_rejects_zero_capacity_without_reading_a_row()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		using MemoryScanSession session = CreateSession(scope.State);
		session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
		session.WaitForCompletion();
		EngineTest.Run(scope.State, "trace = {}"u8);

		MemoryScanMaterializationStatus status = session.TryCopyResultsPage(0, [], out ulong totalCount,
			out int written);

		Assert.Equal(MemoryScanMaterializationStatus.DestinationTooSmall, status);
		Assert.Equal(2UL, totalCount);
		Assert.Equal(0, written);
		Assert.Equal("results.getCount", ReadTrace(scope.State));
	}

	[Fact]
	public void TryCopyResultsPage_rejects_a_negative_start_before_reading_the_count()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		using MemoryScanSession session = CreateSession(scope.State);
		session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
		session.WaitForCompletion();
		EngineTest.Run(scope.State, "trace = {}"u8);
		MemoryScanResult retained = new(new Address(0xA11CE), "retained");
		MemoryScanResult[] destination = [retained];

		Assert.Throws<ArgumentOutOfRangeException>(() =>
		{
			session.TryCopyResultsPage(-1, destination, out _, out _);
		});

		Assert.Equal(retained, destination[0]);
		Assert.Equal(string.Empty, ReadTrace(scope.State));
	}

	[Fact]
	[SuppressMessage("xUnit.Analyzers", "xUnit1051",
		Justification = "The fixture starts with a deliberately cancelled token to prove no paged-row call begins.")]
	public void TryCopyResultsPage_does_not_publish_a_prefix_when_cancellation_precedes_the_page()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		using MemoryScanSession session = CreateSession(scope.State);
		session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
		session.WaitForCompletion();
		EngineTest.Run(scope.State, "trace = {}"u8);
		MemoryScanResult retained = new(new Address(0xA11CE), "retained");
		MemoryScanResult[] destination = [retained];
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();

		MemoryScanMaterializationStatus status = session.TryCopyResultsPageCancellable(0, destination,
			out ulong totalCount, out int written, cancellation.Token);

		Assert.Equal(MemoryScanMaterializationStatus.Cancelled, status);
		Assert.Equal(0UL, totalCount);
		Assert.Equal(0, written);
		Assert.Equal(retained, destination[0]);
		Assert.Equal(MemoryScanCancellationMilestone.CancelledBeforeNativeCall, session.LastCancellationMilestone);
		Assert.Equal(string.Empty, ReadTrace(scope.State));
	}

	[Fact]
	[SuppressMessage("xUnit.Analyzers", "xUnit1051",
		Justification = "The fixture must start with a deliberately cancelled token to prove no CE scan call begins.")]
	public void A_cancellable_first_scan_honors_preexisting_cancellation_without_claiming_to_interrupt_CE()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		using MemoryScanSession session = CreateSession(scope.State);
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();

		Assert.Throws<OperationCanceledException>(() => StartFirstWithCancellation(session,
			FirstScanRequest.ExactValue(VariableType.Dword, "100"), cancellation.Token));

		Assert.Equal(MemoryScanState.New, session.State);
		Assert.Equal(MemoryScanCancellationMilestone.CancelledBeforeNativeCall, session.LastCancellationMilestone);
		Assert.Equal(string.Empty, ReadTrace(scope.State));
	}

	[Fact]
	public void A_cancellable_wait_records_post_call_cancellation_without_falsely_claiming_to_interrupt_CE()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		using MemoryScanSession session = CreateSession(scope.State);
		session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
		using CancellationTokenSource cancellation = new();
		using FakeHost.PCallProbe probe = FakeHost.ReplaceWaitTillDoneWithPCallProbe(scope.State,
			session.Scanner.Handle,
			cancellation.Cancel);

		WaitWithCancellation(session, cancellation.Token);

		Assert.Equal(1, probe.WaitCallCount);
		Assert.Equal(MemoryScanState.ResultsReady, session.State);
		Assert.Equal(MemoryScanCancellationMilestone.ObservedAfterNativeCall, session.LastCancellationMilestone);
	}

	[Fact]
	public void A_session_refuses_scan_work_when_the_original_target_is_no_longer_selected()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		MemoryScanSession session = CreateSession(scope.State);
		EngineTest.Run(scope.State, "opened_process_id = 0"u8);
		EngineTest.Run(scope.State, "trace = {}"u8);

		MemoryScanException failure = Assert.Throws<MemoryScanException>(() =>
			session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100")));

		Assert.Equal(MemoryScanFailureKind.TargetIdentityUnavailable, failure.FailureKind);
		Assert.Equal(MemoryScanState.New, session.State);
		Assert.Equal(TargetIdentityCheckKind.NoTargetSelected, session.LastTargetCheck!.Value.Kind);
		Assert.Equal(MemoryScanInvalidationReason.None, session.InvalidationReason);
		Assert.Equal(string.Empty, ReadTrace(scope.State));
		session.Abandon();
	}

	[Fact]
	public void ReleaseWithOutcome_refuses_cleanup_on_a_replaced_target_and_consumes_both_owners()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		MemoryScanSession session = CreateSession(scope.State);
		EngineTest.Run(scope.State, "opened_process_id = 0; trace = {}"u8);

		MemoryScanReleaseOutcome outcome = session.ReleaseWithOutcome();

		Assert.Equal(MemoryScanState.Disposed, session.State);
		Assert.True(outcome.OwnershipConsumed);
		Assert.Equal(TargetReleaseStatus.RefusedNoTarget, outcome.FoundList.Status);
		Assert.Equal(TargetReleaseStatus.RefusedNoTarget, outcome.MemScan.Status);
		Assert.Equal(TargetIdentityCheckKind.NoTargetSelected, outcome.FoundList.TargetCheck!.Value.Kind);
		Assert.Equal(string.Empty, ReadTrace(scope.State));
	}

	private static MemoryScanMaterializationStatus CopyWithCancellation(MemoryScanSession session,
		Span<MemoryScanResult> destination, CancellationToken cancellationToken, out ulong totalCount, out int written)
	{
		return session.TryCopyResultsCancellable(destination, out totalCount, out written, cancellationToken);
	}

	private static void StartFirstWithCancellation(MemoryScanSession session, in FirstScanRequest request,
		CancellationToken cancellationToken)
	{
		session.StartFirstScanCancellable(in request, cancellationToken);
	}

	private static void WaitWithCancellation(MemoryScanSession session, CancellationToken cancellationToken)
	{
		session.WaitForCompletionCancellable(cancellationToken);
	}

	private static MemoryScanSession CreateSession(LuaState state, bool firstScanRaises = false,
		bool invalidAddress = false, bool waitRaises = false, string resultCountLiteral = "2",
		bool foundListDestroyRaises = false)
	{
		EngineTest.Run(state, "trace = {}"u8);
		InstallCurrentTarget(state);
		CEObject scan = FakeHost.CreateObject(state, "Object",
			ScanInitializer(firstScanRaises, waitRaises));
		CEObject foundList =
			FakeHost.CreateObject(state, "Object", FoundListInitializer(invalidAddress, resultCountLiteral,
				foundListDestroyRaises));
		return MemoryScanSession.Adopt(
			new Owned<MemScan>(MemScan.FromHandle(scan)),
			new Owned<FoundList>(FoundList.FromHandle(foundList)));
	}

	private static void InstallCurrentTarget(LuaState state)
	{
		EngineTest.Run(state, Encoding.UTF8.GetBytes("opened_process_id = " +
													 Environment.ProcessId.ToString(CultureInfo.InvariantCulture) +
													 "; function getOpenedProcessID() return opened_process_id end"));
	}

	private static string ScanInitializer(bool firstScanRaises, bool waitRaises)
	{
		string raiseFirstScan = firstScanRaises ? "; error('first scan rejected')" : string.Empty;
		string raiseWait = waitRaises ? "; error('wait rejected')" : string.Empty;
		return
			"o.props.firstScan = function(...) local n = select('#', ...); if n ~= 14 then error('firstScan argument count') end; local scanoption, vartype, roundingtype, input1, input2, startAddress, stopAddress, protectionflags, alignmenttype, alignmentparam, hexadecimal, nonbinary, unicode, casesensitive = ...; if scanoption ~= 1 or (vartype ~= 2 and vartype ~= 14) or roundingtype ~= 0 or input1 ~= '100' or input2 ~= '' or startAddress ~= 0 or stopAddress ~= -1 or protectionflags ~= '' or alignmenttype ~= 0 or alignmentparam ~= '' or hexadecimal ~= false or nonbinary ~= false or unicode ~= false or casesensitive ~= false then error('firstScan argument values') end; table.insert(trace, 'scan.first:' .. n)" +
			raiseFirstScan + " end\n" +
			"o.props.nextScan = function(...) local n = select('#', ...); if n ~= 9 and n ~= 10 then error('nextScan argument count') end; local scanoption, roundingtype, input1, input2, hexadecimal, nonbinary, unicode, casesensitive, percentage, savedresultname = ...; if scanoption ~= 1 or roundingtype ~= 0 or input1 ~= '90' or input2 ~= '' or hexadecimal ~= false or nonbinary ~= false or unicode ~= false or casesensitive ~= false or percentage ~= false or (n == 9 and savedresultname ~= nil) or (n == 10 and savedresultname ~= 'baseline') then error('nextScan argument values') end; table.insert(trace, 'scan.next:' .. n) end\n" +
			"o.props.waitTillDone = function() table.insert(trace, 'scan.wait')" + raiseWait + " end\n" +
			"o.props.newScan = function() table.insert(trace, 'scan.new') end\n" +
			"o.getters.destroy = function(o) return function() o.destroyed = true; table.insert(trace, 'scan.destroy') end end";
	}

	private static string FoundListInitializer(bool invalidAddress = false, string resultCountLiteral = "2",
		bool destroyRaises = false)
	{
		string firstAddress = invalidAddress ? "'not-an-address'" : "'00001234'";
		string destroyFailure = destroyRaises ? "; error('found-list destroy rejected')" : string.Empty;
		return "o.props.initialize = function() table.insert(trace, 'list.initialize') end\n" +
			   "o.props.deinitialize = function() table.insert(trace, 'list.deinitialize') end\n" +
			   "o.props.Count = " + resultCountLiteral + "\n" +
			   "o.props.getCount = function() table.insert(trace, 'results.getCount'); return o.props.Count end\n" +
			   "o.props.getAddress = function(index) table.insert(trace, 'results.getAddress:' .. index); if index == 0 then return " +
			   firstAddress + " end; return 'FFFFFFFFFFFFFFFF' end\n" +
			   "o.props.getValue = function(index) table.insert(trace, 'results.getValue:' .. index); return '100' end\n" +
			   "o.getters.destroy = function(o) return function() o.destroyed = true; table.insert(trace, 'list.destroy')" +
			   destroyFailure + " end end";
	}

	private static string ReadTrace(LuaState state)
	{
		using LuaFrame frame = new(state);
		EngineTest.Run(state, "return table.concat(trace, ',')"u8, 1);
		return EngineTest.ReadString(state, -1);
	}
}
