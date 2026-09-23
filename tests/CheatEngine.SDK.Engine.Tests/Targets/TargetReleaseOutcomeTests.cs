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
	[InlineData(TargetIdentityCheckKind.RemoteBackend, TargetReleaseStatus.RefusedIdentityUnavailable)]
	[InlineData(TargetIdentityCheckKind.FileAsProcess, TargetReleaseStatus.RefusedIdentityUnavailable)]
	[InlineData(TargetIdentityCheckKind.BackendUnknown, TargetReleaseStatus.RefusedIdentityUnavailable)]
	public void Refused_cleanup_preserves_the_specific_or_unavailable_target_fact(TargetIdentityCheckKind checkKind,
		TargetReleaseStatus expectedStatus)
	{
		TargetSelectionObservationStatus observationStatus = checkKind switch
		{
			TargetIdentityCheckKind.NoTargetSelected => TargetSelectionObservationStatus.NoTargetSelected,
			TargetIdentityCheckKind.GlobalUnavailable => TargetSelectionObservationStatus.GlobalUnavailable,
			TargetIdentityCheckKind.RemoteBackend => TargetSelectionObservationStatus.CurrentTargetRemoteBackend,
			TargetIdentityCheckKind.FileAsProcess => TargetSelectionObservationStatus.CurrentTargetFileAsProcess,
			TargetIdentityCheckKind.BackendUnknown => TargetSelectionObservationStatus.CurrentTargetBackendUnknown,
			_ => TargetSelectionObservationStatus.InvalidResult
		};
		TargetIdentityCheck check = new(checkKind, TargetSelectionObservation.FromStatus(observationStatus));

		TargetReleaseOutcome outcome = TargetReleaseOutcome.Refused(check);

		Assert.Equal(expectedStatus, outcome.Status);
		Assert.Equal(check, outcome.TargetCheck);
		Assert.Equal(checkKind, outcome.TargetCheck?.Kind);
		Assert.Null(outcome.FailureKind);
		Assert.True(outcome.RequiresManualRecovery);
	}

	[Fact]
	public void Unconfirmed_cleanup_requires_manual_recovery_and_preserves_the_boundary_failure_kind()
	{
		TargetReleaseOutcome outcome = TargetReleaseOutcome.Unconfirmed(EngineFailureKind.ProtectedLuaFailure);

		Assert.Equal(TargetReleaseStatus.UnconfirmedAfterInvocation, outcome.Status);
		Assert.Null(outcome.TargetCheck);
		Assert.Equal(EngineFailureKind.ProtectedLuaFailure, outcome.FailureKind);
		Assert.True(outcome.RequiresManualRecovery);
		Assert.False(TargetReleaseOutcome.Released().RequiresManualRecovery);
	}

	[Fact]
	public void Cleanup_that_did_not_begin_requires_manual_recovery_without_target_or_failure_detail()
	{
		TargetReleaseOutcome outcome = TargetReleaseOutcome.NotInvoked();

		Assert.Equal(TargetReleaseStatus.NotInvoked, outcome.Status);
		Assert.Equal((byte) 7, (byte) outcome.Status);
		Assert.Null(outcome.TargetCheck);
		Assert.Null(outcome.FailureKind);
		Assert.True(outcome.RequiresManualRecovery);
	}
}
