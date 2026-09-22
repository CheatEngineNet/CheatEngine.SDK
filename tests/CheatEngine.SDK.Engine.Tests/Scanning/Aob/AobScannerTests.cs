using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Scanning.Aob;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Scanning.Aob;

/// <summary>AOBScan's exact CE argument positions, protected failure conversion, and owned StringList result.</summary>
[Trait("Category", "NativeLua")]
public sealed class AobScannerTests
{
	[Fact]
	public void TryScan_default_options_passes_only_the_pattern_and_returns_a_caller_owned_list()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		CEObject handle = AobStringListTestHost.CreateList(L);
		AobStringListTestHost.InstallAobScan(L, handle);

		Assert.True(AobScanner.TryScan("48 8B ?? 89", out Owned<StringList>? results));
		Owned<StringList> owned = Assert.IsType<Owned<StringList>>(results);
		Assert.True(owned.Value.TryGetCount(out int count));
		Assert.Equal(2, count);
		Assert.True(owned.Value.TryGetItem(1, out string? second));
		Assert.Equal("7FF6A1B2C3D4", second);
		Assert.Equal(0, L.Top);

		EngineTest.Run(L, "return aob_argument_count, aob_pattern, aob_protection"u8, 3);
		Assert.Equal(1, EngineTest.ReadInteger(L, -3));
		Assert.Equal("48 8B ?? 89", EngineTest.ReadString(L, -2));
		Assert.True(L.IsNil(-1));
		L.SetTop(0);

		owned.Dispose();
		Assert.True(owned.IsDisposed);
		Assert.True(FakeHost.IsDestroyed(L, handle));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void TryScan_alignment_preserves_a_nil_protection_slot_and_all_four_CE_positions()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		CEObject handle = AobStringListTestHost.CreateList(L);
		AobStringListTestHost.InstallAobScan(L, handle);
		AobScanOptions options = new(null, FastScanMethod.Aligned, "16");

		Assert.True(AobScanner.TryScan("90 90", options, out Owned<StringList>? results));
		Owned<StringList> owned = Assert.IsType<Owned<StringList>>(results);
		EngineTest.Run(L, "return aob_argument_count, aob_protection, aob_alignment, aob_alignment_parameter"u8, 4);
		Assert.Equal(4, EngineTest.ReadInteger(L, -4));
		Assert.True(L.IsNil(-3));
		Assert.Equal((long) FastScanMethod.Aligned, EngineTest.ReadInteger(L, -2));
		Assert.Equal("16", EngineTest.ReadString(L, -1));
		L.SetTop(0);

