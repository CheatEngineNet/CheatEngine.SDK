using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Scanning.Aob;
using CheatEngine.SDK.Engine.Scanning.Values;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

// The deadline overload is gated by CESDK5010 on purpose; its tests opt in for this file only.
#pragma warning disable CESDK5010

namespace CheatEngine.SDK.Engine.Tests.Scanning.Aob;

/// <summary>
///     The bounded, exhaustive AOB route over a MemScan session (audit F07, A13-07, A13-09, Q28, Q29; spike D4): CE's work
///     is limited to <c>[Start, Stop)</c>, <c>OnlyOneResult</c> is always off, the start is post-filtered, only addresses
///     are read, and the session is released once on every path. The CE call accounting in these tests is the C1 cost
///     proxy of F07; host timings are a C3 matter.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class AobBoundedScanTests
{
	private const string Pattern = "55 48 89 E5";

	private static readonly AobScanBounds ModuleBounds = Bounds(0x1_0000_0000, 0x1_0036_7000);

	[Fact]
	[Trait("Qualification", "Q28")]
	public void TryScanWithinBounds_three_host_rows_two_in_bounds_follow_the_exact_CE_call_sequence()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		SetAddresses(L, "100000000", "100010BC0", "7FFC7A0A0000");
		Address[] destination = new Address[4];

		AobBoundedScanResult result = Scan(ModuleBounds, destination);

		Assert.Equal(AobBoundedScanOutcomeKind.Matches, result.Kind);
		Assert.True(result.IsSuccess);
		Assert.Equal(2, result.Written);
		Assert.Equal([new Address(0x1_0000_0000), new Address(0x1_0001_0BC0)], destination[..2]);
		Assert.Equal(3UL, result.HostResultCount);
		Assert.Equal(3UL, result.RowsRead);
		Assert.Equal(0UL, result.UnreadHostRows);
		Assert.Equal(1UL, result.AtOrAfterStopSkipped);
		Assert.True(result.InBoundsCountIsExact);
		Assert.Equal(
			"factory.scan,factory.list,scan.setOnlyOneResult:false,scan.first:14,scan.wait,list.initialize," +
			"results.getCount,scan.ErrorString,results.getAddress:0,results.getAddress:1,results.getAddress:2," +
			"list.deinitialize,list.destroy,scan.destroy",
			MemScanTestHost.ReadTrace(L));
		Assert.Equal(MemoryScanTerminationStatus.NotRequired, result.Termination);
		Assert.Equal(MemoryScanTerminationStatus.NotRequired, result.Release.Termination);
		Assert.Equal(TargetReleaseStatus.Released, result.Release.FoundList.Status);
		Assert.Equal(TargetReleaseStatus.Released, result.Release.MemScan.Status);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	public void TryScanWithinBounds_refuses_invalid_bounds_before_any_CE_call()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		MemScanTestHost.ClearTrace(L);
		Address[] destination = [new(0xA11CE)];

		AobBoundedScanResult result = Scan(default, destination);

		Assert.Equal(AobBoundedScanOutcomeKind.InvalidBounds, result.Kind);
		Assert.False(result.IsSuccess);
		Assert.Equal(MemoryScanCreationStatus.Unknown, result.Creation.Status);
		Assert.Equal(default, result.Release);
		Assert.Equal(new Address(0xA11CE), destination[0]);
		Assert.Equal(string.Empty, MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	public void TryScanWithinBounds_rejects_a_null_pattern_and_an_empty_destination_before_any_CE_call()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		MemScanTestHost.ClearTrace(L);

		Assert.Throws<ArgumentNullException>(() => AobScanner.TryScanWithinBounds(null!, ModuleBounds,
			AobScanOptions.Default, new Address[1], TestContext.Current.CancellationToken));
		Assert.Throws<ArgumentException>(() => AobScanner.TryScanWithinBounds(Pattern, ModuleBounds,
			AobScanOptions.Default, Span<Address>.Empty, TestContext.Current.CancellationToken));
		Assert.Throws<ArgumentOutOfRangeException>(() => AobScanner.TryScanWithinBounds(Pattern, ModuleBounds,
			AobScanOptions.Default, TimeSpan.Zero, new Address[1], TestContext.Current.CancellationToken));

		Assert.Equal(string.Empty, MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	public void
		TryScanWithinBounds_passes_the_bounds_verbatim_as_integers_and_disables_only_one_result_before_the_first_scan()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);

		AobBoundedScanResult result = Scan(Bounds(0x1_0000_0000, 0x8000_0000_0000_0000), new Address[1]);

		Assert.Equal(AobBoundedScanOutcomeKind.NoMatches, result.Kind);
		Assert.StartsWith("factory.scan,factory.list,scan.setOnlyOneResult:false,scan.first:14,",
			MemScanTestHost.ReadTrace(L), StringComparison.Ordinal);
		MemScanTestHost.AssertLua(L, "set_only_one_argument_count == 1");
		MemScanTestHost.AssertLua(L, "first_scan_args[1] == 1 and first_scan_args[2] == 8 and first_scan_args[3] == 0");
		MemScanTestHost.AssertLua(L, "first_scan_args[4] == '55 48 89 E5' and first_scan_args[5] == ''");
		MemScanTestHost.AssertLua(L, "first_scan_start_type == 'integer' and first_scan_args[6] == 0x100000000");
		MemScanTestHost.AssertLua(L, "first_scan_stop_type == 'integer' and first_scan_args[7] == math.mininteger");
		MemScanTestHost.AssertLua(L,
			"first_scan_args[8] == '' and first_scan_args[9] == 0 and first_scan_args[10] == ''");
		MemScanTestHost.AssertLua(L, "first_scan_args[11] == true and first_scan_args[12] == false");
		MemScanTestHost.AssertLua(L, "first_scan_args[13] == false and first_scan_args[14] == false");
		Assert.Equal(0L, MemScanTestHost.ReadInteger(L, "create_mem_scan_argument_count"));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	public void TryScanWithinBounds_passes_explicit_protection_and_alignment_options_verbatim()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);

		_ = AobScanner.TryScanWithinBounds(Pattern, ModuleBounds,
			new AobScanOptions("+X-C-W", FastScanMethod.Aligned, "4"),
			new Address[1], TestContext.Current.CancellationToken);

		MemScanTestHost.AssertLua(L, "first_scan_args[8] == '+X-C-W' and first_scan_args[9] == 1");
		MemScanTestHost.AssertLua(L, "first_scan_args[10] == '4'");
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	[Trait("Qualification", "Q29")]
	public void TryScanWithinBounds_never_writes_is_unique_or_scan_callbacks()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		SetAddresses(L, "100000000", "100000010");

		AobBoundedScanResult result = Scan(ModuleBounds, new Address[4]);

		string trace = MemScanTestHost.ReadTrace(L);
		Assert.Equal(AobBoundedScanOutcomeKind.Matches, result.Kind);
		Assert.DoesNotContain("scan.set.", trace, StringComparison.Ordinal);
		Assert.DoesNotContain("setOnlyOneResult:true", trace, StringComparison.Ordinal);
		Assert.DoesNotContain("scan.getOnlyResult", trace, StringComparison.Ordinal);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q27")]
	[Trait("Qualification", "Q28")]
	public void TryScanWithinBounds_zero_found_rows_with_an_empty_error_string_is_no_matches()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		Address[] destination = [new(0xA11CE)];

		AobBoundedScanResult result = Scan(ModuleBounds, destination);

		Assert.Equal(AobBoundedScanOutcomeKind.NoMatches, result.Kind);
		Assert.True(result.IsSuccess);
		Assert.Equal(0, result.Written);
		Assert.Equal(0UL, result.HostResultCount);
		Assert.True(result.InBoundsCountIsExact);
		Assert.Null(result.HostErrorText);
		Assert.False(result.IsHostErrorTextUnreadable);
		Assert.Equal(new Address(0xA11CE), destination[0]);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	public void TryScanWithinBounds_zero_found_rows_with_a_host_error_string_is_host_reported_error_without_parsing_it()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		MemScanTestHost.Run(L, "scan_error_string = 'No readable memory found'");
		Address[] destination = [new(0xA11CE)];

		AobBoundedScanResult result = Scan(ModuleBounds, destination);

		Assert.Equal(AobBoundedScanOutcomeKind.HostReportedError, result.Kind);
		Assert.False(result.IsSuccess);
		Assert.Equal("No readable memory found", result.HostErrorText);
		Assert.False(result.IsHostErrorTextTruncated);
		Assert.Equal(new Address(0xA11CE), destination[0]);
		Assert.Equal(TargetReleaseStatus.Released, result.Release.MemScan.Status);
		Assert.Equal(0, L.Top);
	}

	[Theory]
	[Trait("Qualification", "Q28")]
	[InlineData("No readable memory found. Please make sure you are attached to a live process")]
	[InlineData("Aucune mémoire lisible trouvée — vérifiez la cible")]
	[InlineData("text\\0with an embedded NUL")]
	public void TryScanWithinBounds_host_error_text_is_classified_by_presence_not_content(string text)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		MemScanTestHost.Run(L, "scan_error_string = '" + text + "'");

		AobBoundedScanResult result = Scan(ModuleBounds, new Address[1]);

		Assert.Equal(AobBoundedScanOutcomeKind.HostReportedError, result.Kind);
		Assert.Equal(text.Replace("\\0", "\0", StringComparison.Ordinal), result.HostErrorText);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	public void TryScanWithinBounds_unreadable_host_error_text_never_changes_the_primary_outcome()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		MemScanTestHost.Run(L, "scan_error_mode = 'raise'");
		SetAddresses(L, "100000000");

		AobBoundedScanResult matches = Scan(ModuleBounds, new Address[1]);
		_ = MemScanTestHost.Install(L);
		MemScanTestHost.Run(L, "scan_error_mode = 'raise'");
		AobBoundedScanResult noMatches = Scan(ModuleBounds, new Address[1]);

		Assert.Equal(AobBoundedScanOutcomeKind.Matches, matches.Kind);
		Assert.True(matches.IsHostErrorTextUnreadable);
		Assert.Equal(AobBoundedScanOutcomeKind.NoMatches, noMatches.Kind);
		Assert.True(noMatches.IsHostErrorTextUnreadable);
		Assert.Null(noMatches.HostErrorText);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	public void TryScanWithinBounds_skips_and_counts_matches_the_host_returns_below_start()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		// The start bound falls one byte inside the match at 0x100010BC0; CE still returns it (spike D4.2).
		SetAddresses(L, "100010BC0", "100010BF0");

		AobBoundedScanResult result = Scan(Bounds(0x1_0001_0BC1, 0x1_0036_7000), new Address[4]);

		Assert.Equal(AobBoundedScanOutcomeKind.Matches, result.Kind);
		Assert.Equal(1UL, result.BelowStartSkipped);
		Assert.Equal(0UL, result.AtOrAfterStopSkipped);
		Assert.Equal(1, result.Written);
		Assert.Equal(2UL, result.RowsRead);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	public void TryScanWithinBounds_skips_and_counts_matches_at_or_after_stop()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		SetAddresses(L, "100366FFF", "100367000", "FFFFFFFFFFFFFFFF");
		Address[] destination = new Address[4];

		AobBoundedScanResult result = Scan(ModuleBounds, destination);

		Assert.Equal(AobBoundedScanOutcomeKind.Matches, result.Kind);
		Assert.Equal(1, result.Written);
		Assert.Equal(new Address(0x1_0036_6FFF), destination[0]);
		Assert.Equal(2UL, result.AtOrAfterStopSkipped);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q29")]
	public void TryScanWithinBounds_fills_only_the_destination_and_reports_the_materialization_limit()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		SetAddresses(L, "100000000", "100000010", "100000020", "100000030", "100000040");
		Address[] destination = new Address[2];

		AobBoundedScanResult result = Scan(ModuleBounds, destination);

		Assert.Equal(AobBoundedScanOutcomeKind.Matches, result.Kind);
		Assert.Equal(2, result.Written);
		Assert.Equal([new Address(0x1_0000_0000), new Address(0x1_0000_0010)], destination);
		Assert.True(result.IsMaterializationLimitReached);
		Assert.Equal(5UL, result.HostResultCount);
		Assert.Equal(2UL, result.RowsRead);
		Assert.Equal(3UL, result.UnreadHostRows);
		Assert.False(result.InBoundsCountIsExact);
		Assert.DoesNotContain("results.getAddress:2", MemScanTestHost.ReadTrace(L), StringComparison.Ordinal);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q29")]
	public void TryScanWithinBounds_publishes_nothing_when_cancelled_during_the_copy_and_still_releases_once()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		MemScanTestHost.HostObjects objects = MemScanTestHost.Install(L);
		MemScanTestHost.Run(L, "found_count = 3");
		using CancellationTokenSource cancellation = new();
		using FakeHost.PCallProbe probe =
			FakeHost.ReplaceFoundListGetAddressWithPCallProbe(L, objects.FoundList, cancellation.Cancel);
		Address[] destination = [new(0xA11CE), new(0xA11CE), new(0xA11CE)];

		AobBoundedScanResult result = ScanWithToken(Bounds(0, 0x10000), destination, cancellation.Token);

		Assert.Equal(AobBoundedScanOutcomeKind.Cancelled, result.Kind);
		Assert.Equal(0, result.Written);
		Assert.Equal(1, probe.GetAddressCallCount);
		Assert.Equal(1UL, result.RowsRead);
		Assert.All(destination, static address => Assert.Equal(new Address(0xA11CE), address));
		Assert.EndsWith("list.deinitialize,list.destroy,scan.destroy", MemScanTestHost.ReadTrace(L),
			StringComparison.Ordinal);
		Assert.Equal(TargetReleaseStatus.Released, result.Release.FoundList.Status);
		Assert.Equal(TargetReleaseStatus.Released, result.Release.MemScan.Status);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q29")]
	[SuppressMessage("xUnit.Analyzers", "xUnit1051",
		Justification = "The token is cancelled on purpose to prove that no session is created.")]
	public void TryScanWithinBounds_cancellation_before_the_scan_creates_no_session()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		MemScanTestHost.ClearTrace(L);
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();

		AobBoundedScanResult result = ScanWithToken(ModuleBounds, new Address[1], cancellation.Token);

		Assert.Equal(AobBoundedScanOutcomeKind.Cancelled, result.Kind);
		Assert.Equal(MemoryScanCreationStatus.Unknown, result.Creation.Status);
		Assert.Equal(string.Empty, MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Theory]
	[Trait("Qualification", "Q28")]
	[InlineData("100000000", 0x1_0000_0000UL)]
	[InlineData("7FFC7A0A0000", 0x7FFC_7A0A_0000UL)]
	[InlineData("00400000", 0x40_0000UL)]
	public void TryScanWithinBounds_accepts_unpadded_x64_and_zero_padded_x86_host_address_text(string text,
		ulong expected)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		SetAddresses(L, text);
		Address[] destination = new Address[1];

		AobBoundedScanResult result = Scan(Bounds(0, 0x8000_0000_0000), destination);

		Assert.Equal(AobBoundedScanOutcomeKind.Matches, result.Kind);
		Assert.Equal(new Address(expected), destination[0]);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	public void TryScanWithinBounds_passes_the_pattern_bytes_verbatim_including_nul_and_non_ascii()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);

		_ = AobScanner.TryScanWithinBounds("48\0é ??", ModuleBounds, AobScanOptions.Default, new Address[1],
			TestContext.Current.CancellationToken);

		MemScanTestHost.AssertLua(L, "#first_scan_args[4] == 8 and first_scan_args[4] == '48\\0\\195\\169 ??'");
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	public void TryScanWithinBounds_without_a_qualified_target_reports_session_creation_failed_and_starts_no_scan()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		MemScanTestHost.Run(L, "opened_process_id = 0");
		MemScanTestHost.ClearTrace(L);

		AobBoundedScanResult result = Scan(ModuleBounds, new Address[1]);

		Assert.Equal(AobBoundedScanOutcomeKind.SessionCreationFailed, result.Kind);
		Assert.Equal(MemoryScanCreationStatus.TargetIdentityUnavailable, result.Creation.Status);
		Assert.Equal(TargetSelectionObservationStatus.NoTargetSelected, result.Creation.TargetObservation.Status);
		Assert.Equal(default, result.Release);
		Assert.Equal(string.Empty, MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	public void TryScanWithinBounds_scan_failure_is_scan_failed_and_still_releases_child_before_parent()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		MemScanTestHost.Run(L, "scan_first_raises = true; scan_first_error_payload = 'first scan rejected'");

		AobBoundedScanResult result = Scan(ModuleBounds, new Address[1]);

		Assert.Equal(AobBoundedScanOutcomeKind.ScanFailed, result.Kind);
		Assert.Equal(LuaStatus.RuntimeError, result.LuaStatus);
		Assert.Equal(MemoryScanTerminationStatus.NotRequired, result.Termination);
		Assert.Equal(MemoryScanTerminationStatus.Confirmed, result.Release.Termination);
		Assert.EndsWith("scan.first:14,scan.terminate:false,scan.wait:5000,list.destroy,scan.destroy",
			MemScanTestHost.ReadTrace(L), StringComparison.Ordinal);
		Assert.Equal(0, L.Top);
	}

	// Audit ch.08 and F13: a managed failure after the session exists, here the staging buffer's allocation, must not
	// leak the CE objects. The found list and then its scanner are destroyed once, and the original failure propagates.
	[Fact]
	[Trait("Qualification", "Q28")]
	public void TryScanWithinBounds_staging_allocation_failure_releases_the_created_session_once_and_rethrows()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		SetAddresses(L, "100000000");
		InsufficientMemoryException injected = new("injected staging allocation failure");
		StagingPool pool = new(injected);
		Address[] destination = [new(0xA11CE)];

		InsufficientMemoryException thrown = Assert.Throws<InsufficientMemoryException>(() =>
			AobBoundedScan.Run(Pattern, ModuleBounds, AobScanOptions.Default, null, destination, pool,
				TestContext.Current.CancellationToken));

		Assert.Same(injected, thrown);
		Assert.Equal(1, pool.RentCount);
		Assert.Equal(0, pool.ReturnCount);
		Assert.Equal(new Address(0xA11CE), destination[0]);
		Assert.Equal("factory.scan,factory.list,list.destroy,scan.destroy", MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Theory]
	[Trait("Qualification", "Q28")]
	[InlineData("found_addresses = { '100000000' }", AobBoundedScanOutcomeKind.Matches)]
	[InlineData("scan_first_raises = true", AobBoundedScanOutcomeKind.ScanFailed)]
	public void TryScanWithinBounds_returns_its_staging_buffer_once_to_the_pool_it_came_from(string mode,
		AobBoundedScanOutcomeKind expected)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		MemScanTestHost.Run(L, mode);
		StagingPool pool = new(null);

		AobBoundedScanResult result = AobBoundedScan.Run(Pattern, ModuleBounds, AobScanOptions.Default, null,
			new Address[2], pool, TestContext.Current.CancellationToken);

		Assert.Equal(expected, result.Kind);
		Assert.Equal(1, pool.RentCount);
		Assert.Equal(1, pool.ReturnCount);
		Assert.Same(pool.LastRented, pool.LastReturned);
		Assert.EndsWith("list.destroy,scan.destroy", MemScanTestHost.ReadTrace(L), StringComparison.Ordinal);
		Assert.Equal(0, L.Top);
	}

	[Theory]
	[Trait("Qualification", "Q28")]
	[InlineData("found_count = 'many'")]
	[InlineData("found_count = -1")]
	[InlineData("found_count = 1.5")]
	[InlineData("found_addresses = { 'zz' }")]
	[InlineData("found_addresses = { 42 }")]
	[InlineData("found_addresses = { '100000000', '0x' }")]
	public void TryScanWithinBounds_malformed_count_or_address_is_invalid_result_and_releases_once(string mode)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		MemScanTestHost.Run(L, mode);
		Address[] destination = [new(0xA11CE), new(0xA11CE)];

		AobBoundedScanResult result = Scan(ModuleBounds, destination);

		Assert.Equal(AobBoundedScanOutcomeKind.InvalidResult, result.Kind);
		Assert.Equal(0, result.Written);
		Assert.All(destination, static address => Assert.Equal(new Address(0xA11CE), address));
		Assert.EndsWith("list.deinitialize,list.destroy,scan.destroy", MemScanTestHost.ReadTrace(L),
			StringComparison.Ordinal);
		Assert.Equal(TargetReleaseStatus.Released, result.Release.FoundList.Status);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q30.a")]
	public void TryScanWithinBounds_target_change_during_the_scan_is_target_changed_and_publishes_nothing()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		MemScanTestHost.HostObjects objects = MemScanTestHost.Install(L);
		SetAddresses(L, "100000000");
		MemScanTestHost.Run(L, "scan_first_hook = function() opened_process_id = " +
		                       MemScanTestHost.FindOtherQualifiedProcessId().ToString(CultureInfo.InvariantCulture) +
		                       " end");
		Address[] destination = [new(0xA11CE)];

		AobBoundedScanResult result = Scan(ModuleBounds, destination);

		Assert.Equal(AobBoundedScanOutcomeKind.TargetChanged, result.Kind);
		Assert.Equal(new Address(0xA11CE), destination[0]);
		Assert.Equal(MemoryScanTerminationStatus.NotInvoked, result.Release.Termination);
		Assert.Equal(TargetReleaseStatus.RefusedTargetChanged, result.Release.FoundList.Status);
		Assert.Equal(TargetReleaseStatus.RefusedTargetChanged, result.Release.MemScan.Status);
		Assert.Equal("factory.scan,factory.list,scan.setOnlyOneResult:false,scan.first:14",
			MemScanTestHost.ReadTrace(L));
		Assert.False(FakeHost.IsDestroyed(L, objects.Scanner));
		Assert.Equal(0, L.Top);
	}

	[Theory]
	[Trait("Qualification", "Q28")]
	[InlineData("matches", AobBoundedScanOutcomeKind.Matches)]
	[InlineData("no-rows", AobBoundedScanOutcomeKind.NoMatches)]
	[InlineData("host-error", AobBoundedScanOutcomeKind.HostReportedError)]
	[InlineData("set-only-one-raises", AobBoundedScanOutcomeKind.ScanFailed)]
	[InlineData("first-raises", AobBoundedScanOutcomeKind.ScanFailed)]
	[InlineData("wait-raises", AobBoundedScanOutcomeKind.ScanFailed)]
	[InlineData("initialize-raises", AobBoundedScanOutcomeKind.ScanFailed)]
	[InlineData("count-raises", AobBoundedScanOutcomeKind.ScanFailed)]
	[InlineData("address-raises", AobBoundedScanOutcomeKind.ScanFailed)]
	[InlineData("address-malformed", AobBoundedScanOutcomeKind.InvalidResult)]
	[InlineData("no-target", AobBoundedScanOutcomeKind.SessionCreationFailed)]
	[InlineData("no-factory", AobBoundedScanOutcomeKind.SessionCreationFailed)]
	[InlineData("destroy-raises", AobBoundedScanOutcomeKind.Matches)]
	public void TryScanWithinBounds_leaves_the_stack_balanced_on_every_exit_path(string mode,
		AobBoundedScanOutcomeKind expected)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		SetAddresses(L, "100000000");
		MemScanTestHost.Run(L, mode switch
		{
			"no-rows" => "found_addresses = {}",
			"host-error" => "found_addresses = {}; scan_error_string = 'error'",
			"set-only-one-raises" => "scan_set_only_one_raises = true",
			"first-raises" => "scan_first_raises = true",
			"wait-raises" => "scan_wait_modes = { 'raise', 'true' }",
			"initialize-raises" => "list_initialize_raises = true",
			"count-raises" => "found_count_raises = true",
			"address-raises" => "found_address_raises = true",
			"address-malformed" => "found_addresses = { 'not-an-address' }",
			"no-target" => "opened_process_id = 0",
			"no-factory" => "createMemScan = nil",
			"destroy-raises" => "scan_destroy_raises = true; list_destroy_raises = true",
			_ => "local _ = 0"
		});

		AobBoundedScanResult result = Scan(ModuleBounds, new Address[2]);

		Assert.Equal(expected, result.Kind);
		Assert.Equal(0, L.Top);
		if (expected == AobBoundedScanOutcomeKind.SessionCreationFailed)
		{
			Assert.Equal(default, result.Release);
		}
		else
		{
			Assert.True(result.Release.OwnershipConsumed);
		}
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	public void TryScanWithinBounds_next_call_on_the_same_state_succeeds_after_a_failure()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		MemScanTestHost.Run(L, "scan_first_raises = true");
		AobBoundedScanResult failed = Scan(ModuleBounds, new Address[1]);
		_ = MemScanTestHost.Install(L);
		MemScanTestHost.Run(L, "scan_first_raises = false");
		SetAddresses(L, "100000000");
		Address[] destination = new Address[1];

		AobBoundedScanResult next = Scan(ModuleBounds, destination);

		Assert.Equal(AobBoundedScanOutcomeKind.ScanFailed, failed.Kind);
		Assert.Equal(AobBoundedScanOutcomeKind.Matches, next.Kind);
		Assert.Equal(new Address(0x1_0000_0000), destination[0]);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	public void TryScanWithinBounds_with_a_met_deadline_matches_the_unbounded_call()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		SetAddresses(L, "100000000", "7FFC7A0A0000", "100000040");
		Address[] plainDestination = new Address[4];
		AobBoundedScanResult plain = Scan(ModuleBounds, plainDestination);
		_ = MemScanTestHost.Install(L);
		SetAddresses(L, "100000000", "7FFC7A0A0000", "100000040");
		Address[] deadlineDestination = new Address[4];

		AobBoundedScanResult deadline = AobScanner.TryScanWithinBounds(Pattern, ModuleBounds, AobScanOptions.Default,
			TimeSpan.FromSeconds(30), deadlineDestination, TestContext.Current.CancellationToken);

		Assert.Equal(plain.Kind, deadline.Kind);
		Assert.Equal(plain.Written, deadline.Written);
		Assert.Equal(plain.HostResultCount, deadline.HostResultCount);
		Assert.Equal(plain.RowsRead, deadline.RowsRead);
		Assert.Equal(plain.AtOrAfterStopSkipped, deadline.AtOrAfterStopSkipped);
		Assert.Equal(plainDestination, deadlineDestination);
		Assert.Contains("scan.wait:30000,list.initialize", MemScanTestHost.ReadTrace(L), StringComparison.Ordinal);
		Assert.Equal(MemoryScanTerminationStatus.NotRequired, deadline.Termination);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q29")]
	public void TryScanWithinBounds_with_an_expired_deadline_terminates_cooperatively_waits_and_releases_once()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		SetAddresses(L, "100000000");
		MemScanTestHost.Run(L, "scan_wait_modes = { 'false', 'true' }");
		Address[] destination = [new(0xA11CE)];

		AobBoundedScanResult result = AobScanner.TryScanWithinBounds(Pattern, ModuleBounds, AobScanOptions.Default,
			TimeSpan.FromMilliseconds(100), destination, TestContext.Current.CancellationToken);

		Assert.Equal(AobBoundedScanOutcomeKind.WaitTimedOut, result.Kind);
		Assert.Equal(MemoryScanTerminationStatus.Confirmed, result.Termination);
		Assert.Equal(MemoryScanTerminationStatus.NotRequired, result.Release.Termination);
		Assert.Equal(new Address(0xA11CE), destination[0]);
		Assert.Equal(
			"factory.scan,factory.list,scan.setOnlyOneResult:false,scan.first:14,scan.wait:100," +
			"scan.terminate:false,scan.wait:5000,list.destroy,scan.destroy",
			MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q29")]
	public void TryScanWithinBounds_with_an_expired_deadline_and_an_unconfirmed_stop_never_repeats_the_stop()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		MemScanTestHost.Run(L, "scan_wait_mode = 'false'");

		AobBoundedScanResult result = AobScanner.TryScanWithinBounds(Pattern, ModuleBounds, AobScanOptions.Default,
			TimeSpan.FromMilliseconds(100), new Address[1], TestContext.Current.CancellationToken);

		Assert.Equal(AobBoundedScanOutcomeKind.WaitTimedOut, result.Kind);
		Assert.Equal(MemoryScanTerminationStatus.WaitTimedOut, result.Termination);
		Assert.Equal(MemoryScanTerminationStatus.WaitTimedOut, result.Release.Termination);
		Assert.Equal(
			"factory.scan,factory.list,scan.setOnlyOneResult:false,scan.first:14,scan.wait:100," +
			"scan.terminate:false,scan.wait:5000,list.destroy,scan.destroy",
			MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	public void TryScanWithinBounds_issues_one_scan_wait_initialize_and_count_and_at_most_one_row_read_per_needed_row()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		SetAddresses(L, "FFFF0000", "100000000", "7FFC7A0A0000", "100000010", "100000020", "100000030", "100000040");
		Address[] destination = new Address[3];

		AobBoundedScanResult result = Scan(ModuleBounds, destination);

		string trace = MemScanTestHost.ReadTrace(L);
		Assert.Equal(1, Occurrences(trace, "scan.first:"));
		Assert.Equal(1, Occurrences(trace, "scan.wait"));
		Assert.Equal(1, Occurrences(trace, "list.initialize"));
		Assert.Equal(1, Occurrences(trace, "results.getCount"));
		Assert.Equal((int) result.RowsRead, Occurrences(trace, "results.getAddress:"));
		Assert.Equal(result.RowsRead,
			(ulong) result.Written + result.BelowStartSkipped + result.AtOrAfterStopSkipped);
		Assert.Equal(5UL, result.RowsRead);
		Assert.Equal(1UL, result.BelowStartSkipped);
		Assert.Equal(1UL, result.AtOrAfterStopSkipped);
		Assert.Equal(3, result.Written);
		Assert.Equal(2UL, result.UnreadHostRows);
		Assert.True(result.IsMaterializationLimitReached);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	public void TryScanWithinBounds_reads_only_addresses_and_never_values()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		SetAddresses(L, "100000000", "100000010", "100000020");

		AobBoundedScanResult result = Scan(ModuleBounds, new Address[8]);

		Assert.Equal(3, result.Written);
		Assert.DoesNotContain("results.getValue", MemScanTestHost.ReadTrace(L), StringComparison.Ordinal);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	public void TryScanWithinBounds_reports_non_negative_host_scan_copy_and_total_durations()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		SetAddresses(L, "100000000", "100000010");
		MemScanTestHost.Run(L,
			"scan_wait_hook = function() local t = os.clock(); while os.clock() - t < 0.02 do end end");

		AobBoundedScanResult result = Scan(ModuleBounds, new Address[4]);

		Assert.Equal(AobBoundedScanOutcomeKind.Matches, result.Kind);
		Assert.True(result.HostScanElapsed >= TimeSpan.FromMilliseconds(15),
			"The host scan duration must include the wait: " + result.HostScanElapsed);
		Assert.True(result.CopyElapsed >= TimeSpan.Zero);
		Assert.True(result.TotalElapsed >= result.HostScanElapsed + result.CopyElapsed,
			"The total must contain the disjoint scan and copy intervals.");
		Assert.Equal(0, L.Top);
	}

	private static AobBoundedScanResult Scan(AobScanBounds bounds, Span<Address> destination)
	{
		return AobScanner.TryScanWithinBounds(Pattern, bounds, AobScanOptions.Default, destination,
			TestContext.Current.CancellationToken);
	}

	private static AobBoundedScanResult ScanWithToken(AobScanBounds bounds, Span<Address> destination,
		CancellationToken cancellationToken)
	{
		return AobScanner.TryScanWithinBounds(Pattern, bounds, AobScanOptions.Default, destination, cancellationToken);
	}

	private static AobScanBounds Bounds(ulong start, ulong stop)
	{
		Assert.True(AobScanBounds.TryCreate(new Address(start), new Address(stop), out AobScanBounds bounds));
		return bounds;
	}

	private static void SetAddresses(LuaState state, params string[] addresses)
	{
		MemScanTestHost.Run(state, "found_addresses = { '" + string.Join("', '", addresses) + "' }");
	}

	private static int Occurrences(string text, string value)
	{
		int count = 0;
		for (int index = text.IndexOf(value, StringComparison.Ordinal);
		     index >= 0;
		     index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
		{
			count++;
		}

		return count;
	}

	// The staging-buffer seam of AobBoundedScan.Run: hands out plain arrays, or fails every Rent with the injected
	// exception, and records both calls.
	private sealed class StagingPool : ArrayPool<Address>
	{
		private readonly Exception? _rentFailure;

		public StagingPool(Exception? rentFailure)
		{
			_rentFailure = rentFailure;
		}

		public int RentCount
		{
			get;
			private set;
		}

		public int ReturnCount
		{
			get;
			private set;
		}

		public Address[]? LastRented
		{
			get;
			private set;
		}

		public Address[]? LastReturned
		{
			get;
			private set;
		}

		public override Address[] Rent(int minimumLength)
		{
			RentCount++;
			if (_rentFailure is not null)
			{
				throw _rentFailure;
			}

			LastRented = new Address[minimumLength];
			return LastRented;
		}

		public override void Return(Address[] array, bool clearArray = false)
		{
			ReturnCount++;
			LastReturned = array;
		}
	}
}
