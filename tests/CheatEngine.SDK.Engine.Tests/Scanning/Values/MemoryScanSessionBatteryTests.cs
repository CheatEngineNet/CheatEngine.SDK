using System.Globalization;
using System.Text;

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
///     The rest of the chapter-13 qualification battery at fixture level (audit A13-20 to A13-33): target replacement
///     and process-identifier reuse during a wait, a found list already open, a missing saved result, a stale owner
///     after a runtime identity change, no progress or completion callback, error-text independence, a huge found list
///     read one page at a time, and a CE-reported scan error. These are C1 fixture contracts, not host qualification.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class MemoryScanSessionBatteryTests
{
	[Fact]
	[Trait("Qualification", "Q26")]
	[Trait("Qualification", "Q30.a")]
	public void Wait_after_the_target_was_replaced_invalidates_as_target_changed_without_initializing_results()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		MemoryScanSession session = StartScanning(L);
		MemScanTestHost.Run(L,
			"opened_process_id = " +
			MemScanTestHost.FindOtherQualifiedProcessId().ToString(CultureInfo.InvariantCulture));

		MemoryScanException failure = Assert.Throws<MemoryScanException>(session.WaitForCompletion);

		Assert.Equal(MemoryScanFailureKind.TargetIdentityMismatch, failure.FailureKind);
		Assert.Equal(MemoryScanState.Invalidated, session.State);
		Assert.Equal(MemoryScanInvalidationReason.TargetChanged, session.InvalidationReason);
		Assert.Equal(TargetIdentityCheckKind.TargetChanged, session.LastTargetCheck!.Value.Kind);
		Assert.Throws<MemoryScanStateException>(() => session.Results);
		Assert.Equal(string.Empty, MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
		session.Abandon();
	}

	[Fact]
	[Trait("Qualification", "Q26")]
	[Trait("Qualification", "Q30.a")]
	public void Wait_after_the_process_id_was_reused_invalidates_as_target_process_reused()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		MemoryScanSession session = StartScanning(L);
		TargetProcessIncarnation captured = session.TargetObservation.Incarnation!.Value;
		MemScanTestHost.ReplaceCapturedTarget(session,
			new TargetProcessIncarnation(captured.ProcessId, captured.StartedAtUtcTicks - 1));

		MemoryScanException failure = Assert.Throws<MemoryScanException>(session.WaitForCompletion);

		Assert.Equal(MemoryScanFailureKind.TargetIdentityMismatch, failure.FailureKind);
		Assert.Equal(MemoryScanInvalidationReason.TargetProcessReused, session.InvalidationReason);
		Assert.Equal(TargetIdentityCheckKind.ProcessReused, session.LastTargetCheck!.Value.Kind);
		Assert.Equal(string.Empty, MemScanTestHost.ReadTrace(L));
		MemoryScanReleaseOutcome release = session.ReleaseWithOutcome();
		Assert.Equal(TargetReleaseStatus.RefusedProcessReused, release.FoundList.Status);
		Assert.Equal(MemoryScanTerminationStatus.NotInvoked, release.Termination);
		Assert.Equal(string.Empty, MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q26")]
	public void Waiting_again_after_results_are_ready_is_refused_without_reinitializing_the_found_list()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using MemoryScanSession session = StartScanning(L);
		session.WaitForCompletion();
		MemScanTestHost.ClearTrace(L);

		MemoryScanStateException refused = Assert.Throws<MemoryScanStateException>(session.WaitForCompletion);

		Assert.Equal(MemoryScanState.ResultsReady, refused.State);
		Assert.Equal(MemoryScanState.ResultsReady, session.State);
		Assert.Equal(string.Empty, MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q26")]
	public void Next_scan_with_a_missing_saved_result_is_refused_and_invalidates_without_readable_results()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using MemoryScanSession session = StartScanning(L);
		session.WaitForCompletion();
		MemScanTestHost.Run(L, "scan_saved_results = { baseline = true }");
		MemScanTestHost.ClearTrace(L);
		NextScanRequest missing = new(ScanOption.ExactValue, RoundingType.Rounded, "90", string.Empty, false, false,
			false, false, false, "gone");

		MemoryScanException failure = Assert.Throws<MemoryScanException>(() => session.StartNextScan(missing));

		Assert.Equal(MemoryScanFailureKind.LuaError, failure.FailureKind);
		Assert.Equal("MemoryScan.NextScan", failure.Operation);
		Assert.Equal(MemoryScanState.Invalidated, session.State);
		Assert.Throws<MemoryScanStateException>(() => session.Results);
		Assert.Equal("list.deinitialize,scan.next:10", MemScanTestHost.ReadTrace(L));

		MemScanTestHost.ClearTrace(L);
		session.Reset();

		Assert.Equal(MemoryScanState.New, session.State);
		Assert.Equal("list.deinitialize,scan.new", MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	/// <remarks>
	///     A detach and re-attach changes the runtime's attach epoch, which the session compares. A generation-only reset
	///     (a new Lua state under the same attachment) is not reachable from this test project: <c>CheatEngine.SDK.Lua</c>
	///     grants no <c>InternalsVisibleTo</c> to it, so that path stays a C3 scenario.
	/// </remarks>
	[Fact]
	[Trait("Qualification", "Q26")]
	public void Runtime_identity_change_during_results_ready_refuses_the_owner_and_releases_without_CE_calls()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		MemScanTestHost.HostObjects objects = MemScanTestHost.Install(L);
		Assert.Equal(MemoryScanCreationStatus.Success,
			MemoryScanSessions.TryCreateWithOutcome(out MemoryScanSession? created).Status);
		MemoryScanSession session = Assert.IsType<MemoryScanSession>(created);
		session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
		session.WaitForCompletion();
		LuaRuntime.Detach();
		LuaRuntime.Attach(scope.Binding);
		MemScanTestHost.ClearTrace(L);

		MemoryScanMaterializationStatus copied = session.TryCopyResults(new MemoryScanResult[4], out _, out _);
		MemoryScanReleaseOutcome release = session.ReleaseWithOutcome();

		Assert.Equal(MemoryScanMaterializationStatus.RuntimeInvalidated, copied);
		Assert.Equal(MemoryScanInvalidationReason.RuntimeIdentityChanged, session.InvalidationReason);
		Assert.Equal(TargetReleaseStatus.NotInvoked, release.FoundList.Status);
		Assert.Equal(EngineFailureKind.BindingFailure, release.FoundList.FailureKind);
		Assert.Equal(TargetReleaseStatus.NotInvoked, release.MemScan.Status);
		Assert.Equal(MemoryScanTerminationStatus.NotRequired, release.Termination);
		Assert.False(FakeHost.IsDestroyed(L, objects.FoundList));
		Assert.False(FakeHost.IsDestroyed(L, objects.Scanner));
		Assert.Equal(string.Empty, MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q26")]
	public void Scan_sessions_never_install_progress_or_completion_callbacks()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		Assert.Equal(MemoryScanCreationStatus.Success,
			MemoryScanSessions.TryCreateWithOutcome(out MemoryScanSession? created).Status);
		MemoryScanSession session = Assert.IsType<MemoryScanSession>(created);

		session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
		session.WaitForCompletion();
		session.StartNextScan(NextScanRequest.ExactValue("90"));
		session.WaitForCompletion();
		session.Reset();
		session.StartFirstScan(FirstScanRequest.ByteArray("90", new Address(0x1000), new Address(0x2000)));
		session.Dispose();

		string trace = MemScanTestHost.ReadTrace(L);
		Assert.DoesNotContain("scan.set.", trace, StringComparison.Ordinal);
		Assert.DoesNotContain("setOnlyOneResult", trace, StringComparison.Ordinal);
		Assert.Equal(0L, MemScanTestHost.ReadInteger(L, "create_mem_scan_argument_count"));
		Assert.Equal(1L, MemScanTestHost.ReadInteger(L, "create_found_list_argument_count"));
		Assert.EndsWith("scan.terminate:false,scan.wait:5000,list.destroy,scan.destroy", trace,
			StringComparison.Ordinal);
		Assert.Equal(0, L.Top);
	}

	[Theory]
	[Trait("Qualification", "Q26")]
	[InlineData("'firstScan failed'")]
	[InlineData("'Échec de l’analyse — mémoire illisible'")]
	[InlineData("''")]
	[InlineData("{ code = 1 }")]
	[InlineData("nil")]
	[InlineData("42")]
	public void First_scan_failure_category_does_not_depend_on_the_lua_error_text(string payload)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using MemoryScanSession session = MemScanTestHost.CreateSession(L);
		EngineTest.Run(L, Encoding.UTF8.GetBytes("scan_first_raises = true; scan_first_error_payload = " + payload));

		MemoryScanException failure = Assert.Throws<MemoryScanException>(() =>
			session.StartFirstScan(FirstScanRequest.ByteArray("90", new Address(0x1000), new Address(0x2000))));

		Assert.Equal(MemoryScanFailureKind.LuaError, failure.FailureKind);
		Assert.Equal("MemoryScan.FirstScan", failure.Operation);
		Assert.Equal("The protected Lua call for memory scan operation 'MemoryScan.FirstScan' failed.",
			failure.Message);
		Assert.Equal(MemoryScanState.Invalidated, session.State);
		Assert.Equal(MemoryScanInvalidationReason.ProtectedLuaFailure, session.InvalidationReason);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q29")]
	public void TryCopyResultsPage_on_a_huge_found_list_reads_only_the_page_rows()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using MemoryScanSession session = HugeResults(L);
		MemoryScanResult[] page = new MemoryScanResult[4];

		MemoryScanMaterializationStatus status = session.TryCopyResultsPage(0, page, out ulong total, out int written);

		Assert.Equal(MemoryScanMaterializationStatus.Success, status);
		Assert.Equal(3_000_000_000UL, total);
		Assert.Equal(4, written);
		Assert.Equal(new MemoryScanResult(new Address(0x10003), "100"), page[3]);
		Assert.Equal(
			"results.getCount,results.getAddress:0,results.getValue:0,results.getAddress:1,results.getValue:1," +
			"results.getAddress:2,results.getValue:2,results.getAddress:3,results.getValue:3",
			MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q29")]
	public void TryCopyResultsPage_allocations_are_bounded_by_the_page_not_the_found_list()
	{
		const long Bound = 16 * 1024;
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using MemoryScanSession session = HugeResults(L);
		MemoryScanResult[] page = new MemoryScanResult[4];
		for (int warmUp = 0; warmUp < 8; warmUp++)
		{
			_ = session.TryCopyResultsPage(warmUp, page, out _, out _);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();
		MemoryScanMaterializationStatus status = session.TryCopyResultsPage(1_000_000, page, out _, out int written);
		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(MemoryScanMaterializationStatus.Success, status);
		Assert.Equal(4, written);
		Assert.True(allocated < Bound,
			$"Copying a four-row page of a three-billion-row found list allocated {allocated} bytes (bound {Bound}).");
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q26")]
	public void Unreadable_memory_reported_by_CE_is_a_bounded_host_error_fact()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using MemoryScanSession session = StartScanning(L);
		MemScanTestHost.Run(L, "scan_error_string = 'No readable memory found'");
		session.WaitForCompletion();
		MemoryScanResult retained = new(new Address(0xA11CE), "retained");
		MemoryScanResult[] destination = [retained];

		MemoryScanMaterializationStatus copied = session.TryCopyResults(destination, out ulong total, out int written);
		bool read = session.TryGetHostErrorText(out string? text, out bool truncated);

		Assert.Equal(MemoryScanMaterializationStatus.NoResults, copied);
		Assert.Equal(0UL, total);
		Assert.Equal(0, written);
		Assert.Equal(retained, destination[0]);
		Assert.True(read);
		Assert.Equal("No readable memory found", text);
		Assert.False(truncated);
		Assert.Equal(MemoryScanState.ResultsReady, session.State);
		Assert.Equal(0, L.Top);
	}

	private static MemoryScanSession StartScanning(LuaState state)
	{
		MemoryScanSession session = MemScanTestHost.CreateSession(state);
		session.StartFirstScan(FirstScanRequest.ExactValue(VariableType.Dword, "100"));
		MemScanTestHost.ClearTrace(state);
		return session;
	}

	private static MemoryScanSession HugeResults(LuaState state)
	{
		MemoryScanSession session = StartScanning(state);
		MemScanTestHost.Run(state, "found_count = 3000000000");
		session.WaitForCompletion();
		MemScanTestHost.ClearTrace(state);
		return session;
	}
}
