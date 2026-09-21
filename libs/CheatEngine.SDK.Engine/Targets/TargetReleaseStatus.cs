namespace CheatEngine.SDK.Engine.Targets;

/// <summary>The externally visible result of consuming a target-bound owner.</summary>
public enum TargetReleaseStatus : byte
{
    /// <summary>No release was attempted yet.</summary>
    Unspecified = 0,

    /// <summary>The target operation confirmed its release.</summary>
    Released = 1,

    /// <summary>Cleanup was refused because Cheat Engine has no selected target.</summary>
    RefusedNoTarget = 2,

    /// <summary>Cleanup was refused because a required target-identity fact was unavailable.</summary>
    RefusedIdentityUnavailable = 3,

    /// <summary>Cleanup was refused because another PID is currently selected.</summary>
    RefusedTargetChanged = 4,

    /// <summary>Cleanup was refused because the original PID now denotes another process incarnation.</summary>
    RefusedProcessReused = 5,

    /// <summary>The owner was consumed after a CE operation began but release could not be confirmed.</summary>
    UnconfirmedAfterInvocation = 6,

    /// <summary>The owner was consumed, but cleanup could not begin a target operation.</summary>
    NotInvoked = 7,
}
