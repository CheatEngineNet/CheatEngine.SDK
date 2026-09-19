using System;

namespace CESDK.SourceGenerators.Shared.LuaEmit;

/// <summary>
///     What the emitters need to know about each <see cref="LuaValueKind" />: the marshaller, the C# spelling and the
///     words of the error messages.
/// </summary>
/// <remarks>
///     The wording follows the runtime: <c>LuaThunk.FailBadArgument</c> composes
///     <c>
///         bad argument #n (integer
///         expected, got nil)
///     </c>
///     from <see cref="ExpectedArgument" />, and <c>LuaCallSupport.ThrowUnexpectedResult</c>
///     composes <c>The Lua global 'x' returned a nil value, not an integer.</c> from <see cref="ExpectedResult" />.
/// </remarks>
internal static class LuaValueKinds
{
    /// <summary>The <c>global::</c>-qualified marshaller that pushes and reads values of <paramref name="kind" />.</summary>
    public static string MarshallerTypeName(LuaValueKind kind)
    {
        return kind switch
        {
            LuaValueKind.Int32 => LuaApiNames.Int32Marshaller,
            LuaValueKind.Int64 => LuaApiNames.Int64Marshaller,
            LuaValueKind.Single => LuaApiNames.SingleMarshaller,
            LuaValueKind.Double => LuaApiNames.DoubleMarshaller,
            LuaValueKind.Boolean => LuaApiNames.BooleanMarshaller,
            LuaValueKind.Address => LuaApiNames.AddressMarshaller,
            LuaValueKind.Utf8 => LuaApiNames.Utf8Marshaller,
            LuaValueKind.String => LuaApiNames.StringMarshaller,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    /// <summary>
    ///     The C# type as generated code spells it: the keyword for the primitives (<c>int</c>, <c>nuint</c>, ...),
    ///     <c>global::</c>-qualified for the span, <c>string</c> or <c>string?</c> for text.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <param name="isNullable">
    ///     For <see cref="LuaValueKind.String" />: whether the declaration wrote <c>string?</c>. Ignored
    ///     for the other kinds.
    /// </param>
    public static string TypeName(LuaValueKind kind, bool isNullable = false)
    {
        return kind switch
        {
            LuaValueKind.Int32 => "int",
            LuaValueKind.Int64 => "long",
            LuaValueKind.Single => "float",
            LuaValueKind.Double => "double",
            LuaValueKind.Boolean => "bool",
            LuaValueKind.Address => "nuint",
            LuaValueKind.Utf8 => LuaApiNames.ReadOnlySpanOfByte,
            LuaValueKind.String => isNullable ? "string?" : "string",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    /// <summary>
    ///     The Lua type a thunk expects for an argument of this kind, in Lua's own words (<c>integer</c>, <c>number</c>,
    ///     <c>boolean</c>, <c>string</c>).
    /// </summary>
    public static string ExpectedArgument(LuaValueKind kind)
    {
        return kind switch
        {
            LuaValueKind.Int32 or LuaValueKind.Int64 or LuaValueKind.Address => "integer",
            LuaValueKind.Single or LuaValueKind.Double => "number",
            LuaValueKind.Boolean => "boolean",
            LuaValueKind.Utf8 or LuaValueKind.String => "string",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    /// <summary>
    ///     What a throwing wrapper expected from a result of this kind, with its article (<c>an integer</c>,
    ///     <c>a number</c>, <c>a boolean</c>, <c>a string</c>).
    /// </summary>
    public static string ExpectedResult(LuaValueKind kind)
    {
        return kind switch
        {
            LuaValueKind.Int32 or LuaValueKind.Int64 or LuaValueKind.Address => "an integer",
            LuaValueKind.Single or LuaValueKind.Double => "a number",
            LuaValueKind.Boolean => "a boolean",
            LuaValueKind.Utf8 or LuaValueKind.String => "a string",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    /// <summary>
    ///     Whether a wrapper may return a value of this kind. <see cref="LuaValueKind.Utf8" /> may not: the span would
    ///     point into a Lua string that the wrapper pops before it returns.
    /// </summary>
    public static bool CanBeResult(LuaValueKind kind)
    {
        return kind != LuaValueKind.Utf8;
    }

    /// <summary>
    ///     Whether values of this kind are reference types, whose defaulting in generated code needs <c>default!</c>
    ///     under nullable analysis.
    /// </summary>
    public static bool IsReferenceType(LuaValueKind kind)
    {
        return kind == LuaValueKind.String;
    }
}
