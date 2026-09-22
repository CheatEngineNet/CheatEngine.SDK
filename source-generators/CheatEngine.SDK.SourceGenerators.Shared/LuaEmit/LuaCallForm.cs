namespace CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

/// <summary>The bodies a bound global can get.</summary>
internal enum LuaCallForm
{
	/// <summary>
	///     <see langword="bool" /> return, results as <see langword="out" /> parameters: every failure (unresolved global,
	///     raised call, result
	///     of the wrong kind or <c>nil</c>) is <see langword="false" /> with the results defaulted, through
	///     <c>LuaCallSupport.Fail</c>. Never throws for a Lua-side reason.
	/// </summary>
	Try,

	/// <summary>
	///     <c>LuaOperationStatus</c> return, with results as <see langword="out" /> parameters. It preserves the
	///     factual resolution, protected-call and result-shape cause without allocating error text. This is opt-in;
	///     existing <see cref="Try" /> declarations retain their exact <see langword="bool" /> contract.
	/// </summary>
	Outcome,

	/// <summary>
	///     The result is the return value (or the method is <see langword="void" />): every failure is a <c>LuaException</c>
	///     from
	///     the <c>[DoesNotReturn]</c> helpers of <c>LuaCallSupport</c>, which restore the stack first.
	/// </summary>
	Throwing
}
