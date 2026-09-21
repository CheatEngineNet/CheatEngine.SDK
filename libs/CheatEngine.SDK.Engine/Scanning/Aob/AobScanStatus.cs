namespace CheatEngine.SDK.Engine.Scanning.Aob;

/// <summary>Describes the outcome of a protected Cheat Engine <c>AOBScan</c> call.</summary>
/// <remarks>
///     <see cref="Success" /> is the only value that supplies an
///     <see cref="CheatEngine.SDK.Engine.Objects.Owned{T}" /> result. A
///     <see cref="NoResult" /> is reserved for the documented Lua <c>nil</c> result. A valid, caller-owned
///     <c>StringList</c> with zero entries is still <see cref="Success" />: this binding does not reinterpret an empty
///     list as a failed scan or a match classification.
/// </remarks>
public enum AobScanStatus
{
    /// <summary>Cheat Engine returned a caller-owned StringList host object.</summary>
    Success,

    /// <summary>The required <c>AOBScan</c> global was absent or was not callable.</summary>
    GlobalUnavailable,

    /// <summary>The protected global lookup, argument push, or Lua call failed.</summary>
    LuaFailure,

    /// <summary>Cheat Engine returned Lua <c>nil</c>.</summary>
    NoResult,

    /// <summary>Cheat Engine returned a non-nil value that was not a host object.</summary>
    InvalidResult,
}
