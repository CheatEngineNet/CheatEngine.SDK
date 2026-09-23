using System.Runtime.CompilerServices;

using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Lua.Marshalling;

/// <summary>
///     <see cref="long" /> as a Lua integer (<c>lua_Integer</c> is 64-bit in Lua 5.3): the lossless scalar marshaller.
/// </summary>
/// <remarks>
///     <para>
///         Reading never lets a value pass through a lossy <see cref="double" />: an integer subtype reads exactly; a float
///         reads only when it is integral and of magnitude below 2^53 (<c>3.0</c> succeeds, <c>9007199254740992.0</c>,
///         <c>1e19</c> and <c>2.5</c> fail); a string reads only when it is a Lua integer numeral that fits 64 bits
///         (<c>"42"</c>, <c>" 0x10 "</c>, <c>"-9223372036854775808"</c> succeed; <c>"3.0"</c>, <c>"1e3"</c>,
///         <c>"9223372036854775808"</c> and more than 16 significant hexadecimal digits fail). <c>nil</c>, a boolean or a
///         table fail. Use <see cref="LuaState.IsInteger" /> when the representation itself matters.
///     </para>
///     <para>
///         This is stricter than Lua's own <c>lua_tointegerx</c>, which rounds float numerals and floats of any magnitude
///         (audit Q21). One C API call to push, two to read an integer subtype; allocates nothing.
///     </para>
/// </remarks>
public readonly struct Int64Marshaller : ILuaMarshaller<long>
{
	/// <inheritdoc />
	[LuaStackEffect(1)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Push(LuaState state, long value)
	{
		state.PushInteger(value);
	}

	/// <inheritdoc />
	[LuaStackEffect(0)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool TryRead(LuaState state, int index, out long value)
	{
		return LuaIntegerReader.TryRead(state, index, true, out value);
	}
}
