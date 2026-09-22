namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>
///     The stable outcome of an inspection operation against Cheat Engine's Lua API.
/// </summary>
/// <remarks>
///     <see cref="Success" /> is the only outcome that writes a complete result. <see cref="NotFound" /> is an
///     expected value-level absence (a Lua <c>nil</c> from <c>getAddressSafe</c> or <c>getSymbolInfo</c>), while
///     <see cref="GlobalUnavailable" />, <see cref="LuaFailure" /> and <see cref="InvalidResult" /> are binding
///     failures. Callers must not treat a binding failure as an address of zero or an empty collection.
/// </remarks>
public enum InspectionStatus
{
	/// <summary>The operation completed and all returned fields matched their documented Lua shapes.</summary>
	Success,

	/// <summary>The requested symbol or symbol metadata does not exist; this is represented by Lua <c>nil</c>.</summary>
	NotFound,

	/// <summary>The output collection is larger than the caller-supplied destination; no item was written.</summary>
	DestinationTooSmall,

	/// <summary>The required Cheat Engine Lua global is absent or is not a function.</summary>
	GlobalUnavailable,

	/// <summary>The protected Lua call or an argument push failed.</summary>
	LuaFailure,

	/// <summary>The call succeeded but returned a value whose table, field or scalar shape is not the CE 7.7 contract.</summary>
	InvalidResult
}
