namespace CheatEngine.SDK.Engine.Scanning.Values;

/// <summary>
///     Classifies one deadline-bounded wait for a running memory scan (<c>MemScan.waitTillDone(timeout)</c>).
/// </summary>
/// <remarks>
///     The zero value is <see cref="Unknown" />; a default value never reads as success. Only
///     <see cref="Completed" /> makes results readable. <see cref="TimedOut" /> is the call deadline expiring: the scan
///     may still be running and the session stays <see cref="MemoryScanState.Scanning" />. Every other failure leaves
///     the session <see cref="MemoryScanState.Invalidated" /> or refuses before any CE call; none is derived from Lua
///     error text.
/// </remarks>
public enum MemoryScanWaitStatus : byte
{
	/// <summary>No wait status has been observed; never produced by a completed call.</summary>
	Unknown = 0,

	/// <summary>CE reported completion before the deadline and the attached found list was initialized for reading.</summary>
	Completed = 1,

	/// <summary>
	///     The call deadline expired before CE reported completion. The scan may still be running; the found list was
	///     not initialized and the session is still scanning.
	/// </summary>
	TimedOut = 2,

	/// <summary>The protected wait call raised; the session is invalidated.</summary>
	LuaFailure = 3,

	/// <summary>
	///     The wait returned something other than one boolean (<c>nil</c>, no value, a number or a string); the session
	///     is invalidated.
	/// </summary>
	InvalidResult = 4,

	/// <summary>
	///     CE reported completion, then initializing the found list for reading raised; the session is invalidated and
	///     never exposes results.
	/// </summary>
	InitializationFailed = 5,

	/// <summary>The session belongs to an earlier Lua runtime attachment or state generation; no CE call was made.</summary>
	RuntimeInvalidated = 6,

	/// <summary>The current target could not be qualified as the session's original target; no CE call was made.</summary>
	TargetIdentityUnavailable = 7,

	/// <summary>The current target is not the session's original target incarnation; no CE call was made.</summary>
	TargetIdentityMismatch = 8
}
