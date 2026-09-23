using System.Runtime.CompilerServices;

using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Lua.Marshalling;

/// <summary>
///     <see cref="int" /> as a Lua integer. Reading succeeds only when the 64-bit Lua integer fits a 32-bit signed value;
///     out-of-range values are reported as <see langword="false" />, never truncated.
/// </summary>
/// <remarks>
///     Conversion rules for the Lua side are those of <see cref="Int64Marshaller" />: an integer subtype, an integral
///     float below 2^53 or an integer numeral string, never a value rounded through a <see cref="double" />; the 64-bit
///     result must then fit 32 bits signed. An unsigned 32-bit value above <see cref="int.MaxValue" /> is refused rather
///     than reinterpreted: read it as <see cref="long" />. Two C API calls to read an integer subtype plus a range
///     check; allocates nothing.
/// </remarks>
public readonly struct Int32Marshaller : ILuaMarshaller<int>
{
	/// <inheritdoc />
	[LuaStackEffect(1)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Push(LuaState state, int value)
	{
		state.PushInteger(value);
	}

	/// <inheritdoc />
	[LuaStackEffect(0)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool TryRead(LuaState state, int index, out int value)
	{
		if (LuaIntegerReader.TryRead(state, index, true, out long wide) && wide >= int.MinValue && wide <= int.MaxValue)
		{
			value = (int) wide;
			return true;
		}

		value = 0;
		return false;
	}
}
