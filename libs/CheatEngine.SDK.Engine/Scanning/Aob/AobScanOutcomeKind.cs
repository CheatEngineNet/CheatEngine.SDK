namespace CheatEngine.SDK.Engine.Scanning.Aob;

/// <summary>The factual outcome of a protected Cheat Engine <c>AOBScan</c> operation.</summary>
/// <remarks>
///     <para>
///         The zero value is <see cref="Unknown" />, so a default outcome is never interpreted as a successful scan.
///     </para>
///     <para>
///         <see cref="NoMatches" /> is deliberately narrower than <see cref="NoResult" />: it is reported only after CE
///         returned a valid <c>StringList</c> and its <c>Count</c> property was read as zero. On the pinned profile
///         <c>ce-7.7.0.10621-x64-managed-hostfxr</c>, <c>AOBScan</c> never returns such a list: zero matches return no
///         value, which the protected one-result call reads as <c>nil</c> and this binding reports as
///         <see cref="NoResult" /> (host observation, spike 2026-09-22; the Q27 C3 receipt is still pending). On that
///         profile, <see cref="NoMatches" /> is therefore unreachable on the global route, and <see cref="NoResult" />
///         means "zero matches or a host failure that also produced no list": the SDK keeps it raw rather than
///         converting it to a no-match classification. Only the bounded MemScan route,
///         <see cref="AobScanner.TryScanWithinBounds(string, AobScanBounds, AobScanOptions, System.Span{CheatEngine.SDK.Engine.Values.Address}, System.Threading.CancellationToken)" />,
///         reports a factual zero on that profile.
///     </para>
///     <para>Categories come from the result's Lua type and arity only, never from a Lua error message.</para>
/// </remarks>
public enum AobScanOutcomeKind
{
	/// <summary>No scan outcome has been observed.</summary>
	Unknown,

	/// <summary>CE returned a valid StringList whose verified count is positive.</summary>
	Matches,

	/// <summary>
	///     CE returned a valid StringList whose verified count is zero. Unreachable on the pinned CE 7.7.0.10621 x64
	///     profile, where zero matches are reported as <see cref="NoResult" />.
	/// </summary>
	NoMatches,

	/// <summary>The required <c>AOBScan</c> global was absent or was not callable.</summary>
	GlobalUnavailable,

	/// <summary>A protected Lua global lookup or invocation failed.</summary>
	ProtectedLuaFailure,

	/// <summary>
	///     CE returned no value or Lua <c>nil</c>; this binding does not reinterpret it as no matches. On the pinned
	///     CE 7.7.0.10621 x64 profile, this is how zero matches are reported.
	/// </summary>
	NoResult,

	/// <summary>CE returned a non-nil value that was not a valid host object.</summary>
	InvalidResult,

	/// <summary>The returned host object did not provide a non-negative StringList count.</summary>
	ResultListCountUnavailable
}
