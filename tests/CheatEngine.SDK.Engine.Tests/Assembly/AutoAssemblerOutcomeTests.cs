using System.Text;

using CheatEngine.SDK.Engine.Assembly;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Assembly;

/// <summary>
///     <see cref="AutoAssemblerPatcher.TryApplyWithOutcome(string, AutoAssemblerOptions, out AutoAssemblerPatch?)" /> and
///     <see cref="AutoAssemblerPatcher.TryCheck(string, bool, AutoAssemblerOptions)" /> against the Lua double: factual
///     categories and effect states, opt-in bounded host text that never drives a category, and a published patch only
///     after Cheat Engine applied the script.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class AutoAssemblerOutcomeTests
{
	private static readonly AutoAssemblerOptions SCapture = new() { CaptureHostText = true };

	[Fact]
	[Trait("Qualification", "Q35")]
	public void TryApplyWithOutcome_applied_publishes_the_patch_with_its_origin_and_snapshot()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AutoAssemblerTestHost.Install(L);

		AutoAssemblerApplyOutcome outcome =
			AutoAssemblerPatcher.TryApplyWithOutcome("success", out AutoAssemblerPatch? patch);

		Assert.Equal(AutoAssemblerApplyOutcomeKind.Applied, outcome.Kind);
		Assert.Equal(EngineEffectState.Applied, outcome.Effect);
		Assert.True(outcome.HasPatch);
		Assert.Null(outcome.Compensation);
		Assert.True(outcome.TargetObservation.IsQualified);
		Assert.True(outcome.PostEffectTargetCheck.GetValueOrDefault().IsCurrent);
		AutoAssemblerDisableInfoSnapshot
			snapshot = Assert.IsType<AutoAssemblerDisableInfoSnapshot>(outcome.DisableInfo);
		Assert.Equal(AutoAssemblerDisableInfoSnapshotStatus.Complete, snapshot.Status);
		AutoAssemblerPatch owner = Assert.IsType<AutoAssemblerPatch>(patch);
		Assert.Same(snapshot, owner.DisableInfo);
		Assert.Equal(LuaRuntime.CurrentStateIdentity, owner.Origin.Runtime);
		Assert.Equal(outcome.TargetObservation.Incarnation, owner.Origin.Target);
		Assert.Equal(owner.TargetIncarnation, owner.Origin.Target);
		Assert.True(owner.PostApplyTargetCheck.GetValueOrDefault().IsCurrent);
		Assert.Equal(1, AutoAssemblerTestHost.ReadCounter(L, "auto_assembler_apply_arguments"));
		Assert.Equal(0, L.Top);

		Assert.Equal(TargetReleaseStatus.Released, owner.ReleaseWithTargetOutcome().Status);
		Assert.Equal(2, AutoAssemblerTestHost.ReadCounter(L, "auto_assembler_disable_arguments"));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q35")]
	public void TryApplyWithOutcome_rejected_reports_unknown_effect_and_no_owner()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AutoAssemblerTestHost.Install(L);

		AutoAssemblerApplyOutcome outcome =
			AutoAssemblerPatcher.TryApplyWithOutcome("apply-false", out AutoAssemblerPatch? patch);

		Assert.Equal(AutoAssemblerApplyOutcomeKind.Rejected, outcome.Kind);
		Assert.Equal(EngineEffectState.Unknown, outcome.Effect);
		Assert.Null(patch);
		Assert.False(outcome.HasPatch);
		Assert.Null(outcome.DisableInfo);
		Assert.Null(outcome.PostEffectTargetCheck);
		Assert.Null(outcome.Compensation);
		Assert.Equal(0, AutoAssemblerTestHost.ReadCounter(L, "auto_assembler_disable_count"));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void TryApplyWithOutcome_rejected_keeps_a_bounded_diagnostic_only_when_opted_in()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AutoAssemblerTestHost.Install(L);

		AutoAssemblerApplyOutcome silent = AutoAssemblerPatcher.TryApplyWithOutcome("apply-false", out _);
		AutoAssemblerApplyOutcome captured = AutoAssemblerPatcher.TryApplyWithOutcome("apply-false", SCapture, out _);

		Assert.Null(silent.HostText);
		Assert.False(silent.HostTextTruncated);
		Assert.Equal(AutoAssemblerTestHost.RejectionDetail, captured.HostText);
		Assert.False(captured.HostTextTruncated);
		Assert.Equal(silent.Kind, captured.Kind);
		Assert.Equal(silent.Effect, captured.Effect);
		Assert.Equal(0, L.Top);
	}

	[Theory]
	[InlineData("\\195\\169\\195\\169\\195\\169", 5, "éé", true)]
	[InlineData("\\240\\159\\152\\128\\240\\159\\152\\128", 6, "😀", true)]
	[InlineData("\\240\\159\\152\\128\\240\\159\\152\\128", 8, "😀😀", false)]
	[InlineData("a\\0b", 16, "a\0b", false)]
	[InlineData("abcdef", 3, "abc", true)]
	public void TryApplyWithOutcome_diagnostic_is_truncated_at_a_scalar_boundary(string luaEscapedDetail,
		int maxBytes, string expected, bool truncated)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AutoAssemblerTestHost.Install(L);
		EngineTest.Run(L, Encoding.UTF8.GetBytes("aa_long_detail = \"" + luaEscapedDetail + "\""));

		AutoAssemblerApplyOutcome outcome = AutoAssemblerPatcher.TryApplyWithOutcome("apply-reject-long",
			new AutoAssemblerOptions { CaptureHostText = true, MaxHostTextBytes = maxBytes }, out _);

		Assert.Equal(AutoAssemblerApplyOutcomeKind.Rejected, outcome.Kind);
		Assert.Equal(expected, outcome.HostText);
		Assert.Equal(truncated, outcome.HostTextTruncated);
		Assert.DoesNotContain('�', outcome.HostText!);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void TryApplyWithOutcome_non_string_rejection_detail_keeps_the_rejected_category()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AutoAssemblerTestHost.Install(L);

		AutoAssemblerApplyOutcome outcome =
			AutoAssemblerPatcher.TryApplyWithOutcome("apply-reject-nonstring", SCapture, out AutoAssemblerPatch? patch);

		Assert.Equal(AutoAssemblerApplyOutcomeKind.Rejected, outcome.Kind);
		Assert.Equal(EngineEffectState.Unknown, outcome.Effect);
		Assert.Null(outcome.HostText);
		Assert.Null(patch);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q35")]
	public void TryApplyWithOutcome_allocation_impossible_is_rejected_with_unknown_effect()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AutoAssemblerTestHost.Install(L);

		AutoAssemblerCheckOutcome check = AutoAssemblerPatcher.TryCheck("apply-alloc-impossible", true);
		AutoAssemblerApplyOutcome outcome =
			AutoAssemblerPatcher.TryApplyWithOutcome("apply-alloc-impossible", SCapture, out AutoAssemblerPatch? patch);

		Assert.Equal(AutoAssemblerCheckOutcomeKind.Accepted, check.Kind);
		Assert.Equal(AutoAssemblerApplyOutcomeKind.Rejected, outcome.Kind);
		Assert.Equal(EngineEffectState.Unknown, outcome.Effect);
		Assert.Equal("allocation failure for newmem", outcome.HostText);
		Assert.Null(patch);
		Assert.Equal(0, AutoAssemblerTestHost.ReadCounter(L, "auto_assembler_disable_count"));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void TryApplyWithOutcome_keeps_compilation_warnings_and_secondary_results()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AutoAssemblerTestHost.Install(L);
		EngineTest.Run(L, "aa_warnings = 'warning: label never used'"u8);

		AutoAssemblerApplyOutcome silent =
			AutoAssemblerPatcher.TryApplyWithOutcome("apply-warnings", out AutoAssemblerPatch? first);
		AutoAssemblerApplyOutcome captured =
			AutoAssemblerPatcher.TryApplyWithOutcome("apply-warnings", SCapture, out AutoAssemblerPatch? second);
		EngineTest.Run(L, "aa_warnings = { 'not text' }"u8);
		AutoAssemblerApplyOutcome nonString =
			AutoAssemblerPatcher.TryApplyWithOutcome("apply-warnings", SCapture, out AutoAssemblerPatch? third);

		Assert.Equal(AutoAssemblerApplyOutcomeKind.Applied, silent.Kind);
		Assert.True(silent.HasHostWarnings);
		Assert.Null(silent.HostWarnings);
		Assert.True(captured.HasHostWarnings);
		Assert.Equal("warning: label never used", captured.HostWarnings);
		Assert.False(captured.HostWarningsTruncated);
		Assert.True(nonString.HasHostWarnings);
		Assert.Null(nonString.HostWarnings);
		Assert.Equal(AutoAssemblerApplyOutcomeKind.Applied, nonString.Kind);
		Assert.All(new[] { first, second, third }, static patch => Assert.NotNull(patch));
		first!.Dispose();
		second!.Dispose();
		third!.Dispose();
		Assert.Equal(3, AutoAssemblerTestHost.ReadCounter(L, "auto_assembler_disable_count"));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void TryApplyWithOutcome_success_without_warnings_reports_none()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AutoAssemblerTestHost.Install(L);

		AutoAssemblerApplyOutcome outcome =
			AutoAssemblerPatcher.TryApplyWithOutcome("success", SCapture, out AutoAssemblerPatch? patch);

		Assert.False(outcome.HasHostWarnings);
		Assert.Null(outcome.HostWarnings);
		Assert.Null(outcome.HostText);
		patch!.Dispose();
		Assert.Equal(0, L.Top);
	}

	[Theory]
	[InlineData("apply-true-no-table")]
	[InlineData("apply-non-boolean")]
	public void TryApplyWithOutcome_success_without_a_table_is_invalid_result_with_unknown_effect(string script)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AutoAssemblerTestHost.Install(L);

		AutoAssemblerApplyOutcome outcome =
			AutoAssemblerPatcher.TryApplyWithOutcome(script, out AutoAssemblerPatch? patch);

		Assert.Equal(AutoAssemblerApplyOutcomeKind.InvalidResult, outcome.Kind);
		Assert.Equal(EngineEffectState.Unknown, outcome.Effect);
		Assert.Null(patch);
		Assert.Null(outcome.DisableInfo);
		Assert.Null(outcome.Compensation);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q30.b")]
	public void TryApplyWithOutcome_target_changed_after_the_effect_keeps_the_token_and_reports_unknown()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AutoAssemblerTestHost.Install(L);

		AutoAssemblerApplyOutcome outcome =
			AutoAssemblerPatcher.TryApplyWithOutcome("apply-switch-target", out AutoAssemblerPatch? patch);

		Assert.Equal(AutoAssemblerApplyOutcomeKind.AppliedTargetChanged, outcome.Kind);
		Assert.Equal(EngineEffectState.Unknown, outcome.Effect);
		Assert.True(outcome.HasPatch);
		TargetIdentityCheck postCheck = Assert.NotNull(outcome.PostEffectTargetCheck);
		Assert.Equal(TargetIdentityCheckKind.NoTargetSelected, postCheck.Kind);
		AutoAssemblerPatch owner = Assert.IsType<AutoAssemblerPatch>(patch);
		Assert.Equal(Environment.ProcessId, owner.TargetIncarnation.ProcessId);
		Assert.Equal(postCheck, owner.PostApplyTargetCheck);
		Assert.Equal(0, L.Top);

		// The token is kept, bound to the original incarnation: it disables only once that target is current again.
		AutoAssemblerTestHost.SelectTarget(L, Environment.ProcessId);
		Assert.Equal(TargetReleaseStatus.Released, owner.ReleaseWithTargetOutcome().Status);
		Assert.Equal(1, AutoAssemblerTestHost.ReadCounter(L, "auto_assembler_disable_count"));
		Assert.True(AutoAssemblerTestHost.ReadBoolean(L, "auto_assembler_disable_received_info"));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void TryApplyWithOutcome_publication_failure_reports_one_compensation_and_the_snapshot()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AutoAssemblerTestHost.Install(L);
		EngineTest.Run(L, "aa_disable_info = { symbols = { newmem = 0x140000000 } }"u8);

		AutoAssemblerApplyOutcome outcome = AutoAssemblerPatcher.ApplyCore("success", AutoAssemblerOptions.Default,
			AutoAssemblerTestHost.CreateDisableInfo,
			static (_, _, _, _, _) => throw new InvalidOperationException("injected patch publication failure"), false,
			out AutoAssemblerPatch? patch, out Exception? cause);

		Assert.Equal(AutoAssemblerApplyOutcomeKind.HandoffFailed, outcome.Kind);
		Assert.Equal(EngineEffectState.Unknown, outcome.Effect);
		Assert.Null(patch);
		Assert.False(outcome.HasPatch);
		Assert.IsType<InvalidOperationException>(cause);
		Assert.Equal(TargetReleaseStatus.Released, outcome.Compensation.GetValueOrDefault().Status);
		AutoAssemblerDisableInfoSnapshot
			snapshot = Assert.IsType<AutoAssemblerDisableInfoSnapshot>(outcome.DisableInfo);
		Assert.Equal("newmem", Assert.Single(snapshot.Symbols).Name);
		Assert.Equal(1, AutoAssemblerTestHost.ReadCounter(L, "auto_assembler_disable_count"));
		Assert.True(AutoAssemblerTestHost.ReadBoolean(L, "auto_assembler_disable_received_info"));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void TryApplyWithOutcome_missing_global_reports_not_started_and_restores_the_stack()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		FakeHost.InstallQualifiedLocalTarget(L);

		AutoAssemblerApplyOutcome outcome =
			AutoAssemblerPatcher.TryApplyWithOutcome("success", out AutoAssemblerPatch? patch);
		AutoAssemblerCheckOutcome check = AutoAssemblerPatcher.TryCheck("success", true);

		Assert.Equal(AutoAssemblerApplyOutcomeKind.GlobalUnavailable, outcome.Kind);
		Assert.Equal(EngineEffectState.NotStarted, outcome.Effect);
		Assert.Null(patch);
		Assert.Equal(AutoAssemblerCheckOutcomeKind.GlobalUnavailable, check.Kind);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void TryApplyWithOutcome_global_resolution_failure_reports_the_protected_status()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		FakeHost.InstallQualifiedLocalTarget(L);
		EngineTest.Run(L, """
		                  setmetatable(_G, {
		                    __index = function(_, name)
		                      if name == "autoAssemble" or name == "autoAssembleCheck" then error("global lookup failure") end
		                    end
		                  })
		                  """u8);

		AutoAssemblerApplyOutcome outcome = AutoAssemblerPatcher.TryApplyWithOutcome("success", out _);
		AutoAssemblerCheckOutcome check = AutoAssemblerPatcher.TryCheck("success", true);

		Assert.Equal(AutoAssemblerApplyOutcomeKind.ProtectedLuaFailure, outcome.Kind);
		Assert.Equal(LuaStatus.RuntimeError, outcome.LuaStatus);
		Assert.Equal(EngineEffectState.Unknown, outcome.Effect);
		Assert.Equal(AutoAssemblerCheckOutcomeKind.ProtectedLuaFailure, check.Kind);
		Assert.Equal(LuaStatus.RuntimeError, check.LuaStatus);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void TryApplyWithOutcome_unqualified_target_reports_not_started_before_any_call()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AutoAssemblerTestHost.Install(L);
		AutoAssemblerTestHost.SelectTarget(L, 0);

		AutoAssemblerApplyOutcome outcome =
			AutoAssemblerPatcher.TryApplyWithOutcome("success", out AutoAssemblerPatch? patch);

		Assert.Equal(AutoAssemblerApplyOutcomeKind.TargetIdentityUnavailable, outcome.Kind);
		Assert.Equal(EngineEffectState.NotStarted, outcome.Effect);
		Assert.Equal(TargetSelectionObservationStatus.NoTargetSelected, outcome.TargetObservation.Status);
		Assert.Null(patch);
		Assert.Equal(-1, AutoAssemblerTestHost.ReadCounter(L, "auto_assembler_apply_arguments"));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void TryApplyWithOutcome_protected_failure_reports_unknown_effect_and_recovers_on_the_next_call()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AutoAssemblerTestHost.Install(L);

		AutoAssemblerApplyOutcome failed =
			AutoAssemblerPatcher.TryApplyWithOutcome("apply-raise", out AutoAssemblerPatch? none);
		Assert.Equal(0, L.Top);
		AutoAssemblerApplyOutcome recovered =
			AutoAssemblerPatcher.TryApplyWithOutcome("success", out AutoAssemblerPatch? patch);

		Assert.Equal(AutoAssemblerApplyOutcomeKind.ProtectedLuaFailure, failed.Kind);
		Assert.Equal(LuaStatus.RuntimeError, failed.LuaStatus);
		Assert.Equal(EngineEffectState.Unknown, failed.Effect);
		Assert.Null(none);
		Assert.Equal(AutoAssemblerApplyOutcomeKind.Applied, recovered.Kind);
		patch!.Dispose();
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void TryApplyWithOutcome_localized_error_text_never_changes_the_category()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AutoAssemblerTestHost.Install(L);

		AutoAssemblerApplyOutcome english = AutoAssemblerPatcher.TryApplyWithOutcome("apply-false", SCapture, out _);
		AutoAssemblerApplyOutcome french =
			AutoAssemblerPatcher.TryApplyWithOutcome("apply-reject-french", SCapture, out _);

		Assert.Equal(english.Kind, french.Kind);
		Assert.Equal(english.Effect, french.Effect);
		Assert.Equal(english.HasPatch, french.HasPatch);
		Assert.Equal(english.LuaStatus, french.LuaStatus);
		Assert.Equal("rejeté par l'hôte", french.HostText);
		Assert.NotEqual(english.HostText, french.HostText, StringComparer.Ordinal);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void TryApplyWithOutcome_refuses_out_of_range_options_before_any_lua_work()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AutoAssemblerTestHost.Install(L);
		int providerCalls = FakeHost.ProviderCalls;

		Assert.Throws<ArgumentOutOfRangeException>(() => AutoAssemblerPatcher.TryApplyWithOutcome("success",
			new AutoAssemblerOptions { MaxHostTextBytes = 0 }, out _));
		Assert.Throws<ArgumentOutOfRangeException>(() => AutoAssemblerPatcher.TryApplyWithOutcome("success",
			new AutoAssemblerOptions { MaxDisableInfoEntries = 65537 }, out _));
		Assert.Throws<ArgumentOutOfRangeException>(() => AutoAssemblerPatcher.TryCheck("success", true,
			new AutoAssemblerOptions { MaxDisableInfoNameBytes = 4097 }));
		Assert.Throws<ArgumentException>(() => AutoAssemblerPatcher.TryApplyWithOutcome(" ", out _));

		Assert.Equal(providerCalls, FakeHost.ProviderCalls);
		Assert.Equal(-1, AutoAssemblerTestHost.ReadCounter(L, "auto_assembler_apply_arguments"));
	}

	[Fact]
	public void TryCheck_success_and_failure_never_create_an_owner()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AutoAssemblerTestHost.Install(L);
		long destroyedBefore = FakeHost.DestroyedCount(L);

		AutoAssemblerCheckOutcome accepted = AutoAssemblerPatcher.TryCheck("success", true);
		AutoAssemblerCheckOutcome rejected = AutoAssemblerPatcher.TryCheck("check-false", false, SCapture);
		AutoAssemblerCheckOutcome silent = AutoAssemblerPatcher.TryCheck("check-false", false);
		AutoAssemblerCheckOutcome raised = AutoAssemblerPatcher.TryCheck("check-raise", true);
		AutoAssemblerCheckOutcome invalid = AutoAssemblerPatcher.TryCheck("check-non-boolean", true);

		Assert.Equal(AutoAssemblerCheckOutcomeKind.Accepted, accepted.Kind);
		Assert.True(accepted.IsAccepted);
		Assert.Equal(AutoAssemblerCheckOutcomeKind.Rejected, rejected.Kind);
		Assert.Equal("syntax error at line 1", rejected.HostText);
		Assert.Null(silent.HostText);
		Assert.Equal(AutoAssemblerCheckOutcomeKind.ProtectedLuaFailure, raised.Kind);
		Assert.Equal(LuaStatus.RuntimeError, raised.LuaStatus);
		Assert.Equal(AutoAssemblerCheckOutcomeKind.InvalidResult, invalid.Kind);
		Assert.Equal(0, AutoAssemblerTestHost.ReadCounter(L, "auto_assembler_apply_count"));
		Assert.Equal(0, AutoAssemblerTestHost.ReadCounter(L, "auto_assembler_disable_count"));
		Assert.Equal(destroyedBefore, FakeHost.DestroyedCount(L));
		Assert.Equal(0, L.Top);
		Assert.All(typeof(AutoAssemblerCheckOutcome).GetProperties(), static property =>
			Assert.False(typeof(IDisposable).IsAssignableFrom(property.PropertyType), property.Name));
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void TryCheck_passes_exactly_the_script_and_the_enable_flag(bool enable)
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AutoAssemblerTestHost.Install(L);

		_ = AutoAssemblerPatcher.TryCheck("success", enable);

		Assert.Equal(1, AutoAssemblerTestHost.ReadCounter(L, "auto_assembler_check_count"));
		Assert.Equal(2, AutoAssemblerTestHost.ReadCounter(L, "auto_assembler_check_arguments"));
		Assert.Equal(enable, AutoAssemblerTestHost.ReadBoolean(L, "auto_assembler_check_enable"));
		Assert.Equal(0, L.Top);
	}
}
