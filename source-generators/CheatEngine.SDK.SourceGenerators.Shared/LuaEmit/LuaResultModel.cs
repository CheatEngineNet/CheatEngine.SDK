namespace CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

/// <summary>One result of a <c>Try</c>-form wrapper, as it appears in the signature and is read after the call.</summary>
/// <param name="Shape">Value or copy-out.</param>
/// <param name="Kind">
///     For <see cref="LuaResultShape.Value" />: the kind the marshaller reads (never <see cref="LuaValueKind.Utf8" />).
///     For <see cref="LuaResultShape.CopyOut" />: <see cref="LuaValueKind.Int32" />, the type of the <c>written</c> count.
/// </param>
/// <param name="Name">
///     The name of the <see langword="out" /> parameter (the value, or the <c>written</c> count),
///     keyword-escaped.
/// </param>
/// <param name="IsNullable">For a <see cref="LuaValueKind.String" /> value: the declaration wrote <c>out string?</c>.</param>
/// <param name="DestinationName">
///     For copy-out: the name of the <c>Span&lt;byte&gt;</c> parameter that precedes the count;
///     empty otherwise.
/// </param>
/// <param name="DestinationIsScoped">
///     For copy-out: the declaration wrote <c>scoped Span&lt;byte&gt; destination</c>. Reproduced in a wrapper's
///     signature for the same reason as <see cref="LuaArgumentModel.IsScoped" />: <c>Span&lt;byte&gt;</c> is a
///     <c>ref struct</c> passed by value, so a partial method's two declarations must agree on this modifier exactly
///     (CS8988). A value result's <see langword="out" /> parameter has no such requirement (an <see langword="out" />
///     parameter of any type accepts <see langword="scoped" /> without it changing whether the two declarations match), so
///     <see cref="Value" /> does not take it.
/// </param>
internal sealed record LuaResultModel(
    LuaResultShape Shape,
    LuaValueKind Kind,
    string Name,
    bool IsNullable,
    string DestinationName,
    bool DestinationIsScoped = false)
{
    /// <summary>A value result: <c>out &lt;type&gt; name</c>.</summary>
    public static LuaResultModel Value(LuaValueKind kind, string name, bool isNullable = false)
    {
        return new LuaResultModel(LuaResultShape.Value, kind, name, isNullable, string.Empty);
    }

    /// <summary>A copy-out string result: <c>Span&lt;byte&gt; destination, out int written</c>.</summary>
    public static LuaResultModel CopyOut(string destinationName, string writtenName, bool destinationIsScoped = false)
    {
        return new LuaResultModel(
            LuaResultShape.CopyOut,
            LuaValueKind.Int32,
            writtenName,
            false,
            destinationName,
            destinationIsScoped);
    }
}
