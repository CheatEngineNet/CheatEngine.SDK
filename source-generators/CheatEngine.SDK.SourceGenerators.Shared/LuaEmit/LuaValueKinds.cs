using System;

namespace CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

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
	///     The C# type as generated code spells it: the keyword for the primitives (<see langword="int" />,
	///     <see langword="nuint" />, ...),
	///     <c>global::</c>-qualified for the span, <see langword="string" /> or <see langword="string" />? for text.
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
	///     The <c>global::</c>-qualified <c>LuaOptional&lt;T&gt;</c> spelling of an optional value of
	///     <paramref name="kind" />: <c>global::CheatEngine.SDK.Lua.Marshalling.LuaOptional&lt;long&gt;</c>. The type
	///     argument is never nullable: <c>nil</c> is a state of the optional, not a <see langword="null" /> value.
	/// </summary>
	public static string OptionalTypeName(LuaValueKind kind)
	{
		return LuaApiNames.LuaOptional + "<" + TypeName(kind) + ">";
	}

	/// <summary>
	///     Whether a value of this kind can be a <c>LuaOptional&lt;T&gt;</c> argument or result: every built-in kind
	///     except <see cref="LuaValueKind.Utf8" />, a <c>ReadOnlySpan&lt;byte&gt;</c> that cannot be a type argument.
	/// </summary>
	public static bool CanBeOptional(LuaValueKind kind)
	{
		return kind != LuaValueKind.Utf8;
	}

	/// <summary>
	///     Whether a value of this kind can be the element of a variadic <c>Span&lt;T&gt; values, out int count</c> result:
	///     the unmanaged scalar kinds. <see langword="string" /> and <c>ReadOnlySpan&lt;byte&gt;</c> are not, and a
	///     <c>Span&lt;byte&gt;</c> destination stays the UTF-8 copy-out pair.
	/// </summary>
	public static bool CanBeVariadicElement(LuaValueKind kind)
	{
		return kind is LuaValueKind.Int32 or LuaValueKind.Int64 or LuaValueKind.Single or LuaValueKind.Double
			or LuaValueKind.Boolean or LuaValueKind.Address;
	}

	/// <summary>
	///     The Lua type a thunk expects for an argument of this kind, in Lua's own words
	///     (<c language="lua">integer</c>, <c language="lua">number</c>, <c language="lua">boolean</c>,
	///     <c language="lua">string</c>).
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
