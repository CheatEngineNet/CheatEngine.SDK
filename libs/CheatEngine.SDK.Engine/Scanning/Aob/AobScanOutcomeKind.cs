namespace CheatEngine.SDK.Engine.Scanning.Aob;

/// <summary>The factual outcome of a protected Cheat Engine <c>AOBScan</c> operation.</summary>
/// <remarks>
///     <see cref="NoMatches" /> is deliberately narrower than <see cref="NoResult" />: it is reported only after
///     CE returned a valid <c>StringList</c> and its <c>Count</c> property was read as zero. A raw Lua <c>nil</c> has
///     no documented no-match meaning for this CE primitive and remains <see cref="NoResult" />. The zero value is
///     <see cref="Unknown" />, so a default outcome is never interpreted as a successful scan.
/// </remarks>
public enum AobScanOutcomeKind
{
    /// <summary>No scan outcome has been observed.</summary>
    Unknown,

    /// <summary>CE returned a valid StringList whose verified count is positive.</summary>
    Matches,

    /// <summary>CE returned a valid StringList whose verified count is zero.</summary>
    NoMatches,

    /// <summary>The required <c>AOBScan</c> global was absent or was not callable.</summary>
    GlobalUnavailable,

    /// <summary>A protected Lua global lookup or invocation failed.</summary>
    ProtectedLuaFailure,

    /// <summary>CE returned Lua <c>nil</c>; this binding does not reinterpret it as no matches.</summary>
    NoResult,

    /// <summary>CE returned a non-nil value that was not a valid host object.</summary>
    InvalidResult,

    /// <summary>The returned host object did not provide a non-negative StringList count.</summary>
    ResultListCountUnavailable,
}