		owned.Dispose();
		Assert.True(FakeHost.IsDestroyed(L, handle));
	}

	[Fact]
	public void TryScan_protection_only_passes_two_CE_positions_without_synthesizing_alignment_values()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		CEObject handle = AobStringListTestHost.CreateList(L);
		AobStringListTestHost.InstallAobScan(L, handle);
		AobScanOptions options = new("+X-C-W", FastScanMethod.NotAligned, null);

		Assert.True(AobScanner.TryScan("CC", options, out Owned<StringList>? results));
		Owned<StringList> owned = Assert.IsType<Owned<StringList>>(results);
		EngineTest.Run(L, "return aob_argument_count, aob_protection, aob_alignment"u8, 3);
		Assert.Equal(2, EngineTest.ReadInteger(L, -3));
		Assert.Equal("+X-C-W", EngineTest.ReadString(L, -2));
		Assert.True(L.IsNil(-1));
		L.SetTop(0);

		owned.Dispose();
		Assert.True(FakeHost.IsDestroyed(L, handle));
	}

	[Fact]
	public void TryScan_nil_or_raising_result_returns_false_and_restores_the_stack()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AobStringListTestHost.InstallAobScan(L, AobStringListTestHost.CreateList(L));

		Assert.False(AobScanner.TryScan("nil-result", out Owned<StringList>? nilResults));
		Assert.Null(nilResults);
		Assert.Equal(0, L.Top);

		Assert.False(AobScanner.TryScan("raise", out Owned<StringList>? raisedResults));
		Assert.Null(raisedResults);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void TryScanDetailed_distinguishes_nil_lua_failure_and_invalid_non_nil_results()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AobStringListTestHost.InstallAobScan(L, AobStringListTestHost.CreateList(L));
		int top = L.Top;

		AobScanStatus status = AobScanner.TryScanDetailed("nil-result", out Owned<StringList>? nilResults);

		Assert.Equal(AobScanStatus.NoResult, status);
		Assert.Null(nilResults);
		Assert.Equal(top, L.Top);

		status = AobScanner.TryScanDetailed("raise", out Owned<StringList>? raisedResults);

		Assert.Equal(AobScanStatus.LuaFailure, status);
		Assert.Null(raisedResults);
		Assert.Equal(top, L.Top);

		status = AobScanner.TryScanDetailed("invalid-result", out Owned<StringList>? invalidResults);

		Assert.Equal(AobScanStatus.InvalidResult, status);
		Assert.Null(invalidResults);
		Assert.Equal(top, L.Top);
	}

	[Fact]
	public void TryScanDetailed_empty_string_list_is_a_successful_caller_owned_result()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		CEObject handle = AobStringListTestHost.CreateEmptyList(L);
		AobStringListTestHost.InstallAobScan(L, handle);
		int top = L.Top;

		AobScanStatus status = AobScanner.TryScanDetailed("48 8B", out Owned<StringList>? results);

		Assert.Equal(AobScanStatus.Success, status);
		Owned<StringList> owned = Assert.IsType<Owned<StringList>>(results);
		Assert.True(owned.Value.TryGetCount(out int count));
		Assert.Equal(0, count);
		Assert.Equal(top, L.Top);

		owned.Dispose();
		Assert.True(FakeHost.IsDestroyed(L, handle));
		Assert.Equal(top, L.Top);
	}

	[Fact]
	public void TryScanDetailed_unavailable_global_does_not_enter_lua()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		int top = L.Top;

		AobScanStatus status = AobScanner.TryScanDetailed("48 8B", out Owned<StringList>? results);

		Assert.Equal(AobScanStatus.GlobalUnavailable, status);
		Assert.Null(results);
		Assert.Equal(top, L.Top);
	}

	[Fact]
	public void TryScanOutcome_reports_matches_and_keeps_the_sole_owner_alive_until_the_caller_copies_and_disposes()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		CEObject handle = AobStringListTestHost.CreateList(L);
		AobStringListTestHost.InstallAobScan(L, handle);
		int top = L.Top;

		AobScanOutcome outcome = AobScanner.TryScanOutcome("48 8B ?? 89", out Owned<StringList>? results);

		Assert.Equal(AobScanOutcomeKind.Matches, outcome.Kind);
		Assert.True(outcome.IsSuccess);
		Assert.True(outcome.HasResultCount);
		Assert.Equal(2, outcome.ResultCount);
		Assert.Equal(LuaStatus.Ok, outcome.LuaStatus);
		Owned<StringList> owned = Assert.IsType<Owned<StringList>>(results);
		Assert.True(owned.Value.TryGetItem(0, out string? first));
		Assert.True(owned.Value.TryGetItem(1, out string? second));
		Assert.Equal("00401000", first);
		Assert.Equal("7FF6A1B2C3D4", second);
		Assert.Equal(top, L.Top);

		owned.Dispose();

		Assert.True(owned.IsDisposed);
		Assert.True(FakeHost.IsDestroyed(L, handle));
		Assert.Equal("00401000", first);
		Assert.Equal("7FF6A1B2C3D4", second);
		Assert.Equal(top, L.Top);
	}

	[Fact]
	public void TryScanOutcome_classifies_only_a_valid_empty_list_as_no_matches()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		CEObject handle = AobStringListTestHost.CreateEmptyList(L);
		AobStringListTestHost.InstallAobScan(L, handle);
		int top = L.Top;

		AobScanOutcome outcome = AobScanner.TryScanOutcome("48 8B", out Owned<StringList>? results);

		Assert.Equal(AobScanOutcomeKind.NoMatches, outcome.Kind);
		Assert.True(outcome.IsSuccess);
		Assert.True(outcome.HasResultCount);
		Assert.Equal(0, outcome.ResultCount);
		Owned<StringList> owned = Assert.IsType<Owned<StringList>>(results);
		Assert.Equal(top, L.Top);

		owned.Dispose();

		Assert.True(owned.IsDisposed);
		Assert.True(FakeHost.IsDestroyed(L, handle));
		Assert.Equal(top, L.Top);
	}

	[Fact]
	public void TryScanOutcome_keeps_nil_lua_failure_and_invalid_scalar_as_distinct_non_match_outcomes()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AobStringListTestHost.InstallAobScan(L, AobStringListTestHost.CreateList(L));
		int top = L.Top;

		AobScanOutcome nilOutcome = AobScanner.TryScanOutcome("nil-result", out Owned<StringList>? nilResults);
		AobScanOutcome luaOutcome = AobScanner.TryScanOutcome("raise", out Owned<StringList>? luaResults);
		AobScanOutcome invalidOutcome =
			AobScanner.TryScanOutcome("invalid-result", out Owned<StringList>? invalidResults);

		Assert.Equal(AobScanOutcomeKind.NoResult, nilOutcome.Kind);
		Assert.False(nilOutcome.IsSuccess);
		Assert.False(nilOutcome.HasResultCount);
		Assert.Null(nilResults);
		Assert.Equal(AobScanOutcomeKind.ProtectedLuaFailure, luaOutcome.Kind);
		Assert.Equal(LuaStatus.RuntimeError, luaOutcome.LuaStatus);
		Assert.False(luaOutcome.IsSuccess);
		Assert.Null(luaResults);
		Assert.Equal(AobScanOutcomeKind.InvalidResult, invalidOutcome.Kind);
		Assert.False(invalidOutcome.IsSuccess);
		Assert.Null(invalidResults);
		Assert.Equal(top, L.Top);
	}

	[Fact]
	public void TryScanOutcome_distinguishes_missing_global_protected_lookup_failure_and_malformed_userdata()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState missingState = new();
		using HostScope missingScope = new(missingState);
		LuaState missing = missingScope.State;
		int missingTop = missing.Top;

		AobScanOutcome missingOutcome = AobScanner.TryScanOutcome("48 8B", out Owned<StringList>? missingResults);

		Assert.Equal(AobScanOutcomeKind.GlobalUnavailable, missingOutcome.Kind);
		Assert.Null(missingResults);
		Assert.Equal(missingTop, missing.Top);

		using NativeLuaState lookupState = new();
		using HostScope lookupScope = new(lookupState);
		LuaState lookup = lookupScope.State;
		EngineTest.Run(lookup, """
		                       setmetatable(_G, {
		                         __index = function(_, name)
		                           if name == 'AOBScan' then error('AOBScan lookup failed') end
		                         end
		                       })
		                       """u8);
		int lookupTop = lookup.Top;

		AobScanOutcome lookupOutcome = AobScanner.TryScanOutcome("48 8B", out Owned<StringList>? lookupResults);

		Assert.Equal(AobScanOutcomeKind.ProtectedLuaFailure, lookupOutcome.Kind);
		Assert.Equal(LuaStatus.RuntimeError, lookupOutcome.LuaStatus);
		Assert.Null(lookupResults);
		Assert.Equal(lookupTop, lookup.Top);

		using NativeLuaState malformedState = new();
		using HostScope malformedScope = new(malformedState);
		LuaState malformed = malformedScope.State;
		AobStringListTestHost.InstallAobScan(malformed, AobStringListTestHost.CreateList(malformed));
		_ = malformed.NewUserdata(1);
		Assert.True(malformed.TrySetGlobal("aob_malformed"u8).IsOk);
		int malformedTop = malformed.Top;

		AobScanOutcome malformedOutcome =
			AobScanner.TryScanOutcome("malformed-result", out Owned<StringList>? malformedResults);

		Assert.Equal(AobScanOutcomeKind.InvalidResult, malformedOutcome.Kind);
		Assert.Null(malformedResults);
		Assert.Equal(malformedTop, malformed.Top);
	}

	[Fact]
	public void TryScanOutcome_releases_a_valid_host_object_when_its_count_is_unreadable()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		CEObject handle = AobStringListTestHost.CreateInvalidCountList(L);
		AobStringListTestHost.InstallAobScan(L, handle);
		int top = L.Top;

		AobScanOutcome outcome = AobScanner.TryScanOutcome("48 8B", out Owned<StringList>? results);

		Assert.Equal(AobScanOutcomeKind.ResultListCountUnavailable, outcome.Kind);
		Assert.False(outcome.IsSuccess);
		Assert.False(outcome.HasResultCount);
		Assert.Null(results);
		Assert.True(FakeHost.IsDestroyed(L, handle));
		Assert.Equal(top, L.Top);
	}

	[Fact]
	public void AobScanOutcome_default_is_unknown_and_never_reports_a_successful_match()
	{
		AobScanOutcome outcome = default;

		Assert.Equal(AobScanOutcomeKind.Unknown, outcome.Kind);
		Assert.False(outcome.HasResultCount);
		Assert.False(outcome.IsSuccess);
		Assert.Equal(0, outcome.ResultCount);
		Assert.Equal(LuaStatus.Ok, outcome.LuaStatus);
	}

	[Fact]
	public void TryScan_while_detached_throws_without_attempting_lua_access()
	{
		LuaRuntime.Detach();

		Assert.Throws<InvalidOperationException>(() => AobScanner.TryScan("90", out _));
	}
}
