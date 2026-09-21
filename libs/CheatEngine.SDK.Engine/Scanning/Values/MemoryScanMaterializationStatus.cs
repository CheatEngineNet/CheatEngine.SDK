namespace CheatEngine.SDK.Engine.Scanning.Values;

/// <summary>Classifies a bounded, copied memory-scan result materialization attempt.</summary>
public enum MemoryScanMaterializationStatus : byte
{
    /// <summary>Every result was copied into the caller-supplied destination.</summary>
    Success = 0,

    /// <summary>The scan completed successfully but its initialized found list contains no rows.</summary>
    NoResults = 1,

    /// <summary>The complete result set exceeds the caller-supplied bounded destination; no row was written.</summary>
    DestinationTooSmall = 2,

    /// <summary>Cancellation was observed before a row was copied; the destination remains unchanged.</summary>
    Cancelled = 3,

    /// <summary>The session belongs to a previous Lua runtime attachment or state generation.</summary>
    RuntimeInvalidated = 4,

    /// <summary>The current target cannot be qualified as the session's original target.</summary>
    TargetIdentityUnavailable = 5,

    /// <summary>The current target is not the session's original target incarnation.</summary>
    TargetIdentityMismatch = 6,

    /// <summary>A protected CE operation failed while reading the result set.</summary>
    LuaFailure = 7,

    /// <summary>CE returned a count, address, or value that does not satisfy the declared scan contract.</summary>
    InvalidResult = 8,
}
