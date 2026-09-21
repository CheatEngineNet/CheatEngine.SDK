using System.Reflection;
using System.Text;
using CheatEngine.SDK.Engine.Assembly;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Assembly;

/// <summary>
///     Exercises the SDK Auto Assembler owner against a Lua double that preserves CE's two-call disable-info protocol.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class AutoAssemblerPatcherTests
{
    [Fact]
    public void Apply_on_success_retains_the_disable_info_until_Release_completes()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallAutoAssembler(scope.State);

        var patch = AutoAssemblerPatcher.Apply("success");

        Assert.True(patch.IsEnabled);
        Assert.False(patch.IsDisposed);
        Assert.False(patch.RequiresManualRecovery);
        Assert.Equal(1, ReadCounter(scope.State, "auto_assembler_apply_count"));
        Assert.Equal(0, ReadCounter(scope.State, "auto_assembler_disable_count"));

        patch.Release();

        Assert.True(patch.IsDisposed);
        Assert.False(patch.IsEnabled);
        Assert.False(patch.RequiresManualRecovery);
        Assert.Equal(1, ReadCounter(scope.State, "auto_assembler_disable_count"));
        Assert.True(ReadBoolean(scope.State, "auto_assembler_disable_received_info"));
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void TryApply_when_CE_rejects_the_script_returns_false_without_an_owner()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallAutoAssembler(scope.State);

        var applied = AutoAssemblerPatcher.TryApply("apply-false", out var patch);

        Assert.False(applied);
        Assert.Null(patch);
        Assert.Equal(0, ReadCounter(scope.State, "auto_assembler_apply_count"));
        Assert.Equal(0, ReadCounter(scope.State, "auto_assembler_disable_count"));
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void Apply_when_CE_rejects_the_script_throws_the_stable_expected_failure()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallAutoAssembler(scope.State);

        var exception = Assert.Throws<EngineOperationFailedException>(() => AutoAssemblerPatcher.Apply("apply-false"));

        Assert.Equal("AutoAssemblerApply", exception.Operation);
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void Apply_when_the_protected_CE_call_fails_preserves_the_Lua_failure_and_restores_the_stack()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallAutoAssembler(scope.State);

        var exception = Assert.Throws<EngineLuaException>(() => AutoAssemblerPatcher.Apply("apply-raise"));

        Assert.Equal("AutoAssemblerApply", exception.Operation);
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void TryApply_when_patch_publication_fails_compensates_once_with_the_rooted_disable_info()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallAutoAssembler(scope.State);
        var cause = new InvalidOperationException("injected patch publication failure");

        var exception = Assert.Throws<EngineResourceHandoffException>(() => AutoAssemblerPatcher.TryApplyCore(
            "success", out _, CreateDisableInfo,
            (_, _, _) => throw cause));

        Assert.Same(cause, exception.InnerException);
        Assert.Equal(TargetReleaseStatus.Released, exception.CleanupOutcome.Status);
        Assert.Equal(1, ReadCounter(scope.State, "auto_assembler_apply_count"));
        Assert.Equal(1, ReadCounter(scope.State, "auto_assembler_disable_count"));
        Assert.True(ReadBoolean(scope.State, "auto_assembler_disable_received_info"));
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void TryApply_when_disable_info_tracking_fails_compensates_once_with_the_stack_retained_table()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallAutoAssembler(scope.State);

        var exception = Assert.Throws<EngineResourceHandoffException>(() => AutoAssemblerPatcher.TryApplyCore(
            "success", out _, FailDisableInfoTracking,
            static (_, _, _) => throw new InvalidOperationException("patch factory must not be called")));

        Assert.Equal(TargetReleaseStatus.Released, exception.CleanupOutcome.Status);
        Assert.IsType<EngineLuaException>(exception.InnerException);
        Assert.Equal(1, ReadCounter(scope.State, "auto_assembler_apply_count"));
        Assert.Equal(1, ReadCounter(scope.State, "auto_assembler_disable_count"));
        Assert.True(ReadBoolean(scope.State, "auto_assembler_disable_received_info"));
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void Dispose_after_a_successful_disable_is_idempotent_and_never_replays_disable()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallAutoAssembler(scope.State);
        var patch = AutoAssemblerPatcher.Apply("success");

        patch.Dispose();
        patch.Dispose();

        Assert.True(patch.IsDisposed);
        Assert.False(patch.RequiresManualRecovery);
        Assert.Equal(1, ReadCounter(scope.State, "auto_assembler_disable_count"));
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void ReleaseWithTargetOutcome_after_a_successful_disable_reports_released_and_consumes_the_owner()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallAutoAssembler(scope.State);
        var patch = AutoAssemblerPatcher.Apply("success");

        var outcome = patch.ReleaseWithTargetOutcome();

        Assert.Equal(TargetReleaseStatus.Released, outcome.Status);
        Assert.False(outcome.RequiresManualRecovery);
        Assert.False(patch.RequiresManualRecovery);
        Assert.True(patch.IsDisposed);
        Assert.Equal(outcome, patch.LastReleaseOutcome);
        Assert.Equal(1, ReadCounter(scope.State, "auto_assembler_disable_count"));
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void ReleaseWithTargetOutcome_when_the_current_target_differs_refuses_without_disabling()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallAutoAssembler(scope.State);
        using LuaFrame frame = new(scope.State);
        scope.State.CreateTable();
        var disableInfo = scope.State.CreateRef();
        var originalTargetId = Environment.ProcessId == 1 ? 2 : 1;
        var patch = new AutoAssemblerPatch("success", disableInfo,
            new TargetProcessIncarnation(originalTargetId, 1));

        var outcome = patch.ReleaseWithTargetOutcome();

        Assert.Equal(TargetReleaseStatus.RefusedTargetChanged, outcome.Status);
        Assert.Equal(TargetIdentityCheckKind.TargetChanged, outcome.TargetCheck.GetValueOrDefault().Kind);
        Assert.False(outcome.FailureKind.HasValue);
        Assert.True(patch.IsDisposed);
        Assert.True(patch.RequiresManualRecovery);
        Assert.False(disableInfo.IsResolved);
        Assert.Equal(0, ReadCounter(scope.State, "auto_assembler_disable_count"));
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void ReleaseWithTargetOutcome_when_disable_fails_reports_an_unconfirmed_effect_without_retrying()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallAutoAssembler(scope.State);
        var patch = AutoAssemblerPatcher.Apply("disable-false");
        var disableInfo = GetDisableInfo(patch);

        var outcome = patch.ReleaseWithTargetOutcome();
        patch.Dispose();

        Assert.Equal(TargetReleaseStatus.UnconfirmedAfterInvocation, outcome.Status);
        Assert.Equal(EngineFailureKind.ExpectedOperationFailure, outcome.FailureKind);
        Assert.True(outcome.RequiresManualRecovery);
        Assert.Equal(outcome, patch.LastReleaseOutcome);
        Assert.True(patch.IsDisposed);
        Assert.True(patch.RequiresManualRecovery);
        Assert.False(disableInfo.IsResolved);
        Assert.Equal(1, ReadCounter(scope.State, "auto_assembler_disable_count"));
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void ReleaseWithTargetOutcome_when_disable_raises_reports_the_protected_failure_without_throwing()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallAutoAssembler(scope.State);
        var patch = AutoAssemblerPatcher.Apply("disable-raise");
        var disableInfo = GetDisableInfo(patch);

        var outcome = patch.ReleaseWithTargetOutcome();

        Assert.Equal(TargetReleaseStatus.UnconfirmedAfterInvocation, outcome.Status);
        Assert.Equal(EngineFailureKind.ProtectedLuaFailure, outcome.FailureKind);
        Assert.True(outcome.RequiresManualRecovery);
        Assert.True(patch.IsDisposed);
        Assert.True(patch.RequiresManualRecovery);
        Assert.False(disableInfo.IsResolved);
        Assert.Equal(1, ReadCounter(scope.State, "auto_assembler_disable_count"));
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void ReleaseWithTargetOutcome_when_the_attached_host_cannot_provide_a_state_reports_cleanup_not_invoked()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallAutoAssembler(scope.State);
        var patch = AutoAssemblerPatcher.Apply("success");
        var disableInfo = GetDisableInfo(patch);

        TargetReleaseOutcome outcome;
        using (FakeHost.SuppressStateProvider())
        {
            outcome = patch.ReleaseWithTargetOutcome();
        }

        Assert.Equal(TargetReleaseStatus.NotInvoked, outcome.Status);
        Assert.Null(outcome.FailureKind);
        Assert.True(outcome.RequiresManualRecovery);
        Assert.Equal(outcome, patch.LastReleaseOutcome);
        Assert.True(patch.IsDisposed);
        Assert.True(patch.RequiresManualRecovery);
        Assert.False(disableInfo.IsResolved);
        Assert.Equal(0, ReadCounter(scope.State, "auto_assembler_disable_count"));
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void Release_when_the_attached_host_cannot_provide_a_state_preserves_the_preinvocation_outcome()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallAutoAssembler(scope.State);
        var patch = AutoAssemblerPatcher.Apply("success");
        var disableInfo = GetDisableInfo(patch);

        using (FakeHost.SuppressStateProvider())
        {
            _ = Assert.Throws<InvalidOperationException>(patch.Release);
        }

        Assert.Equal(TargetReleaseStatus.NotInvoked, patch.LastReleaseOutcome.Status);
        Assert.Null(patch.LastReleaseOutcome.FailureKind);
        Assert.True(patch.RequiresManualRecovery);
        Assert.True(patch.IsDisposed);
        Assert.False(disableInfo.IsResolved);
        Assert.Equal(0, ReadCounter(scope.State, "auto_assembler_disable_count"));
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void Release_paths_after_Dispose_throw_without_replaying_disable()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallAutoAssembler(scope.State);
        var patch = AutoAssemblerPatcher.Apply("success");

        patch.Dispose();

        _ = Assert.Throws<ObjectDisposedException>(patch.Release);
        _ = Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = patch.ReleaseWithTargetOutcome();
        });
        Assert.Equal(1, ReadCounter(scope.State, "auto_assembler_disable_count"));
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void Release_when_CE_returns_false_marks_manual_recovery_releases_the_LuaRef_and_never_retries()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallAutoAssembler(scope.State);
        var patch = AutoAssemblerPatcher.Apply("disable-false");
        var disableInfo = GetDisableInfo(patch);

        var exception = Assert.Throws<EngineOperationFailedException>(patch.Release);

        Assert.Equal("AutoAssemblerDisable", exception.Operation);
        Assert.True(patch.IsDisposed);
        Assert.False(patch.IsEnabled);
        Assert.True(patch.RequiresManualRecovery);
        Assert.False(disableInfo.IsResolved);
        Assert.Equal(1, ReadCounter(scope.State, "auto_assembler_disable_count"));

        patch.Dispose();

        Assert.Equal(1, ReadCounter(scope.State, "auto_assembler_disable_count"));
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void TryApply_when_patch_publication_and_compensation_fail_reports_the_unconfirmed_cleanup()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallAutoAssembler(scope.State);
        var cause = new InvalidOperationException("injected patch publication failure");

        var exception = Assert.Throws<EngineResourceHandoffException>(() => AutoAssemblerPatcher.TryApplyCore(
            "disable-false", out _, CreateDisableInfo,
            (_, _, _) => throw cause));

        Assert.Same(cause, exception.InnerException);
        Assert.Equal(TargetReleaseStatus.UnconfirmedAfterInvocation, exception.CleanupOutcome.Status);
        Assert.Equal(EngineFailureKind.ExpectedOperationFailure, exception.CleanupOutcome.FailureKind);
        Assert.True(exception.CleanupOutcome.RequiresManualRecovery);
        Assert.Equal(1, ReadCounter(scope.State, "auto_assembler_apply_count"));
        Assert.Equal(1, ReadCounter(scope.State, "auto_assembler_disable_count"));
        Assert.True(ReadBoolean(scope.State, "auto_assembler_disable_received_info"));
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void TryApply_when_tracking_and_compensation_fail_reports_the_unconfirmed_cleanup()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallAutoAssembler(scope.State);

        var exception = Assert.Throws<EngineResourceHandoffException>(() => AutoAssemblerPatcher.TryApplyCore(
            "disable-raise", out _, FailDisableInfoTracking,
            static (_, _, _) => throw new InvalidOperationException("patch factory must not be called")));

        Assert.IsType<EngineLuaException>(exception.InnerException);
        Assert.Equal(TargetReleaseStatus.UnconfirmedAfterInvocation, exception.CleanupOutcome.Status);
        Assert.Equal(EngineFailureKind.ProtectedLuaFailure, exception.CleanupOutcome.FailureKind);
        Assert.True(exception.CleanupOutcome.RequiresManualRecovery);
        Assert.Equal(1, ReadCounter(scope.State, "auto_assembler_apply_count"));
        Assert.Equal(1, ReadCounter(scope.State, "auto_assembler_disable_count"));
        Assert.True(ReadBoolean(scope.State, "auto_assembler_disable_received_info"));
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void TryApply_when_tracking_fails_and_compensation_returns_false_reports_the_unconfirmed_cleanup()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallAutoAssembler(scope.State);

        var exception = Assert.Throws<EngineResourceHandoffException>(() => AutoAssemblerPatcher.TryApplyCore(
            "disable-false", out _, FailDisableInfoTracking,
            static (_, _, _) => throw new InvalidOperationException("patch factory must not be called")));

        Assert.IsType<EngineLuaException>(exception.InnerException);
        Assert.Equal(TargetReleaseStatus.UnconfirmedAfterInvocation, exception.CleanupOutcome.Status);
        Assert.Equal(EngineFailureKind.ExpectedOperationFailure, exception.CleanupOutcome.FailureKind);
        Assert.True(exception.CleanupOutcome.RequiresManualRecovery);
        Assert.Equal(1, ReadCounter(scope.State, "auto_assembler_apply_count"));
        Assert.Equal(1, ReadCounter(scope.State, "auto_assembler_disable_count"));
        Assert.True(ReadBoolean(scope.State, "auto_assembler_disable_received_info"));
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void Dispose_when_the_disable_call_raises_marks_manual_recovery_releases_the_LuaRef_and_never_retries()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallAutoAssembler(scope.State);
        var patch = AutoAssemblerPatcher.Apply("disable-raise");
        var disableInfo = GetDisableInfo(patch);

        patch.Dispose();
        patch.Dispose();

        Assert.True(patch.IsDisposed);
        Assert.True(patch.RequiresManualRecovery);
        Assert.Equal(TargetReleaseStatus.UnconfirmedAfterInvocation, patch.LastReleaseOutcome.Status);
        Assert.False(disableInfo.IsResolved);
        Assert.Equal(1, ReadCounter(scope.State, "auto_assembler_disable_count"));
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void Release_after_an_external_target_termination_refuses_without_disabling_the_patch()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallAutoAssembler(scope.State);
        var patch = AutoAssemblerPatcher.Apply("success");

        EngineTest.Run(scope.State, "auto_assembler_target_process_id = 0"u8);
        var exception = Assert.Throws<EngineTargetIdentityException>(patch.Release);

        Assert.Equal(TargetIdentityCheckKind.NoTargetSelected, exception.Check.Kind);
        Assert.Equal(TargetReleaseStatus.RefusedNoTarget, patch.LastReleaseOutcome.Status);
        Assert.True(patch.RequiresManualRecovery);
        Assert.Equal(0, ReadCounter(scope.State, "auto_assembler_disable_count"));
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void Dispose_after_disable_and_reenable_does_not_route_a_stale_disable_info_into_the_new_lifecycle()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        AutoAssemblerPatch patch;
        LuaRef disableInfo;
        HostScope firstScope = new(state);
        try
        {
            InstallAutoAssembler(firstScope.State);
            patch = AutoAssemblerPatcher.Apply("success");
            disableInfo = GetDisableInfo(patch);
        }
        finally
        {
            firstScope.Dispose();
        }

        Assert.False(patch.IsEnabled);
        using HostScope secondScope = new(state);

        patch.Dispose();

        Assert.True(patch.IsDisposed);
        Assert.True(patch.RequiresManualRecovery);
        Assert.Equal(TargetReleaseStatus.NotInvoked, patch.LastReleaseOutcome.Status);
        Assert.False(disableInfo.IsResolved);
        Assert.Equal(0, ReadCounter(secondScope.State, "auto_assembler_disable_count"));
        Assert.Equal(0, secondScope.State.Top);
    }

    private static LuaRef GetDisableInfo(AutoAssemblerPatch patch)
    {
        var field = typeof(AutoAssemblerPatch).GetField("_disableInfo",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<LuaRef>(field.GetValue(patch));
    }

    private static LuaRef CreateDisableInfo(LuaState state)
    {
        return state.CreateRef();
    }

    private static LuaRef FailDisableInfoTracking(LuaState _)
    {
        throw new EngineLuaException("AutoAssemblerApply", LuaStatus.MemoryError);
    }

    private static void InstallAutoAssembler(CheatEngine.SDK.Lua.State.LuaState state)
    {
        EngineTest.Run(state, Encoding.UTF8.GetBytes("auto_assembler_target_process_id = " +
                                                     Environment.ProcessId +
                                                     "\nfunction getOpenedProcessID() return auto_assembler_target_process_id end"));
        EngineTest.Run(state, """
                              auto_assembler_apply_count = 0
                              auto_assembler_disable_count = 0
                              auto_assembler_disable_received_info = false

                              autoAssemble = function(script, disableInfo)
                                if disableInfo == nil then
                                  if script == "apply-false" then return false, nil end
                                  if script == "apply-raise" then error("apply failure") end
                                  auto_assembler_apply_count = auto_assembler_apply_count + 1
                                  return true, { sequence = auto_assembler_apply_count }
                                end

                                auto_assembler_disable_count = auto_assembler_disable_count + 1
                                auto_assembler_disable_received_info = type(disableInfo) == "table"
                                if script == "disable-false" then return false end
                                if script == "disable-raise" then error("disable failure") end
                                return true
                              end
                              """u8);
    }

    private static long ReadCounter(CheatEngine.SDK.Lua.State.LuaState state, string name)
    {
        using var frame = new CheatEngine.SDK.Lua.State.LuaFrame(state);
        Assert.True(state.TryGetGlobal(System.Text.Encoding.UTF8.GetBytes(name)).IsOk);
        return EngineTest.ReadInteger(state, -1);
    }

    private static bool ReadBoolean(CheatEngine.SDK.Lua.State.LuaState state, string name)
    {
        using var frame = new CheatEngine.SDK.Lua.State.LuaFrame(state);
        Assert.True(state.TryGetGlobal(System.Text.Encoding.UTF8.GetBytes(name)).IsOk);
        Assert.Equal(CheatEngine.SDK.Lua.State.LuaType.Boolean, state.TypeOf(-1));
        return state.ToBoolean(-1);
    }
}
