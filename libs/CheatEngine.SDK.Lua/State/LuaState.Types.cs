using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using CheatEngine.SDK.Annotations.Lua;

using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

namespace CheatEngine.SDK.Lua.State;

// Type tests: one C API call each, none can raise. Every member takes an acceptable index.
public readonly unsafe partial struct LuaState
{
	/// <summary>Gets the basic type of the value at <paramref name="index" /> (<c>lua_type</c>).</summary>
	/// <param name="index">An acceptable index.</param>
	/// <returns>The type tag; <see cref="LuaType.None" /> for an index beyond the top.</returns>
	[LuaStackEffect(0)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public LuaType TypeOf(int index)
	{
		return (LuaType) lua_type(Pointer, index);
	}

	/// <summary>
	///     The name Lua gives to the type of the value at <paramref name="index" /> (<c>lua_typename</c> of <c>lua_type</c>):
	///     <c language="lua">nil</c>, <c language="lua">boolean</c>, <c language="lua">number</c>,
	///     <c language="lua">string</c>, <c language="lua">table</c>, <c language="lua">function</c>,
	///     <c language="lua">userdata</c>, <c language="lua">thread</c>, or <c language="lua">no value</c> beyond the
	///     top. For error messages that name what a thunk or a wrapper
	///     actually received, without allocating.
	/// </summary>
	/// <param name="index">An acceptable index.</param>
	/// <returns>
	///     The name, ASCII, without its terminator; a view of a constant string inside the Lua library, valid as long as
	///     the library is loaded.
	/// </returns>
	/// <remarks>Two C API calls; never raises.</remarks>
	[LuaStackEffect(0)]
	public ReadOnlySpan<byte> TypeName(int index)
	{
		return TypeName((LuaType) lua_type(Pointer, index));
	}

	/// <summary>The name Lua gives to a type tag (<c>lua_typename</c>); see <see cref="TypeName(int)" />.</summary>
	/// <param name="type">A type tag, <see cref="LuaType.None" /> included.</param>
	/// <returns>The name, ASCII, without its terminator.</returns>
	/// <remarks>One C API call; never raises for a tag of <see cref="LuaType" />.</remarks>
	public ReadOnlySpan<byte> TypeName(LuaType type)
	{
		Debug.Assert(type >= LuaType.None && type <= LuaType.Thread, "Not a Lua 5.3 type tag.");
		return MemoryMarshal.CreateReadOnlySpanFromNullTerminated(lua_typename(Pointer, (int) type));
	}

	/// <summary>
	///     Whether the value at <paramref name="index" /> is a number stored as an integer (<c>lua_isinteger</c>). A
	///     float with an integral value is not.
	/// </summary>
	/// <param name="index">An acceptable index.</param>
	[LuaStackEffect(0)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsInteger(int index)
	{
		return lua_isinteger(Pointer, index) != 0;
	}

	/// <summary>
	///     Whether the value at <paramref name="index" /> is a number, or a string that Lua would convert to one (
	///     <c>lua_isnumber</c>).
	/// </summary>
	/// <param name="index">An acceptable index.</param>
	[LuaStackEffect(0)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsNumberConvertible(int index)
	{
		return lua_isnumber(Pointer, index) != 0;
	}

	/// <summary>Whether the value at <paramref name="index" /> is <c>nil</c>.</summary>
	/// <param name="index">An acceptable index.</param>
	[LuaStackEffect(0)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsNil(int index)
	{
		return lua_type(Pointer, index) == LUA_TNIL;
	}

	/// <summary>Whether <paramref name="index" /> lies beyond the top of the stack (an absent argument).</summary>
	/// <param name="index">An acceptable index.</param>
	[LuaStackEffect(0)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsNone(int index)
	{
		return lua_type(Pointer, index) == LUA_TNONE;
	}

	/// <summary>
	///     Whether the value at <paramref name="index" /> is <c>nil</c> or absent: what an optional argument left out
	///     looks like.
	/// </summary>
	/// <param name="index">An acceptable index.</param>
	[LuaStackEffect(0)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsNoneOrNil(int index)
	{
		return lua_type(Pointer, index) <= LUA_TNIL;
	}

	/// <summary>Whether the value at <paramref name="index" /> is a function, Lua or C.</summary>
	/// <param name="index">An acceptable index.</param>
	[LuaStackEffect(0)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsFunction(int index)
	{
		return lua_type(Pointer, index) == LUA_TFUNCTION;
	}

	/// <summary>Whether the value at <paramref name="index" /> is a table.</summary>
	/// <param name="index">An acceptable index.</param>
	[LuaStackEffect(0)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsTable(int index)
	{
		return lua_type(Pointer, index) == LUA_TTABLE;
	}

	/// <summary>
	///     Whether the value at <paramref name="index" /> is a full userdata (a Lua-owned block, which is what host
	///     objects are).
	/// </summary>
	/// <param name="index">An acceptable index.</param>
	[LuaStackEffect(0)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsUserdata(int index)
	{
		return lua_type(Pointer, index) == LUA_TUSERDATA;
	}

	/// <summary>Whether the value at <paramref name="index" /> is a light userdata (a bare pointer).</summary>
	/// <param name="index">An acceptable index.</param>
	[LuaStackEffect(0)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool IsLightUserdata(int index)
	{
		return lua_type(Pointer, index) == LUA_TLIGHTUSERDATA;
	}
}
