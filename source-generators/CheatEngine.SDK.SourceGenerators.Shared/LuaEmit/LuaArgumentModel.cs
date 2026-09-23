namespace CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

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
///     For <see cref="LuaValueKind.Utf8" /> (the only argument kind of <see langword="ref" /> <see langword="struct" />
///     type): the declaration wrote
///     <see langword="scoped" />. Reproduced in a wrapper's signature: a partial method's implementing declaration must
///     repeat a
///     by-value <see langword="ref" /> <see langword="struct" /> parameter's <see langword="scoped" /> modifier exactly,
///     explicit or not (CS8988), even though
///     such a parameter is effectively scoped either way. Meaningless, and always <see langword="false" />, for every
///     other kind: <see langword="scoped" /> on a by-value parameter of a non-<see langword="ref" />
///     <see langword="struct" /> type does not compile.
/// </param>
/// <param name="FixedValue">
///     A C# expression pushed to Lua without appearing in the managed wrapper signature. Engine API specs use this for
///     host-required flags such as <c>readInteger</c>'s signed-result argument; binding declarations always leave it
///     <see langword="null" />.
/// </param>
/// <param name="CustomMarshaller">
///     An explicit marshaller selected with <c>[LuaMarshaller]</c>, or <see langword="null" /> for one of the SDK
///     scalar marshallers represented by <paramref name="Kind" />.
/// </param>
/// <param name="IsOptional">
///     The value is a <c>LuaOptional&lt;T&gt;</c> of <paramref name="Kind" />: a wrapper pushes it only when it is not
///     omitted (and pushes <c>nil</c> for <c>Nil</c>), a thunk reads an absent position as omitted. Optional arguments
///     form a trailing run, after every required and fixed argument; <paramref name="Kind" /> is then a built-in kind
///     other than <see cref="LuaValueKind.Utf8" />, <paramref name="IsNullable" /> is <see langword="false" /> and no
///     custom marshaller is set.
/// </param>
internal sealed record LuaArgumentModel(
	string Name,
	LuaValueKind Kind,
	bool IsNullable,
	bool IsScoped = false,
	string? FixedValue = null,
	LuaCustomMarshallerModel? CustomMarshaller = null,
	bool IsOptional = false)
{
	/// <summary>Initializes a built-in scalar argument model with the pre-custom-marshaller binary shape.</summary>
	public LuaArgumentModel(string name, LuaValueKind kind, bool isNullable, bool isScoped, string? fixedValue)
		: this(name, kind, isNullable, isScoped, fixedValue, null)
	{
	}

	/// <summary>Initializes an argument model with the pre-optional binary shape.</summary>
	public LuaArgumentModel(string name, LuaValueKind kind, bool isNullable, bool isScoped, string? fixedValue,
		LuaCustomMarshallerModel? customMarshaller)
		: this(name, kind, isNullable, isScoped, fixedValue, customMarshaller, false)
	{
	}

	/// <summary>Whether this value is pushed directly instead of being supplied by a wrapper parameter.</summary>
	public bool IsFixed => FixedValue is not null;

	/// <summary>The concrete marshaller that emitted code calls directly.</summary>
	public string GeneratedMarshallerTypeName =>
		CustomMarshaller?.MarshallerTypeName ?? LuaValueKinds.MarshallerTypeName(Kind);

	/// <summary>
	///     The C# type spelling used in an emitted parameter or local: <c>LuaOptional&lt;T&gt;</c> for an optional
	///     argument, the value type otherwise.
	/// </summary>
	public string GeneratedTypeName => IsOptional
		? LuaValueKinds.OptionalTypeName(Kind)
		: CustomMarshaller?.ValueTypeName ?? LuaValueKinds.TypeName(Kind, IsNullable);

	/// <summary>The Lua-facing expected type in a generated bad-argument message.</summary>
	public string ExpectedArgumentTypeName =>
		CustomMarshaller?.ExpectedTypeName ?? LuaValueKinds.ExpectedArgument(Kind);

	/// <summary>An optional <c>LuaOptional&lt;T&gt;</c> argument of a built-in kind.</summary>
	public static LuaArgumentModel Optional(string name, LuaValueKind kind)
	{
		return new LuaArgumentModel(name, kind, false, IsOptional: true);
	}
}
