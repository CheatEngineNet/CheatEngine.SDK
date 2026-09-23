namespace CheatEngine.SDK.Engine.Scanning.Aob;

/// <summary>Describes the outcome of a protected Cheat Engine <c>AOBScan</c> call.</summary>
/// <remarks>
///     <para>
///         The zero value is <see cref="Unknown" />; a default value never reads as success. <see cref="Success" /> is
///         the only value that supplies an <see cref="CheatEngine.SDK.Engine.Objects.Owned{T}" /> result. A valid,
///         caller-owned <c>StringList</c> with zero entries is still <see cref="Success" />: this binding does not
///         reinterpret an empty list as a failed scan or a match classification.
///     </para>
///     <para>
///         <see cref="NoResult" /> is the raw, factual category for a call that produced no host object. On the pinned
///         profile <c>ce-7.7.0.10621-x64-managed-hostfxr</c>, <c>AOBScan</c> reports zero matches this way: it returns
///         no value, which the one-result protected call reads as <c>nil</c> (host observation, spike 2026-09-22; the
///         Q27 C3 receipt is still pending). A <c>nil</c> can also come from other host paths, so the SDK never turns it
///         into a no-match classification.
///     </para>
/// </remarks>
public enum AobScanStatus
{
	/// <summary>No scan status has been observed; never produced by a completed call.</summary>
	Unknown = 0,

	/// <summary>Cheat Engine returned a caller-owned StringList host object.</summary>
	Success = 1,

	/// <summary>The required <c>AOBScan</c> global was absent or was not callable.</summary>
	GlobalUnavailable = 2,

	/// <summary>The protected global lookup, argument push, or Lua call failed.</summary>
	LuaFailure = 3,

	/// <summary>
	///     Cheat Engine returned no value or Lua <c>nil</c>. On the pinned CE 7.7.0.10621 x64 profile this is how zero
	///     matches are reported; the category stays raw because <c>nil</c> can also come from a host failure.
	/// </summary>
	NoResult = 4,

	/// <summary>Cheat Engine returned a non-nil value that was not a host object.</summary>
	InvalidResult = 5
}
