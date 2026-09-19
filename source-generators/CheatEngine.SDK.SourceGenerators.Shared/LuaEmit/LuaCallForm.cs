namespace CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

/// <summary>The two bodies a bound global can get.</summary>
internal enum LuaCallForm
{
    /// <summary>
    ///     <c>bool</c> return, results as <c>out</c> parameters: every failure (unresolved global, raised call, result
    ///     of the wrong kind or <c>nil</c>) is <see langword="false" /> with the results defaulted, through
    ///     <c>LuaCallSupport.Fail</c>. Never throws for a Lua-side reason.
    /// </summary>
    Try,

    /// <summary>
    ///     The result is the return value (or the method is <c>void</c>): every failure is a <c>LuaException</c> from
    ///     the <c>[DoesNotReturn]</c> helpers of <c>LuaCallSupport</c>, which restore the stack first.
    /// </summary>
    Throwing
}
