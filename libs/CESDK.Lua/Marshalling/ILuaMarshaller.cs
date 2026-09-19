using System.Diagnostics.CodeAnalysis;
using CESDK.Annotations.Lua;
using CESDK.Lua.State;

namespace CESDK.Lua.Marshalling;

/// <summary>
///     Converts one managed type to and from a Lua stack value. Implemented by stateless <see langword="readonly" />
///     <see langword="struct" />s with <see langword="static" /> members, so that generic code written against
///     <c>TMarshaller : ILuaMarshaller&lt;T&gt;</c> is specialised by the JIT per marshaller: the calls are direct,
///     nothing
///     is boxed, no delegate exists.
/// </summary>
/// <typeparam name="T">
///     The managed type; a <see langword="ref" /> <see langword="struct" /> such as
///     <c>ReadOnlySpan&lt;byte&gt;</c> is allowed.
/// </typeparam>
/// <remarks>
///     <para>
///         <b>Contract.</b> <see cref="Push" /> pushes exactly one value and never runs Lua code (it may allocate inside
///         Lua,
///         as a string push does). <see cref="TryRead" /> reads the value at an acceptable index, never modifies the
///         stack,
///         never runs Lua code and reports a value of the wrong kind as <see langword="false" /> with a default result, so
///         that <c>nil</c> results (Cheat Engine's "failed") become <see langword="false" /> without an exception.
///     </para>
///     <para>
///         Generated code calls the concrete marshaller directly (<c>Int32Marshaller.Push(L, x)</c>); the interface exists
///         for code that is generic over the marshaller (table readers, argument lists) and for user-defined marshallers
///         that a future generator option can name. <typeparamref name="T" /> is the type a declaration names
///         (<c>string</c>, not <c>string?</c>): a failed read leaves <see langword="default" />, which the
///         <see cref="MaybeNullWhenAttribute" /> on <see cref="TryRead" /> tells the compiler about.
///     </para>
///     <para>
///         A <see langword="ref" /> <see langword="struct" /> <typeparamref name="T" /> such as
///         <c>ReadOnlySpan&lt;byte&gt;</c>
///         is for arguments and for reads whose value stays on the stack while the span is used (inside a thunk or a
///         <see cref="LuaFrame" />). A generated wrapper that pops its results before returning must not return such a
///         span:
///         it copies the bytes out first (<see cref="LuaState.TryCopyUtf8" />) or reads a <see cref="string" />.
///     </para>
/// </remarks>
public interface ILuaMarshaller<T>
    where T : allows ref struct
{
    /// <summary>Pushes <paramref name="value" /> as one Lua value.</summary>
    /// <param name="state">The state to push on.</param>
    /// <param name="value">The value.</param>
    [LuaStackEffect(1)]
    public static abstract void Push(LuaState state, T value);

    /// <summary>Reads the value at <paramref name="index" /> as a <typeparamref name="T" /> without changing the stack.</summary>
    /// <param name="state">The state to read from.</param>
    /// <param name="index">An acceptable index.</param>
    /// <param name="value">The read value, or <see langword="default" /> when the Lua value is not of the expected kind.</param>
    /// <returns><see langword="true" /> when <paramref name="value" /> holds a read value.</returns>
    [LuaStackEffect(0)]
    public static abstract bool TryRead(LuaState state, int index, [MaybeNullWhen(false)] out T value);
}
