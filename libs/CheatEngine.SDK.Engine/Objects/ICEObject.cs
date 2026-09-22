namespace CheatEngine.SDK.Engine.Objects;

/// <summary>
///     The shape of a borrowed handle to a Cheat Engine object: a <see langword="readonly" /> <see langword="struct" />
///     that wraps the untyped <see cref="CEObject" /> and can be made from one. <see cref="CEObject" /> itself implements
///     it; the typed wrappers of Cheat Engine's classes (<c>MemScan</c>, <c>FoundList</c>, <c>AddressList</c>, ...)
///     will too, so that <see cref="Owned{T}" /> and generic readers work for every class the same way.
/// </summary>
/// <typeparam name="TSelf">The implementing struct.</typeparam>
/// <remarks>
///     <para>
///         A handle owns nothing and can be copied freely; whoever holds an <see cref="Owned{T}" /> owns the object. A
///         handle is a pointer value: it does not know whether the object still exists, and no member of it can tell.
///     </para>
///     <para>
///         <see cref="FromHandle" /> performs no check: it wraps the pointer and nothing else, so that a value read from
///         the
///         Lua stack (<see cref="CEObject.TryRead" />) can be typed without another native call. Whether the object really
///         is
///         of the wrapped class is the caller's knowledge, taken from the API that produced the value.
///     </para>
/// </remarks>
public interface ICEObject<TSelf>
	where TSelf : struct, ICEObject<TSelf>
{
	/// <summary>Gets the untyped handle: the native object pointer with the property and method primitives.</summary>
	public CEObject Handle
	{
		get;
	}

	/// <summary>Wraps an untyped handle as <typeparamref name="TSelf" /> without any check.</summary>
	/// <param name="handle">The handle; may be <see cref="CEObject.IsNull" />, which gives the default value.</param>
	/// <returns>The typed handle.</returns>
	public static abstract TSelf FromHandle(CEObject handle);
}
