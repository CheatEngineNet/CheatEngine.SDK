using System.Globalization;
using System.Text;

using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Scanning.Aob;
using CheatEngine.SDK.Engine.Targets;
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
	[Trait("Qualification", "Q27")]
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

	[Fact]
	[Trait("Qualification", "Q27")]
	public void TryScanOutcome_zero_values_as_CE_7_7_reports_zero_matches_is_no_result_never_no_matches()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AobStringListTestHost.InstallAobScan(L, AobStringListTestHost.CreateList(L));
		long destroyedBefore = FakeHost.DestroyedCount(L);

		AobScanOutcome outcome = AobScanner.TryScanOutcome("zero-values", out Owned<StringList>? results);

		Assert.Equal(AobScanOutcomeKind.NoResult, outcome.Kind);
		Assert.NotEqual(AobScanOutcomeKind.NoMatches, outcome.Kind);
		Assert.False(outcome.IsSuccess);
		Assert.False(outcome.HasResultCount);
		Assert.Equal(LuaStatus.Ok, outcome.LuaStatus);
		Assert.Null(results);
		Assert.Equal(destroyedBefore, FakeHost.DestroyedCount(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q27")]
	public void TryScanDetailed_zero_values_and_an_explicit_nil_yield_the_same_status_and_restore_the_stack()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AobStringListTestHost.InstallAobScan(L, AobStringListTestHost.CreateList(L));
		long destroyedBefore = FakeHost.DestroyedCount(L);

		AobScanStatus zeroValues = AobScanner.TryScanDetailed("zero-values", out Owned<StringList>? zeroResults);
		Assert.Equal(0, L.Top);
		AobScanStatus explicitNil = AobScanner.TryScanDetailed("nil-result", out Owned<StringList>? nilResults);
		Assert.Equal(0, L.Top);

		Assert.Equal(AobScanStatus.NoResult, zeroValues);
		Assert.Equal(zeroValues, explicitNil);
		Assert.Null(zeroResults);
		Assert.Null(nilResults);
		Assert.Equal(destroyedBefore, FakeHost.DestroyedCount(L));
	}

	[Fact]
	[Trait("Qualification", "Q27")]
	public void TryScan_zero_values_returns_false_without_an_owner()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AobStringListTestHost.InstallAobScan(L, AobStringListTestHost.CreateList(L));
		long destroyedBefore = FakeHost.DestroyedCount(L);

		Assert.False(AobScanner.TryScan("zero-values", out Owned<StringList>? results));

		Assert.Null(results);
		Assert.Equal(destroyedBefore, FakeHost.DestroyedCount(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q27")]
	public void TryScanOutcome_missing_global_is_global_unavailable()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;

		AobScanOutcome outcome = AobScanner.TryScanOutcome("48 8B", out Owned<StringList>? results);

		Assert.Equal(AobScanOutcomeKind.GlobalUnavailable, outcome.Kind);
		Assert.Equal(LuaStatus.Ok, outcome.LuaStatus);
		Assert.False(outcome.IsSuccess);
		Assert.Null(results);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q27")]
	public void TryScanOutcome_nil_result_is_no_result()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AobStringListTestHost.InstallAobScan(L, AobStringListTestHost.CreateList(L));

		AobScanOutcome outcome = AobScanner.TryScanOutcome("nil-result", out Owned<StringList>? results);

		Assert.Equal(AobScanOutcomeKind.NoResult, outcome.Kind);
		Assert.Equal(LuaStatus.Ok, outcome.LuaStatus);
		Assert.False(outcome.IsSuccess);
		Assert.Null(results);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q27")]
	public void TryScanOutcome_protected_call_error_is_protected_lua_failure_with_its_status()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AobStringListTestHost.InstallAobScan(L, AobStringListTestHost.CreateList(L));
		long destroyedBefore = FakeHost.DestroyedCount(L);

		AobScanOutcome outcome = AobScanner.TryScanOutcome("raise", out Owned<StringList>? results);

		Assert.Equal(AobScanOutcomeKind.ProtectedLuaFailure, outcome.Kind);
		Assert.Equal(LuaStatus.RuntimeError, outcome.LuaStatus);
		Assert.False(outcome.IsSuccess);
		Assert.Null(results);
		Assert.Equal(destroyedBefore, FakeHost.DestroyedCount(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q27")]
	public void TryScanOutcome_protected_global_lookup_error_is_protected_lua_failure()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		EngineTest.Run(L, """
		                  setmetatable(_G, {
		                    __index = function(_, name)
		                      if name == 'AOBScan' then error('AOBScan lookup failed') end
		                    end
		                  })
		                  """u8);

		AobScanOutcome outcome = AobScanner.TryScanOutcome("48 8B", out Owned<StringList>? results);

		Assert.Equal(AobScanOutcomeKind.ProtectedLuaFailure, outcome.Kind);
		Assert.Equal(LuaStatus.RuntimeError, outcome.LuaStatus);
		Assert.Null(results);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q27")]
	public void TryScanOutcome_malformed_userdata_is_invalid_result()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AobStringListTestHost.InstallAobScan(L, AobStringListTestHost.CreateList(L));
		_ = L.NewUserdata(1);
		Assert.True(L.TrySetGlobal("aob_malformed"u8).IsOk);
		long destroyedBefore = FakeHost.DestroyedCount(L);

		AobScanOutcome outcome = AobScanner.TryScanOutcome("malformed-result", out Owned<StringList>? results);

		Assert.Equal(AobScanOutcomeKind.InvalidResult, outcome.Kind);
		Assert.Equal(LuaStatus.Ok, outcome.LuaStatus);
		Assert.False(outcome.IsSuccess);
		Assert.Null(results);
		Assert.Equal(destroyedBefore, FakeHost.DestroyedCount(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q27")]
	public void TryScanOutcome_valid_empty_list_is_no_matches_with_a_live_owner()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		CEObject handle = AobStringListTestHost.CreateEmptyList(L);
		AobStringListTestHost.InstallAobScan(L, handle);

		AobScanOutcome outcome = AobScanner.TryScanOutcome("48 8B", out Owned<StringList>? results);

		Assert.Equal(AobScanOutcomeKind.NoMatches, outcome.Kind);
		Assert.Equal(0, outcome.ResultCount);
		Owned<StringList> owned = Assert.IsType<Owned<StringList>>(results);
		Assert.False(owned.IsDisposed);
		Assert.False(FakeHost.IsDestroyed(L, handle));
		Assert.Equal(0, L.Top);

		owned.Dispose();

		Assert.True(FakeHost.IsDestroyed(L, handle));
		Assert.Equal(0, L.Top);
	}

	[Theory]
	[Trait("Qualification", "Q27")]
	[InlineData("'AOBScan failed'")]
	[InlineData("'Échec de l’analyse — mémoire illisible'")]
	[InlineData("''")]
	[InlineData("{ code = 1 }")]
	[InlineData("nil")]
	[InlineData("42")]
	public void TryScanOutcome_category_does_not_depend_on_the_lua_error_text(string payload)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AobStringListTestHost.InstallAobScan(L, AobStringListTestHost.CreateList(L));
		EngineTest.Run(L, Encoding.UTF8.GetBytes("aob_error_payload = " + payload));

		AobScanOutcome outcome = AobScanner.TryScanOutcome("raise-text", out Owned<StringList>? results);

		Assert.Equal(AobScanOutcome.ProtectedLuaFailure(LuaStatus.RuntimeError), outcome);
		Assert.Null(results);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q27")]
	public void TryScanOutcome_next_call_on_the_same_state_succeeds_after_a_protected_failure()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		CEObject handle = AobStringListTestHost.CreateList(L);
		AobStringListTestHost.InstallAobScan(L, handle);

		AobScanOutcome failed = AobScanner.TryScanOutcome("raise", out Owned<StringList>? failedResults);
		AobScanOutcome next = AobScanner.TryScanOutcome("48 8B", out Owned<StringList>? results);

		Assert.Equal(AobScanOutcomeKind.ProtectedLuaFailure, failed.Kind);
		Assert.Null(failedResults);
		Assert.Equal(AobScanOutcome.Matches(2), next);
		using Owned<StringList> owned = Assert.IsType<Owned<StringList>>(results);
		Assert.True(owned.Value.TryGetItem(0, out string? first));
		Assert.Equal("00401000", first);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void TryScanOutcome_with_target_context_reports_the_same_qualified_incarnation_before_and_after()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AobStringListTestHost.InstallTarget(L, Environment.ProcessId);
		AobStringListTestHost.InstallAobScan(L, AobStringListTestHost.CreateList(L));

		AobScanOutcome outcome = AobScanner.TryScanOutcome("48 8B", AobScanOptions.Default,
			out Owned<StringList>? results, out AobScanTargetContext context);

		using Owned<StringList> owned = Assert.IsType<Owned<StringList>>(results);
		Assert.Equal(AobScanOutcome.Matches(2), outcome);
		Assert.True(context.Before.IsQualified);
		Assert.True(context.After.IsQualified);
		Assert.Equal(Environment.ProcessId, context.Before.Incarnation!.Value.ProcessId);
		Assert.Equal(context.Before.Incarnation, context.After.Incarnation);
		Assert.True(context.IsSameQualifiedIncarnation);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void TryScanOutcome_with_target_context_reports_a_target_change_without_reclassifying_the_scan()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		int otherProcessId = MemScanTestHost.FindOtherQualifiedProcessId();
		AobStringListTestHost.InstallTarget(L, Environment.ProcessId);
		AobStringListTestHost.InstallAobScan(L, AobStringListTestHost.CreateList(L));
		MemScanTestHost.Run(L, "aob_retarget_pid = " + otherProcessId.ToString(CultureInfo.InvariantCulture));

		AobScanOutcome outcome = AobScanner.TryScanOutcome("retarget", AobScanOptions.Default,
			out Owned<StringList>? results, out AobScanTargetContext context);

		// Both observations are qualified, but of two different process incarnations.
		using Owned<StringList> owned = Assert.IsType<Owned<StringList>>(results);
		Assert.Equal(AobScanOutcome.Matches(2), outcome);
		Assert.True(context.Before.IsQualified);
		Assert.True(context.After.IsQualified);
		Assert.Equal(Environment.ProcessId, context.Before.Incarnation!.Value.ProcessId);
		Assert.Equal(otherProcessId, context.After.Incarnation!.Value.ProcessId);
		Assert.NotEqual(context.Before.Incarnation, context.After.Incarnation);
		Assert.False(context.IsSameQualifiedIncarnation);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void TryScanOutcome_with_target_context_and_no_target_still_scans_and_reports_unqualified_facts()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AobStringListTestHost.InstallTarget(L, 0);
		AobStringListTestHost.InstallAobScan(L, AobStringListTestHost.CreateList(L));

		AobScanOutcome outcome = AobScanner.TryScanOutcome("48 8B", AobScanOptions.Default,
			out Owned<StringList>? results, out AobScanTargetContext context);

		using Owned<StringList> owned = Assert.IsType<Owned<StringList>>(results);
		Assert.Equal(AobScanOutcome.Matches(2), outcome);
		Assert.Equal(TargetSelectionObservationStatus.NoTargetSelected, context.Before.Status);
		Assert.Equal(TargetSelectionObservationStatus.NoTargetSelected, context.After.Status);
		Assert.False(context.IsSameQualifiedIncarnation);
		Assert.Equal(0, L.Top);
		Assert.Equal(1L, MemScanTestHost.ReadInteger(L, "aob_calls"));
	}

	[Theory]
	[InlineData("48 8B")]
	[InlineData("zero-values")]
	[InlineData("nil-result")]
	[InlineData("raise")]
	[InlineData("invalid-result")]
	[InlineData("empty")]
	public void TryScanOutcome_with_target_context_classifies_exactly_like_the_three_argument_overload(string mode)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AobStringListTestHost.InstallTarget(L, Environment.ProcessId);
		AobStringListTestHost.InstallAobScan(L, CreateListFor(L, mode));

		AobScanOutcome plain = AobScanner.TryScanOutcome(mode, AobScanOptions.Default, out Owned<StringList>? first);
		first?.Dispose();
		AobStringListTestHost.SetGlobalObject(L, "aob_results"u8, CreateListFor(L, mode));
		AobScanOutcome withContext = AobScanner.TryScanOutcome(mode, AobScanOptions.Default,
			out Owned<StringList>? second, out _);
		second?.Dispose();

		Assert.Equal(plain, withContext);
		Assert.Equal(first is null, second is null);
		Assert.Equal(0, L.Top);
	}

	[Theory]
	[InlineData(AobScanStatus.Unknown)]
	[InlineData(AobScanStatus.Success)]
	[InlineData((AobScanStatus) 250)]
	public void FromStatus_never_maps_an_unexpected_status_to_a_count_failure(AobScanStatus status)
	{
		AobScanOutcome outcome = AobScanner.FromStatus(status, LuaStatus.Ok);

		Assert.Equal(AobScanOutcomeKind.Unknown, outcome.Kind);
		Assert.NotEqual(AobScanOutcomeKind.ResultListCountUnavailable, outcome.Kind);
		Assert.False(outcome.IsSuccess);
		Assert.Equal(default, outcome);
	}

	[Theory]
	[InlineData(AobScanStatus.GlobalUnavailable, AobScanOutcomeKind.GlobalUnavailable)]
	[InlineData(AobScanStatus.LuaFailure, AobScanOutcomeKind.ProtectedLuaFailure)]
	[InlineData(AobScanStatus.NoResult, AobScanOutcomeKind.NoResult)]
	[InlineData(AobScanStatus.InvalidResult, AobScanOutcomeKind.InvalidResult)]
	public void FromStatus_maps_every_failure_status_to_its_own_outcome_kind(AobScanStatus status,
		AobScanOutcomeKind expected)
	{
		AobScanOutcome outcome = AobScanner.FromStatus(status, LuaStatus.RuntimeError);

		Assert.Equal(expected, outcome.Kind);
		Assert.False(outcome.IsSuccess);
	}

	private static CEObject CreateListFor(LuaState state, string mode)
	{
		return string.Equals(mode, "empty", StringComparison.Ordinal)
			? AobStringListTestHost.CreateEmptyList(state)
			: AobStringListTestHost.CreateList(state);
	}
}
