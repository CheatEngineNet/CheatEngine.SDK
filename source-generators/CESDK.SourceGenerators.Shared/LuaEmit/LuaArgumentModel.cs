namespace CESDK.SourceGenerators.Shared.LuaEmit;

/// <summary>One argument of a call shape: a managed parameter pushed to Lua (wrapper) or read from Lua (thunk).</summary>
/// <param name="Name">
///     The C# parameter name, keyword-escaped (<c>@string</c>) where needed. A wrapper repeats it in its
///     signature; a thunk does not use it (its locals are numbered).
/// </param>
/// <param name="Kind">The value kind, which selects the marshaller.</param>
/// <param name="IsNullable">
///     For <see cref="LuaValueKind.String" />: the declaration wrote <c>string?</c>. Reproduced in a
///     wrapper's signature so that the partial parts agree.
/// </param>
/// <param name="IsScoped">
///     For <see cref="LuaValueKind.Utf8" /> (the only argument kind of <c>ref struct</c> type): the declaration wrote
///     <c>scoped</c>. Reproduced in a wrapper's signature: a partial method's implementing declaration must repeat a
///     by-value <c>ref struct</c> parameter's <c>scoped</c> modifier exactly, explicit or not (CS8988), even though
///     such a parameter is effectively scoped either way. Meaningless, and always <see langword="false" />, for every
///     other kind: <c>scoped</c> on a by-value parameter of a non-<c>ref struct</c> type does not compile.
/// </param>
/// <param name="FixedValue">
///     A C# expression pushed to Lua without appearing in the managed wrapper signature. Engine API specs use this for
///     host-required flags such as <c>readInteger</c>'s signed-result argument; binding declarations always leave it
///     <see langword="null" />.
/// </param>
internal sealed record LuaArgumentModel(string Name, LuaValueKind Kind, bool IsNullable, bool IsScoped = false,
    string? FixedValue = null)
{
    /// <summary>Whether this value is pushed directly instead of being supplied by a wrapper parameter.</summary>
    public bool IsFixed => FixedValue is not null;
}
