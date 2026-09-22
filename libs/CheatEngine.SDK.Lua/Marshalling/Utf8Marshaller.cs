using System;
using System.Runtime.CompilerServices;

using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Lua.Marshalling;

/// <summary>
///     UTF-8 bytes (<c>ReadOnlySpan&lt;byte&gt;</c>) as a Lua string: the primary, allocation-free string marshaller.
///     Pushing copies the bytes into Lua; reading returns a view of Lua's memory that is valid only while the string
///     stays on the stack.
/// </summary>
/// <remarks>
///     Reading is strict: a number is not converted (that would rewrite the stack slot); see
///     <see cref="LuaState.TryReadUtf8" />. Pushing allocates inside Lua; nothing allocates on the managed side.
/// </remarks>
public readonly struct Utf8Marshaller : ILuaMarshaller<ReadOnlySpan<byte>>
{
	/// <inheritdoc />
	[LuaStackEffect(1)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Push(LuaState state, ReadOnlySpan<byte> value)
	{
		state.PushString(value);
	}

	/// <inheritdoc />
	[LuaStackEffect(0)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool TryRead(LuaState state, int index, out ReadOnlySpan<byte> value)
	{
		return state.TryReadUtf8(index, out value);
	}
}
