namespace CheatEngine.SDK.Lua.Calls;

/// <summary>The factual outcome of one generated, protected Lua global call.</summary>
/// <remarks>
///     This describes the binding boundary, not application policy. In particular, <see cref="NilResult" /> is the raw
///     Lua <c>nil</c> result and is not a claim that a domain object was not found.
/// </remarks>
public enum LuaOperationStatusKind
{
	/// <summary>The global call and every declared result conversion succeeded.</summary>
	Success,

	/// <summary>The required global was absent or was not a callable Lua function.</summary>
	GlobalUnavailable,

	/// <summary>A protected Lua operation failed.</summary>
	LuaFailure,

	/// <summary>The call completed but returned Lua <c>nil</c> where the declaration requires a value.</summary>
	NilResult,

	/// <summary>The call completed but returned a non-nil value the declared marshaller cannot represent.</summary>
	InvalidResult,

	/// <summary>The Lua stack could not grow enough to begin the declared call.</summary>
	StackUnavailable
}
