namespace CheatEngine.SDK.Engine.Scanning.Aob;

/// <summary>The factual outcome of a first-found AOB scan (CE's <c>OnlyOneResult</c> mode).</summary>
/// <remarks>
///     The zero value is <see cref="Unknown" />; a default value never reads as found. <see cref="Found" /> means
///     "CE stopped on some match in the bounds", never "the only match" or "the lowest match": CE documents the address as
///     the first result found, without an order.
/// </remarks>
public enum AobFirstFoundOutcomeKind
{
	/// <summary>No outcome has been observed.</summary>
	Unknown = 0,

	/// <summary>CE reported a match whose address lies in the bounds; it is some match, not a unique or lowest one.</summary>
	Found = 1,

	/// <summary>CE reported no match: <c>getOnlyResult</c> returned no value or <c>nil</c>.</summary>
	NotFound = 2,

	/// <summary>
	///     CE reported a match below the start bound. Indeterminate: CE's start bound is not byte-exact, so CE may have
	///     stopped on a match just before the range, and whether an in-bounds match exists is unknown.
	/// </summary>
	FoundOutsideBounds = 3,

	/// <summary>The bounds were empty or inverted; refused before any CE call.</summary>
	InvalidBounds = 4,

	/// <summary>The MemScan/FoundList session could not be created; see <see cref="AobFirstFoundResult.Creation" />.</summary>
	SessionCreationFailed = 5,

	/// <summary>A protected scan call raised; see <see cref="AobFirstFoundResult.LuaStatus" />.</summary>
	ScanFailed = 6,

	/// <summary><c>getOnlyResult</c> returned something other than an integer or no value (a float, string or boolean).</summary>
	InvalidResult = 7,

	/// <summary>The selected target is no longer the incarnation the session was created for; no address is reported.</summary>
	TargetChanged = 8,

	/// <summary>The selected target could not be qualified during the scan; no address is reported.</summary>
	TargetIdentityUnavailable = 9,

	/// <summary>The Lua runtime identity changed during the scan; no address is reported.</summary>
	RuntimeInvalidated = 10,

	/// <summary>Cancellation was observed before the scan started; no address is reported.</summary>
	Cancelled = 11
}
