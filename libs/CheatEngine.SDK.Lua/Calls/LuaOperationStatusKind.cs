namespace CheatEngine.SDK.Lua.Calls;

/// <summary>The factual outcome of one generated, protected Lua global call.</summary>
/// <remarks>
///     This describes the binding boundary, not application policy. In particular, <see cref="NilResult" /> is the raw
///     Lua <c>nil</c> result and is not a claim that a domain object was not found. The zero value is
///     <see cref="Unknown" />, never <see cref="Success" />: a status that was never assigned cannot read as success.
///     The numeric values are part of the contract and never change.
/// </remarks>
public enum LuaOperationStatusKind
{
	/// <summary>
	///     No binding outcome was recorded: the value of <c>default(LuaOperationStatus)</c>, or an operation that made no
	///     binding call. Never success.
	/// </summary>
	Unknown = 0,

	/// <summary>The global call and every declared result conversion succeeded.</summary>
	Success = 1,

	/// <summary>The required global was absent or was not a callable Lua function.</summary>
	GlobalUnavailable = 2,

	/// <summary>A protected Lua operation failed.</summary>
	LuaFailure = 3,

	/// <summary>The call completed but returned Lua <c>nil</c> where the declaration requires a value.</summary>
	NilResult = 4,

	/// <summary>The call completed but returned a non-nil value the declared marshaller cannot represent.</summary>
	InvalidResult = 5,

	/// <summary>The Lua stack could not grow enough to begin the declared call.</summary>
	StackUnavailable = 6,

	/// <summary>
	///     The call completed but returned fewer values than the declaration requires. Only a declaration that reads the
	///     factual result count (an optional or variadic result) can tell this apart from <see cref="NilResult" />: Lua
	///     pads a fixed-count call with <c>nil</c>.
	/// </summary>
	MissingResult = 7,

	/// <summary>
	///     The call completed but returned more variadic values than the destination span can hold. Nothing was copied;
	///     the count output holds the number of values Lua returned.
	/// </summary>
	ResultCapacityExceeded = 8
}
