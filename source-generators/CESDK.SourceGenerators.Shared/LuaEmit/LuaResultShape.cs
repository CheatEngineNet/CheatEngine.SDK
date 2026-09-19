namespace CESDK.SourceGenerators.Shared.LuaEmit;

/// <summary>How a <c>Try</c>-form wrapper hands one Lua result back to its caller.</summary>
internal enum LuaResultShape
{
    /// <summary>An <c>out</c> parameter read through the kind's marshaller: <c>out int value</c>, <c>out string value</c>.</summary>
    Value,

    /// <summary>
    ///     A string copied into a caller buffer while it is still on the stack, allocation-free:
    ///     <c>Span&lt;byte&gt; destination, out int written</c> (<c>LuaState.TryCopyUtf8</c>).
    /// </summary>
    CopyOut
}
