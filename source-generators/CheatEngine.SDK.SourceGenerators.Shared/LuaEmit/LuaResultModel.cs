namespace CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

/// <summary>One result of a <c>Try</c>-form wrapper, as it appears in the signature and is read after the call.</summary>
/// <param name="Shape">Value, copy-out, optional value or variadic tail.</param>
/// <param name="Kind">
///     For <see cref="LuaResultShape.Value" /> and <see cref="LuaResultShape.Optional" />: the kind the marshaller reads
///     (never <see cref="LuaValueKind.Utf8" />). For <see cref="LuaResultShape.CopyOut" />:
///     <see cref="LuaValueKind.Int32" />, the type of the <c>written</c> count. For
///     <see cref="LuaResultShape.Variadic" />: the element kind of the span.
/// </param>
/// <param name="Name">
///     The name of the <see langword="out" /> parameter (the value, the <c>written</c> count or the variadic
///     <c>count</c>), keyword-escaped.
/// </param>
/// <param name="IsNullable">For a <see cref="LuaValueKind.String" /> value: the declaration wrote <c>out string?</c>.</param>
/// <param name="DestinationName">
///     For copy-out and variadic: the name of the span parameter that precedes the count; empty otherwise.
/// </param>
/// <param name="DestinationIsScoped">
///     For copy-out: the declaration wrote <c>scoped Span&lt;byte&gt; destination</c>. Reproduced in a wrapper's
///     signature for the same reason as <see cref="LuaArgumentModel.IsScoped" />: <c>Span&lt;byte&gt;</c> is a
///     <c>ref struct</c> passed by value, so a partial method's two declarations must agree on this modifier exactly
///     (CS8988). A value result's <see langword="out" /> parameter has no such requirement (an <see langword="out" />
///     parameter of any type accepts <see langword="scoped" /> without it changing whether the two declarations match), so
///     <see cref="Value" /> does not take it.
/// </param>
/// <param name="CustomMarshaller">
///     An explicit marshaller selected with <c>[LuaMarshaller]</c>, or <see langword="null" /> for the SDK scalar
///     marshaller represented by <paramref name="Kind" />.
/// </param>
internal sealed record LuaResultModel(
	LuaResultShape Shape,
	LuaValueKind Kind,
	string Name,
	bool IsNullable,
	string DestinationName,
	bool DestinationIsScoped = false,
	LuaCustomMarshallerModel? CustomMarshaller = null)
{
	/// <summary>Initializes a built-in scalar result model with the pre-custom-marshaller binary shape.</summary>
	public LuaResultModel(LuaResultShape shape, LuaValueKind kind, string name, bool isNullable,
		string destinationName, bool destinationIsScoped)
		: this(shape, kind, name, isNullable, destinationName, destinationIsScoped, null)
	{
	}

	/// <summary>The concrete marshaller that emitted code calls directly.</summary>
	public string GeneratedMarshallerTypeName =>
		CustomMarshaller?.MarshallerTypeName ?? LuaValueKinds.MarshallerTypeName(Kind);

	/// <summary>
	///     The C# type spelling used in an emitted <see langword="out" /> parameter or local: <c>LuaOptional&lt;T&gt;</c>
	///     for an optional result, the value type for a value result.
	/// </summary>
	public string GeneratedTypeName => Shape == LuaResultShape.Optional
		? LuaValueKinds.OptionalTypeName(Kind)
		: CustomMarshaller?.ValueTypeName ?? LuaValueKinds.TypeName(Kind, IsNullable);

	/// <summary>The Lua-facing expected type in a generated failure message.</summary>
	public string ExpectedResultTypeName => CustomMarshaller?.ExpectedTypeName ?? LuaValueKinds.ExpectedResult(Kind);

	/// <summary>
	///     Whether the generated default assignment needs the null-forgiving operator: only for a value result of a
	///     reference type (an optional result is a struct, a copy-out or variadic count is an <see langword="int" />).
	/// </summary>
	public bool IsReferenceType => Shape == LuaResultShape.Value &&
								   (CustomMarshaller?.IsReferenceType ?? LuaValueKinds.IsReferenceType(Kind));

	/// <summary>Whether the protected call must keep every result and read the factual count.</summary>
	public bool IsDynamic => Shape is LuaResultShape.Optional or LuaResultShape.Variadic;

	/// <summary>A value result: <c>out &lt;type&gt; name</c>.</summary>
	public static LuaResultModel Value(LuaValueKind kind, string name, bool isNullable = false)
	{
		return new LuaResultModel(LuaResultShape.Value, kind, name, isNullable, string.Empty);
	}

	/// <summary>A value result read through an explicitly selected static marshaller.</summary>
	public static LuaResultModel Custom(LuaCustomMarshallerModel marshaller, string name)
	{
		return new LuaResultModel(LuaResultShape.Value, LuaValueKind.Int32, name, false,
			string.Empty, CustomMarshaller: marshaller);
	}

	/// <summary>An optional result: <c>out LuaOptional&lt;T&gt; name</c>.</summary>
	public static LuaResultModel Optional(LuaValueKind kind, string name)
	{
		return new LuaResultModel(LuaResultShape.Optional, kind, name, false, string.Empty);
	}

	/// <summary>A variadic tail: <c>Span&lt;T&gt; values, out int count</c>.</summary>
	public static LuaResultModel Variadic(LuaValueKind elementKind, string valuesName, string countName,
		bool valuesIsScoped = false)
	{
		return new LuaResultModel(LuaResultShape.Variadic, elementKind, countName, false, valuesName,
			valuesIsScoped);
	}

	/// <summary>A copy-out string result: <c>Span&lt;byte&gt; destination, out int written</c>.</summary>
	public static LuaResultModel CopyOut(string destinationName, string writtenName, bool destinationIsScoped = false)
	{
		return new LuaResultModel(
			LuaResultShape.CopyOut,
			LuaValueKind.Int32,
			writtenName,
			IsNullable: false,
			destinationName,
			destinationIsScoped);
	}
}
