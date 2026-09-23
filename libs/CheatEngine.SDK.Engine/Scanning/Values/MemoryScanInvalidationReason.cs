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
	///     A cooperative stop of the running scan was requested; its results are never exposed. Whether the stop was
	///     confirmed is reported by the termination status that the request returned.
	/// </summary>
	ScanTerminated = 5
}
