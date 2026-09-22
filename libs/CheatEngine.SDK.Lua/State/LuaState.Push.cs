using System;
using System.Runtime.CompilerServices;

using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Interop.Protected;
using CheatEngine.SDK.Lua.Text;

using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

namespace CheatEngine.SDK.Lua.State;

// Scalar pushes do not run Lua code. Allocating string pushes use the native protection boundary.
public readonly unsafe partial struct LuaState
{
	/// <summary>Pushes <c>nil</c>.</summary>
	[LuaStackEffect(1)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void PushNil()
	{
		lua_pushnil(Pointer);
	}

	/// <summary>Pushes an integer (<c>lua_pushinteger</c>). Lua integers are 64-bit.</summary>
	/// <param name="value">The value.</param>
	[LuaStackEffect(1)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void PushInteger(long value)
	{
		lua_pushinteger(Pointer, value);
	}

	/// <summary>Pushes a float (<c>lua_pushnumber</c>). Lua floats are doubles.</summary>
	/// <param name="value">The value.</param>
	[LuaStackEffect(1)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void PushNumber(double value)
	{
		lua_pushnumber(Pointer, value);
	}

	/// <summary>Pushes a boolean (<c>lua_pushboolean</c>).</summary>
	/// <param name="value">The value.</param>
	[LuaStackEffect(1)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void PushBoolean(bool value)
	{
		lua_pushboolean(Pointer, value ? 1 : 0);
	}

	/// <summary>
	///     Pushes a light userdata: a bare pointer value with no identity or lifetime of its own (
	///     <c>lua_pushlightuserdata</c>).
	/// </summary>
	/// <param name="address">The pointer value; zero is a valid (null) light userdata.</param>
	[LuaStackEffect(1)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void PushLightUserdata(nint address)
	{
		lua_pushlightuserdata(Pointer, (void*) address);
	}

	/// <summary>Pushes the globals table (<c>lua_pushglobaltable</c>), for raw access to globals that bypasses metamethods.</summary>
	[LuaStackEffect(1)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void PushGlobalTable()
	{
		_ = lua_rawgeti(Pointer, LUA_REGISTRYINDEX, LUA_RIDX_GLOBALS);
	}

	/// <summary>
	///     Pushes a string from its bytes (<c>lua_pushlstring</c>). Lua copies the bytes; embedded NULs are kept. The
	///     SDK's convention is UTF-8, which is what a <c>"..."u8</c> literal is.
	/// </summary>
	/// <param name="utf8">The bytes; may be empty.</param>
	/// <remarks>
	///     Allocates inside Lua under native protection. A failed allocation or finalizer becomes a managed
	///     <see cref="LuaException" />; the original stack depth is preserved on failure.
	/// </remarks>
	[LuaStackEffect(1)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void PushString(ReadOnlySpan<byte> utf8)
	{
		CheckProtectedResult(TryPushString(utf8));
	}

	/// <summary>Pushes bytes as a Lua string under native protection; on failure pushes one error value instead.</summary>
	/// <param name="utf8">The bytes, including any embedded NULs.</param>
	/// <returns>The Lua status of the allocation and any finalizer it ran.</returns>
	[LuaStackEffect(1)]
	public LuaStatus TryPushString(ReadOnlySpan<byte> utf8)
	{
		return new LuaStatus(LuaProtectedApi.PushBytes(Pointer, utf8));
	}

	// Throw only after native protection has returned; consuming the error preserves the operation's input contract.
	internal void CheckProtectedResult(LuaStatus status)
	{
		if (status.IsOk)
		{
			return;
		}

		LuaError error;
		try
		{
			error = LuaError.FromStack(this, status);
		}
		finally
		{
			Pop(1);
		}

		throw new LuaException(error);
	}

	/// <summary>
	///     Pushes a string from UTF-16 text (a <see cref="string" /> converts implicitly), transcoded to UTF-8 through a
	///     stack buffer, or a pooled array above <see cref="Utf8Scratch.StackBufferSize" /> bytes. Lone surrogates become
	///     U+FFFD.
	/// </summary>
	/// <param name="text">The text; may be empty.</param>
	/// <remarks>
	///     The convenience form: hot paths push <c>"..."u8</c> literals or cached UTF-8 through
	///     <see cref="PushString(ReadOnlySpan{byte})" />. Allocates inside Lua; see there.
	/// </remarks>
	[LuaStackEffect(1)]
	[SkipLocalsInit] // Encode writes the bytes it reports before anything reads them; zeroing the 512-byte buffer first would be dead stores.
	public void PushString(ReadOnlySpan<char> text)
	{
		Span<byte> scratch = stackalloc byte[Utf8Scratch.StackBufferSize];
		using Utf8Scratch utf8 = Utf8Scratch.Encode(text, scratch);
		PushString(utf8.Bytes);
	}
}
