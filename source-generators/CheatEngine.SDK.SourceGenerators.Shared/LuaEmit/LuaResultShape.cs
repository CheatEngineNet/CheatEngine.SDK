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
    CopyOut
}
