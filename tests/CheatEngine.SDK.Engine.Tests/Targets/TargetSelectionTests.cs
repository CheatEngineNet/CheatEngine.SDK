using System;
using System.Text;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Targets;

/// <summary>Fixture-level observations for the SDK target-selection and incarnation contract.</summary>
[Trait("Category", "NativeLua")]
public sealed class TargetSelectionTests
{
    [Fact]
    public void ObserveCurrent_with_the_current_process_id_returns_a_qualified_incarnation()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallOpenedProcessId(scope.State, Environment.ProcessId);

        var observation = TargetSelection.ObserveCurrent();

        Assert.Equal(TargetSelectionObservationStatus.CurrentTargetQualified, observation.Status);
        Assert.Equal(Environment.ProcessId, observation.SelectedProcessId);
        Assert.True(observation.IsQualified);
        Assert.True(observation.Incarnation.HasValue);
        Assert.Equal(Environment.ProcessId, observation.Incarnation.Value.ProcessId);
        Assert.Equal(TargetIdentityEvidence.CheatEngineSelectedProcessId | TargetIdentityEvidence.LocalProcessStartTime,
            observation.Evidence);
        Assert.True(TargetSelection.ValidateCurrent(observation.Incarnation.Value).IsCurrent);
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void ObserveCurrent_with_no_Cheat_Engine_target_reports_no_target_without_inventing_an_incarnation()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallOpenedProcessId(scope.State, 0);

        var observation = TargetSelection.ObserveCurrent();

        Assert.Equal(TargetSelectionObservationStatus.NoTargetSelected, observation.Status);
        Assert.Equal(TargetIdentityEvidence.None, observation.Evidence);
        Assert.Null(observation.SelectedProcessId);
        Assert.Null(observation.Incarnation);
        Assert.False(observation.IsQualified);
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void ObserveCurrent_when_the_selection_global_is_unavailable_preserves_that_unavailable_fact()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        TargetProcessIncarnation expected = new(4101, 1001);

        var observation = TargetSelection.ObserveCurrent();
        var check = TargetSelection.ValidateCurrent(expected);

        Assert.Equal(TargetSelectionObservationStatus.GlobalUnavailable, observation.Status);
        Assert.Equal(TargetIdentityEvidence.None, observation.Evidence);
        Assert.Null(observation.SelectedProcessId);
        Assert.Null(observation.Incarnation);
        Assert.Equal(TargetIdentityCheckKind.GlobalUnavailable, check.Kind);
        Assert.False(check.IsCurrent);
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void ObserveCurrent_when_the_selection_callback_raises_preserves_the_Lua_failure_fact()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        EngineTest.Run(scope.State, "function getOpenedProcessID() error('fixture target selection failure') end"u8);

        var observation = TargetSelection.ObserveCurrent();

        Assert.Equal(TargetSelectionObservationStatus.LuaFailure, observation.Status);
        Assert.Equal(TargetIdentityEvidence.None, observation.Evidence);
        Assert.Null(observation.SelectedProcessId);
        Assert.Null(observation.Incarnation);
        Assert.False(observation.IsQualified);
        Assert.Equal(0, scope.State.Top);
    }

    [Theory]
    [InlineData("function getOpenedProcessID() return true end")]
    [InlineData("function getOpenedProcessID() return -1 end")]
    public void ObserveCurrent_when_the_selection_callback_returns_an_invalid_PID_reports_an_invalid_result(string fixture)
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        EngineTest.Run(scope.State, Encoding.UTF8.GetBytes(fixture));

        var observation = TargetSelection.ObserveCurrent();

        Assert.Equal(TargetSelectionObservationStatus.InvalidResult, observation.Status);
        Assert.Equal(TargetIdentityEvidence.None, observation.Evidence);
        Assert.Null(observation.SelectedProcessId);
        Assert.Null(observation.Incarnation);
        Assert.False(observation.IsQualified);
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void ObserveCurrent_when_the_selected_PID_cannot_be_locally_qualified_keeps_the_PID_without_granting_authority()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallOpenedProcessId(scope.State, int.MaxValue);

        var observation = TargetSelection.ObserveCurrent();

        Assert.Equal(TargetSelectionObservationStatus.CurrentTargetUnqualified, observation.Status);
        Assert.Equal(TargetIdentityEvidence.CheatEngineSelectedProcessId, observation.Evidence);
        Assert.Equal(int.MaxValue, observation.SelectedProcessId);
        Assert.Null(observation.Incarnation);
        Assert.False(observation.IsQualified);
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void ValidateCurrent_distinguishes_a_different_PID_from_reuse_of_the_same_PID()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        InstallOpenedProcessId(scope.State, Environment.ProcessId);
        TargetProcessIncarnation changedTarget = new(Environment.ProcessId + 1, 1001);
        TargetProcessIncarnation reusedPid = new(Environment.ProcessId, 1);

        var changed = TargetSelection.ValidateCurrent(changedTarget);
        var reused = TargetSelection.ValidateCurrent(reusedPid);

        Assert.Equal(TargetIdentityCheckKind.TargetChanged, changed.Kind);
        Assert.Equal(Environment.ProcessId, changed.Observed.SelectedProcessId);
        Assert.Equal(TargetIdentityCheckKind.ProcessReused, reused.Kind);
        Assert.Equal(Environment.ProcessId, reused.Observed.SelectedProcessId);
        Assert.False(changed.IsCurrent);
        Assert.False(reused.IsCurrent);
        Assert.Equal(0, scope.State.Top);
    }

    [Fact]
    public void Default_identity_check_is_not_current()
    {
        TargetIdentityCheck check = default;

        Assert.Equal(TargetIdentityCheckKind.Unspecified, check.Kind);
        Assert.False(check.IsCurrent);
    }

    [Fact]
    public void Default_selection_observation_is_not_qualified()
    {
        TargetSelectionObservation observation = default;

        Assert.Equal(TargetSelectionObservationStatus.Unspecified, observation.Status);
        Assert.False(observation.IsQualified);
    }

    private static void InstallOpenedProcessId(CheatEngine.SDK.Lua.State.LuaState state, int processId)
    {
        EngineTest.Run(state, Encoding.UTF8.GetBytes("function getOpenedProcessID() return " + processId + " end"));
    }
}
