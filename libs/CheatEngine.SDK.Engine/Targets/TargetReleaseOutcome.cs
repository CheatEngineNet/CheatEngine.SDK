using CheatEngine.SDK.Engine.Errors;
using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Engine.Targets;

/// <summary>
///     A copied release result that distinguishes safe refusal, cleanup that could not begin, and an attempted but
///     unconfirmed target operation.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct TargetReleaseOutcome
{
    internal TargetReleaseOutcome(TargetReleaseStatus status, TargetIdentityCheck? targetCheck,
        EngineFailureKind? failureKind)
    {
        Status = status;
        TargetCheck = targetCheck;
        FailureKind = failureKind;
    }

    /// <summary>Gets whether the target-bound cleanup was confirmed.</summary>
    public TargetReleaseStatus Status { get; }

    /// <summary>Gets the target check that refused cleanup, when one occurred.</summary>
    public TargetIdentityCheck? TargetCheck { get; }

    /// <summary>
    ///     Gets the binding failure category after the cleanup crossed the SDK binding boundary. The status records
    ///     whether the target operation could begin; the value is absent when no binding failure is available.
    /// </summary>
    public EngineFailureKind? FailureKind { get; }

    /// <summary>Gets whether a caller must treat the external resource as requiring manual recovery.</summary>
    public bool RequiresManualRecovery => Status is not TargetReleaseStatus.Unspecified and not TargetReleaseStatus.Released;

    internal static TargetReleaseOutcome Released()
    {
        return new TargetReleaseOutcome(TargetReleaseStatus.Released, null, null);
    }

    internal static TargetReleaseOutcome Unconfirmed(EngineFailureKind? failureKind)
    {
        return new TargetReleaseOutcome(TargetReleaseStatus.UnconfirmedAfterInvocation, null, failureKind);
    }

    internal static TargetReleaseOutcome NotInvoked(EngineFailureKind? failureKind = null)
    {
        return new TargetReleaseOutcome(TargetReleaseStatus.NotInvoked, null, failureKind);
    }

    internal static TargetReleaseOutcome Refused(TargetIdentityCheck check)
    {
        return new TargetReleaseOutcome(GetRefusalStatus(check.Kind), check, null);
    }

    private static TargetReleaseStatus GetRefusalStatus(TargetIdentityCheckKind kind)
    {
        return kind switch
        {
            TargetIdentityCheckKind.NoTargetSelected => TargetReleaseStatus.RefusedNoTarget,
            TargetIdentityCheckKind.TargetChanged => TargetReleaseStatus.RefusedTargetChanged,
            TargetIdentityCheckKind.ProcessReused => TargetReleaseStatus.RefusedProcessReused,
            _ => TargetReleaseStatus.RefusedIdentityUnavailable,
        };
    }
}
