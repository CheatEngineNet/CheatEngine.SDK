namespace CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

/// <summary>
///     The managed types a generated Lua call shape can push or read: exactly the types <c>CheatEngine.SDK.Lua</c> has a
///     marshaller for. Each value maps to one marshaller (<see cref="LuaValueKinds.MarshallerTypeName" />) and one C#
///     type name (<see cref="LuaValueKinds.TypeName" />).
/// </summary>
/// <remarks>
///     The enum is the vocabulary shared by the LuaBindings generator (which maps <c>ITypeSymbol</c>s to it), the
///     EngineApi generator (which maps spec-file type names to it) and the analyzer that explains unsupported types.
///     It holds no Roslyn type on purpose.
/// </remarks>
internal enum LuaValueKind
{
    /// <summary><see cref="int" /> through <c>Int32Marshaller</c>: a Lua integer that fits 32 bits.</summary>
    Int32,

    /// <summary><see cref="long" /> through <c>Int64Marshaller</c>: a Lua integer.</summary>
    Int64,

    /// <summary><see cref="float" /> through <c>SingleMarshaller</c>: a Lua number.</summary>
    Single,

    /// <summary><see cref="double" /> through <c>DoubleMarshaller</c>: a Lua number.</summary>
    Double,

    /// <summary><see cref="bool" /> through <c>BooleanMarshaller</c>: a Lua boolean (strict; <c>nil</c> is not <c>false</c>).</summary>
    Boolean,

    /// <summary><c>nuint</c> through <c>AddressMarshaller</c>: a target-process address as a Lua integer, bits reinterpreted.</summary>
    Address,

    /// <summary>
    ///     <c>ReadOnlySpan&lt;byte&gt;</c> (UTF-8) through <c>Utf8Marshaller</c>. Arguments only: a span read from a
    ///     result would point into a Lua string the wrapper pops before returning.
    /// </summary>
    Utf8,

    /// <summary><see cref="string" /> through <c>StringMarshaller</c>: the allocating convenience.</summary>
    String
}
