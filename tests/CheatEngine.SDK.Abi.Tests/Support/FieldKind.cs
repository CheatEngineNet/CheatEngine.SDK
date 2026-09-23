namespace CheatEngine.SDK.Abi.Tests.Support;

/// <summary>What a field of an ABI structure is, as far as the layout gate is concerned.</summary>
internal enum FieldKind
{
	/// <summary>
	///     A fixed-width or pointer-sized integer (<c>int</c>, <c>uint</c>, <c>long</c>, <c>byte</c>, <c>nuint</c>, an
	///     enumeration).
	/// </summary>
	Integer,

	/// <summary>One of the two ABI booleans, <see cref="Bool32" /> or <see cref="Bool8" />.</summary>
	AbiBoolean,

	/// <summary>A typed data pointer (<c>byte*</c>, <c>uint*</c>, <c>void**</c>).</summary>
	Pointer,

	/// <summary>An unmanaged function pointer (<c>delegate* unmanaged[Stdcall]&lt;…&gt;</c>).</summary>
	FunctionPointer,

	/// <summary>A deliberately untyped <c>void*</c>: a slot the SDK preserves physically but never invokes.</summary>
	OpaquePointer,

	/// <summary>Any other value type embedded by value.</summary>
	Struct
}
