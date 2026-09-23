using System.Diagnostics.CodeAnalysis;

using CheatEngine.SDK.Engine.Scanning.Aob;
using CheatEngine.SDK.Engine.Scanning.Values;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

// The first-found route is gated by CESDK5011 on purpose; these tests opt in for this file only.
#pragma warning disable CESDK5011

namespace CheatEngine.SDK.Engine.Tests.Scanning.Aob;

/// <summary>
///     The separately named first-found opt-in (audit A13-07, F07; spike D4.6): <c>OnlyOneResult</c> on, the found list
///     never initialized, <c>getOnlyResult</c> read with no argument and one result, only a Lua integer accepted as an
///     address, a match below the start reported as indeterminate, and the session released once on every path.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class AobFirstFoundScanTests
{
	private const string Pattern = "48 83 EC ??";

	private static readonly AobScanBounds ModuleBounds = Bounds(0x1_0000_0000, 0x1_0036_7000);

	[Fact]
	[Trait("Qualification", "Q28")]
	public void TryFindFirstFoundWithinBounds_enables_only_one_result_and_never_initializes_the_found_list()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		MemScanTestHost.Run(L, "only_result_mode = 'value'; only_result_value = 0x100003590");

		AobFirstFoundResult result = Find(ModuleBounds);

		Assert.Equal(AobFirstFoundOutcomeKind.Found, result.Kind);
		Assert.Equal(
			"factory.scan,factory.list,scan.setOnlyOneResult:true,scan.first:14,scan.wait,scan.getOnlyResult," +
			"list.destroy,scan.destroy",
			MemScanTestHost.ReadTrace(L));
		MemScanTestHost.AssertLua(L, "set_only_one_argument_count == 1 and only_result_argument_count == 0");
		MemScanTestHost.AssertLua(L, "first_scan_args[2] == 8 and first_scan_args[6] == 0x100000000");
		Assert.Equal(MemoryScanTerminationStatus.NotRequired, result.Release.Termination);
		Assert.Equal(TargetReleaseStatus.Released, result.Release.FoundList.Status);
		Assert.Equal(TargetReleaseStatus.Released, result.Release.MemScan.Status);
		Assert.Equal(0, L.Top);
	}

	[Theory]
	[Trait("Qualification", "Q28")]
	[InlineData("none")]
	[InlineData("nil")]
	public void TryFindFirstFoundWithinBounds_zero_values_from_get_only_result_is_not_found(string mode)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		MemScanTestHost.Run(L, "only_result_mode = '" + mode + "'");

		AobFirstFoundResult result = Find(ModuleBounds);

		Assert.Equal(AobFirstFoundOutcomeKind.NotFound, result.Kind);
		Assert.False(result.HasAddress);
		Assert.Equal(Address.Zero, result.Address);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	public void TryFindFirstFoundWithinBounds_integer_inside_bounds_is_found()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		MemScanTestHost.Run(L, "only_result_mode = 'value'; only_result_value = 0x100366FFF");

		AobFirstFoundResult result = Find(ModuleBounds);

		Assert.Equal(AobFirstFoundOutcomeKind.Found, result.Kind);
		Assert.True(result.HasAddress);
		Assert.Equal(new Address(0x1_0036_6FFF), result.Address);
		Assert.Equal(LuaStatus.Ok, result.LuaStatus);
		Assert.True(result.HostScanElapsed >= TimeSpan.Zero);
		Assert.Equal(0, L.Top);
	}

	[Theory]
	[Trait("Qualification", "Q28")]
	[InlineData(0x1_0000_0FFFUL)]
	[InlineData(0x1_0000_0000UL)]
	public void TryFindFirstFoundWithinBounds_address_below_start_is_indeterminate_found_outside_bounds(ulong reported)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		MemScanTestHost.Run(L, "only_result_mode = 'value'; only_result_value = " + reported);

		AobFirstFoundResult result = Find(Bounds(0x1_0000_1000, 0x1_0036_7000));

		Assert.Equal(AobFirstFoundOutcomeKind.FoundOutsideBounds, result.Kind);
		Assert.True(result.HasAddress);
		Assert.Equal(new Address(reported), result.Address);
		Assert.Equal(0, L.Top);
	}

	[Theory]
	[Trait("Qualification", "Q28")]
	[InlineData("3.0")]
	[InlineData("'1000'")]
	[InlineData("true")]
	[InlineData("{}")]
	public void TryFindFirstFoundWithinBounds_float_string_or_boolean_result_is_invalid_result(string value)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		MemScanTestHost.Run(L, "only_result_mode = 'value'; only_result_value = " + value);

		AobFirstFoundResult result = Find(Bounds(0, 0x10_0000));

		Assert.Equal(AobFirstFoundOutcomeKind.InvalidResult, result.Kind);
		Assert.False(result.HasAddress);
		Assert.Equal(Address.Zero, result.Address);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	public void TryFindFirstFoundWithinBounds_keeps_the_64_bit_pattern_of_high_addresses()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		MemScanTestHost.Run(L, "only_result_mode = 'value'; only_result_value = math.mininteger");

		AobFirstFoundResult result = Find(Bounds(0x7FFF_FFFF_FFFF_0000, 0xFFFF_FFFF_FFFF_FFFF));

		Assert.Equal(AobFirstFoundOutcomeKind.Found, result.Kind);
		Assert.Equal(new Address(0x8000_0000_0000_0000), result.Address);
		Assert.Equal(0, L.Top);
	}

	[Theory]
	[Trait("Qualification", "Q28")]
	[InlineData("found", AobFirstFoundOutcomeKind.Found, "scan.getOnlyResult,list.destroy,scan.destroy")]
	[InlineData("not-found", AobFirstFoundOutcomeKind.NotFound, "scan.getOnlyResult,list.destroy,scan.destroy")]
	[InlineData("invalid", AobFirstFoundOutcomeKind.InvalidResult, "scan.getOnlyResult,list.destroy,scan.destroy")]
	[InlineData("get-raises", AobFirstFoundOutcomeKind.ScanFailed, "scan.getOnlyResult,list.destroy,scan.destroy")]
	[InlineData("set-raises", AobFirstFoundOutcomeKind.ScanFailed, "scan.setOnlyOneResult:true,list.destroy,scan.destroy")]
	[InlineData("first-raises", AobFirstFoundOutcomeKind.ScanFailed,
		"scan.first:14,scan.terminate:false,scan.wait:5000,list.destroy,scan.destroy")]
	[InlineData("wait-raises", AobFirstFoundOutcomeKind.ScanFailed,
		"scan.wait,scan.terminate:false,scan.wait:5000,list.destroy,scan.destroy")]
	public void TryFindFirstFoundWithinBounds_releases_child_before_parent_on_every_path(string mode,
		AobFirstFoundOutcomeKind expected, string traceEnd)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		MemScanTestHost.Run(L, mode switch
		{
			"found" => "only_result_mode = 'value'; only_result_value = 0x100000000",
			"invalid" => "only_result_mode = 'value'; only_result_value = 'x'",
			"get-raises" => "only_result_mode = 'raise'",
			"set-raises" => "scan_set_only_one_raises = true",
			"first-raises" => "scan_first_raises = true",
			"wait-raises" => "scan_wait_modes = { 'raise', 'true' }",
			_ => "only_result_mode = 'none'"
		});

		AobFirstFoundResult result = Find(ModuleBounds);

		Assert.Equal(expected, result.Kind);
		Assert.EndsWith(traceEnd, MemScanTestHost.ReadTrace(L), StringComparison.Ordinal);
		Assert.DoesNotContain("list.initialize", MemScanTestHost.ReadTrace(L), StringComparison.Ordinal);
		Assert.True(result.Release.OwnershipConsumed);
		Assert.Equal(TargetReleaseStatus.Released, result.Release.FoundList.Status);
		Assert.Equal(TargetReleaseStatus.Released, result.Release.MemScan.Status);
		if (expected == AobFirstFoundOutcomeKind.ScanFailed)
		{
			Assert.Equal(LuaStatus.RuntimeError, result.LuaStatus);
		}

		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	[SuppressMessage("xUnit.Analyzers", "xUnit1051",
		Justification = "The token is cancelled on purpose to prove that no session is created.")]
	public void TryFindFirstFoundWithinBounds_refuses_invalid_bounds_and_a_cancelled_token_before_any_CE_call()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		MemScanTestHost.ClearTrace(L);
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();

		AobFirstFoundResult invalid = Find(default);
		AobFirstFoundResult cancelled =
			AobScanner.TryFindFirstFoundWithinBounds(Pattern, ModuleBounds, AobScanOptions.Default, cancellation.Token);

		Assert.Equal(AobFirstFoundOutcomeKind.InvalidBounds, invalid.Kind);
		Assert.Equal(AobFirstFoundOutcomeKind.Cancelled, cancelled.Kind);
		Assert.Equal(MemoryScanCreationStatus.Unknown, cancelled.Creation.Status);
		Assert.Throws<ArgumentNullException>(() => AobScanner.TryFindFirstFoundWithinBounds(null!, ModuleBounds,
			AobScanOptions.Default, TestContext.Current.CancellationToken));
		Assert.Equal(string.Empty, MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	public void TryFindFirstFoundWithinBounds_without_a_qualified_target_reports_session_creation_failed()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		MemScanTestHost.Run(L, "opened_process_id = 0");
		MemScanTestHost.ClearTrace(L);

		AobFirstFoundResult result = Find(ModuleBounds);

		Assert.Equal(AobFirstFoundOutcomeKind.SessionCreationFailed, result.Kind);
		Assert.Equal(MemoryScanCreationStatus.TargetIdentityUnavailable, result.Creation.Status);
		Assert.Equal(string.Empty, MemScanTestHost.ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q30.a")]
	public void TryFindFirstFoundWithinBounds_target_change_during_the_scan_reports_no_address()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		_ = MemScanTestHost.Install(L);
		MemScanTestHost.Run(L, "only_result_mode = 'value'; only_result_value = 0x100000000; " +
							   "scan_first_hook = function() opened_process_id = " +
							   MemScanTestHost.FindOtherQualifiedProcessId() + " end");

		AobFirstFoundResult result = Find(ModuleBounds);

		Assert.Equal(AobFirstFoundOutcomeKind.TargetChanged, result.Kind);
		Assert.False(result.HasAddress);
		Assert.Equal(TargetReleaseStatus.RefusedTargetChanged, result.Release.MemScan.Status);
		Assert.DoesNotContain("scan.getOnlyResult", MemScanTestHost.ReadTrace(L), StringComparison.Ordinal);
		Assert.Equal(0, L.Top);
	}

	private static AobFirstFoundResult Find(AobScanBounds bounds)
	{
		return AobScanner.TryFindFirstFoundWithinBounds(Pattern, bounds, AobScanOptions.Default,
			TestContext.Current.CancellationToken);
	}

	private static AobScanBounds Bounds(ulong start, ulong stop)
	{
		Assert.True(AobScanBounds.TryCreate(new Address(start), new Address(stop), out AobScanBounds bounds));
		return bounds;
	}
}
