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
