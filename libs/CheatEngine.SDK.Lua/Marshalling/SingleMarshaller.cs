using System.Runtime.CompilerServices;

using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Lua.Marshalling;

/// <summary>
///     <see cref="float" /> as a Lua float. Pushed widened to a double; read back narrowed with <see cref="double" /> to
///     <see cref="float" /> conversion semantics (a value outside the <see cref="float" /> range becomes infinity, which
///     is what Cheat Engine's <c>readFloat</c> results never are in practice).
/// </summary>
/// <remarks>One C API call each way; allocates nothing.</remarks>
public readonly struct SingleMarshaller : ILuaMarshaller<float>
{
	/// <inheritdoc />
	[LuaStackEffect(1)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Push(LuaState state, float value)
	{
		state.PushNumber(value);
	}

	/// <inheritdoc />
	[LuaStackEffect(0)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool TryRead(LuaState state, int index, out float value)
	{
		bool ok = state.TryReadNumber(index, out double wide);
		value = (float) wide;
		return ok;
	}
}
