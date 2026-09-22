using System.Runtime.CompilerServices;

using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Lua.Marshalling;

/// <summary>
///     <see cref="double" /> as a Lua float (<c>lua_Number</c> is a double in Lua 5.3). Reading accepts any Lua number,
///     integer or float (an integer is converted exactly up to 2^53), and a string Lua can convert.
/// </summary>
/// <remarks>One C API call each way; allocates nothing.</remarks>
public readonly struct DoubleMarshaller : ILuaMarshaller<double>
{
	/// <inheritdoc />
	[LuaStackEffect(1)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Push(LuaState state, double value)
	{
		state.PushNumber(value);
	}

	/// <inheritdoc />
	[LuaStackEffect(0)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool TryRead(LuaState state, int index, out double value)
	{
		return state.TryReadNumber(index, out value);
	}
}
