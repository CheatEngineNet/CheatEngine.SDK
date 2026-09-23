namespace CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

/// <summary>How a <c>Try</c>-form wrapper hands one Lua result back to its caller.</summary>
internal enum LuaResultShape
{
	/// <summary>
	///     An <see langword="out" /> parameter read through the kind's marshaller: <see langword="out" />
	///     <see langword="int" /> value, <see langword="out" /> <see langword="string" /> value.
	/// </summary>
	Value,

	/// <summary>
	///     A string copied into a caller buffer while it is still on the stack, allocation-free:
	///     <c>Span&lt;byte&gt; destination</c>, <see langword="out" /> <see langword="int" /> written (
	///     <c>LuaState.TryCopyUtf8</c>).
	/// </summary>
	CopyOut,

	/// <summary>
	///     An <see langword="out" /> <c>LuaOptional&lt;T&gt;</c> after every required result: a position Lua did not
	///     return is <c>Omitted</c>, a <c>nil</c> is <c>Nil</c>, a readable value is present. A declaration with such a
	///     result calls with <c>LUA_MULTRET</c> and reads the factual result count.
	/// </summary>
	Optional,

	/// <summary>
	///     The last result of an Outcome form: <c>Span&lt;T&gt; values, out int count</c>, every value Lua returned after
	///     the fixed and optional results, copied while still on the stack (<c>LuaCallSupport.ReadResults</c>).
	/// </summary>
	Variadic
}
