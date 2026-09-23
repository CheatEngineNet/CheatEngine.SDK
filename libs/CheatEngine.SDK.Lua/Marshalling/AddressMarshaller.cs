using System.Runtime.CompilerServices;

using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Lua.Marshalling;

/// <summary>
///     <see cref="nuint" /> (a target-process address) as a Lua integer, by bit reinterpretation: the 64-bit unsigned
///     address is pushed as the 64-bit signed <c>lua_Integer</c> with the same bits, which is how Cheat Engine's Lua
///     functions take and return addresses, so an address above <see cref="long.MaxValue" /> appears negative in Lua and
///     comes back intact.
/// </summary>
/// <remarks>
///     Reading is strict about the Lua type: only a number is accepted, an integer subtype (every bit kept, an address
///     above 4 GiB or above <see cref="long.MaxValue" /> included) or a float with an exact integral value of magnitude
///     below 2^53. A float at or above 2^53 is refused: a 64-bit address never passes through a lossy
///     <see cref="double" /> (audit A07-02, Q21). A string is refused even when Lua could convert it, because Cheat Engine
///     returns some addresses as hexadecimal <i>text</i> and Lua's own string-to-number rule would read <c>"10"</c> as ten
///     and <c>"00400000"</c> as four hundred thousand: a wrong address rather than a failure. The number-or-hex-string
///     convention is decoded one layer up (<c>CheatEngine.SDK.Engine</c>'s address reader: a number through this
///     marshaller, a string through <see cref="LuaState.TryReadUtf8" /> and a hexadecimal parse). On a 32-bit process a
///     value that does not fit is reported as <see langword="false" />. One C API call to push, two to read an integer
///     subtype; allocates nothing.
/// </remarks>
public readonly struct AddressMarshaller : ILuaMarshaller<nuint>
{
	/// <inheritdoc />
	[LuaStackEffect(1)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Push(LuaState state, nuint value)
	{
		state.PushInteger(unchecked((long) value));
	}

	/// <inheritdoc />
	[LuaStackEffect(0)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool TryRead(LuaState state, int index, out nuint value)
	{
		if (!LuaIntegerReader.TryRead(state, index, false, out long bits))
		{
			value = 0;
			return false;
		}

		ulong wide = unchecked((ulong) bits);
		if (nuint.Size == sizeof(uint) && wide > uint.MaxValue)
		{
			value = 0;
			return false;
		}

		value = unchecked((nuint) wide);
		return true;
	}
}
