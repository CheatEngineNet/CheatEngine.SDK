namespace CheatEngine.SDK.Engine.Scanning.Values;

/// <summary>Records why a memory-scan session can no longer safely continue its previous lifecycle.</summary>
public enum MemoryScanInvalidationReason : byte
{
	/// <summary>No invalidation reason has been recorded.</summary>
	None = 0,

	/// <summary>A protected CE operation began but did not complete with its declared result.</summary>
	ProtectedLuaFailure = 1,

	/// <summary>The runtime attach epoch or Lua-state generation no longer matches the owned CE objects.</summary>
	RuntimeIdentityChanged = 2,

	/// <summary>The selected target differs from the target incarnation observed when the session was created.</summary>
	TargetChanged = 3,

	/// <summary>The observed target reused the original process identifier with a different start time.</summary>
	TargetProcessReused = 4,

	/// <summary>
	///     The caller asked the session to stop its running scan; the scan's results are never exposed. The termination
	///     status that the request returned tells whether CE was asked at all
	///     (<see cref="MemoryScanTerminationStatus.NotInvoked" /> when the session's context was refused and no CE call was
	///     made) and whether a cooperative stop was confirmed.
	/// </summary>
	ScanTerminated = 5
}
