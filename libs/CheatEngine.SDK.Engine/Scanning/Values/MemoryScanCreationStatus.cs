namespace CheatEngine.SDK.Engine.Scanning.Values;

/// <summary>Classifies the factual result of creating a plugin-owned memory-scan session.</summary>
/// <remarks>
///     The status distinguishes a documented absent factory result from a protected Lua failure or an invalid host
///     value. It is fixture/source evidence for the creation binding, not a CE live-qualification claim.
/// </remarks>
public enum MemoryScanCreationStatus : byte
{
    /// <summary>The scanner and its distinct found-list child were created and adopted by a session.</summary>
    Success = 0,

    /// <summary>The required CE factory global was absent or was not callable.</summary>
    GlobalUnavailable = 1,

    /// <summary>A protected factory call failed before returning its documented result.</summary>
    LuaFailure = 2,

    /// <summary>The scanner factory returned its documented absent result, <see langword="nil" />.</summary>
    NoScannerResult = 3,

    /// <summary>The scanner factory returned a non-null value that was not a CE host object.</summary>
    InvalidScannerResult = 4,

    /// <summary>The found-list factory returned its documented absent result, <see langword="nil" />.</summary>
    NoFoundListResult = 5,

    /// <summary>The found-list factory returned a non-null value that was not a CE host object.</summary>
    InvalidFoundListResult = 6,

    /// <summary>The found-list factory returned the scanner object, so publishing a second owner was refused.</summary>
    AliasedFoundList = 7,

    /// <summary>A rollback destroy call began but Cheat Engine did not confirm all required cleanup operations.</summary>
    RollbackUnconfirmed = 8,
}
