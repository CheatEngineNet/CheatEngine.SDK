namespace CheatEngine.SDK.Engine.Scanning.Values;

/// <summary>Classifies a bounded, copied memory-scan result materialization attempt.</summary>
/// <remarks>The zero value is <see cref="Unknown" />; a default value never reads as success.</remarks>
public enum MemoryScanMaterializationStatus : byte
{
	/// <summary>No materialization status has been observed; never produced by a completed copy attempt.</summary>
	Unknown = 0,

	/// <summary>Every result was copied into the caller-supplied destination.</summary>
	Success = 1,

	/// <summary>The scan completed successfully but its initialized found list contains no rows.</summary>
	NoResults = 2,

	/// <summary>The complete result set exceeds the caller-supplied bounded destination; no row was written.</summary>
	DestinationTooSmall = 3,

	/// <summary>Cancellation was observed before a row was copied; the destination remains unchanged.</summary>
	Cancelled = 4,

	/// <summary>The session belongs to a previous Lua runtime attachment or state generation.</summary>
	RuntimeInvalidated = 5,

	/// <summary>The current target cannot be qualified as the session's original target.</summary>
	TargetIdentityUnavailable = 6,

	/// <summary>The current target is not the session's original target incarnation.</summary>
	TargetIdentityMismatch = 7,

	/// <summary>A protected CE operation failed while reading the result set.</summary>
	LuaFailure = 8,

	/// <summary>CE returned a count, address, or value that does not satisfy the declared scan contract.</summary>
	InvalidResult = 9,

	/// <summary>The requested page starts at or beyond a non-empty found-list count.</summary>
	PageStartOutOfRange = 10
}
