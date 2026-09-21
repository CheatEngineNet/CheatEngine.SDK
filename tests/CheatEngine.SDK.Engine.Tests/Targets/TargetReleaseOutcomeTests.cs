using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Targets;

namespace CheatEngine.SDK.Engine.Tests.Targets;

/// <summary>Outcome-contract tests for target-bound cleanup refusals and uncertainty.</summary>
public sealed class TargetReleaseOutcomeTests
{
    [Theory]
    [InlineData(TargetIdentityCheckKind.NoTargetSelected, TargetReleaseStatus.RefusedNoTarget)]
    [InlineData(TargetIdentityCheckKind.GlobalUnavailable, TargetReleaseStatus.RefusedIdentityUnavailable)]
    [InlineData(TargetIdentityCheckKind.InvalidResult, TargetReleaseStatus.RefusedIdentityUnavailable)]
    public void Refused_cleanup_preserves_the_specific_or_unavailable_target_fact(TargetIdentityCheckKind checkKind,
        TargetReleaseStatus expectedStatus)
    {
        var observationStatus = checkKind switch
        {
            TargetIdentityCheckKind.NoTargetSelected => TargetSelectionObservationStatus.NoTargetSelected,
            TargetIdentityCheckKind.GlobalUnavailable => TargetSelectionObservationStatus.GlobalUnavailable,
            _ => TargetSelectionObservationStatus.InvalidResult,
        };
        var check = new TargetIdentityCheck(checkKind, TargetSelectionObservation.FromStatus(observationStatus));

        var outcome = TargetReleaseOutcome.Refused(check);

        Assert.Equal(expectedStatus, outcome.Status);
        Assert.Equal(check, outcome.TargetCheck);
        Assert.Null(outcome.FailureKind);
        Assert.True(outcome.RequiresManualRecovery);
    }

    [Fact]
    public void Unconfirmed_cleanup_requires_manual_recovery_and_preserves_the_boundary_failure_kind()
    {
        var outcome = TargetReleaseOutcome.Unconfirmed(EngineFailureKind.ProtectedLuaFailure);

        Assert.Equal(TargetReleaseStatus.UnconfirmedAfterInvocation, outcome.Status);
        Assert.Null(outcome.TargetCheck);
        Assert.Equal(EngineFailureKind.ProtectedLuaFailure, outcome.FailureKind);
        Assert.True(outcome.RequiresManualRecovery);
        Assert.False(TargetReleaseOutcome.Released().RequiresManualRecovery);
    }

    [Fact]
    public void Cleanup_that_did_not_begin_requires_manual_recovery_without_target_or_failure_detail()
    {
        var outcome = TargetReleaseOutcome.NotInvoked();

        Assert.Equal(TargetReleaseStatus.NotInvoked, outcome.Status);
        Assert.Equal((byte)7, (byte)outcome.Status);
        Assert.Null(outcome.TargetCheck);
        Assert.Null(outcome.FailureKind);
        Assert.True(outcome.RequiresManualRecovery);
    }
}
