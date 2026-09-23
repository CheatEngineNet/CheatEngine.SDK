namespace CheatEngine.SDK.Engine.Scanning.Aob;

/// <summary>The factual outcome of a bounded, exhaustive AOB scan over a MemScan session.</summary>
/// <remarks>
///     The zero value is <see cref="Unknown" />; a default value never reads as success. Only <see cref="Matches" /> and
///     <see cref="NoMatches" /> publish addresses. Categories come from CE result types, counts and the presence of an
///     error text, never from the content of a Lua or CE error message.
/// </remarks>
public enum AobBoundedScanOutcomeKind
{
	/// <summary>No outcome has been observed.</summary>
	Unknown = 0,

	/// <summary>At least one in-bounds match was copied to the destination.</summary>
	Matches = 1,

	/// <summary>
	///     The scan completed and no returned row lies in the bounds. When CE's error text was read and empty, this is a
	///     factual zero, unlike the global <c>AOBScan</c> route on the pinned profile. When the error text could not be
	///     read, <see cref="AobBoundedScanResult.IsHostErrorTextUnreadable" /> is <see langword="true" /> and a host error
	///     cannot be excluded: check that flag before treating this outcome as "not found".
	/// </summary>
	NoMatches = 2,

	/// <summary>The bounds were empty or inverted; refused before any CE call.</summary>
	InvalidBounds = 3,

	/// <summary>The MemScan/FoundList session could not be created; see <see cref="AobBoundedScanResult.Creation" />.</summary>
	SessionCreationFailed = 4,

	/// <summary>
	///     A protected scan call (<c>setOnlyOneResult</c>, <c>firstScan</c>, the wait, <c>initialize</c>, the count or a
	///     row read) raised; see <see cref="AobBoundedScanResult.LuaStatus" />.
	/// </summary>
	ScanFailed = 5,

	/// <summary>
	///     The call deadline expired before CE reported completion; the SDK requested one cooperative stop, reported in
	///     <see cref="AobBoundedScanResult.Termination" />.
	/// </summary>
	WaitTimedOut = 6,

	/// <summary>
	///     The scan completed with no in-bounds row while CE reported a non-empty error text; the text is copied, bounded
	///     and unparsed, into <see cref="AobBoundedScanResult.HostErrorText" />.
	/// </summary>
	HostReportedError = 7,

	/// <summary>CE returned a malformed wait result, count or row address.</summary>
	InvalidResult = 8,

	/// <summary>The selected target is no longer the incarnation the session was created for; nothing was published.</summary>
	TargetChanged = 9,

	/// <summary>The selected target could not be qualified during the scan; nothing was published.</summary>
	TargetIdentityUnavailable = 10,

	/// <summary>The Lua runtime identity changed during the scan; nothing was published.</summary>
	RuntimeInvalidated = 11,

	/// <summary>Cancellation was observed between CE calls; nothing was published.</summary>
	Cancelled = 12
}
