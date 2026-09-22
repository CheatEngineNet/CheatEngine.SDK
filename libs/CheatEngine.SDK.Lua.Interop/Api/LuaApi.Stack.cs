using System.Runtime.CompilerServices;

using CheatEngine.SDK.Lua.Interop.Types;

namespace CheatEngine.SDK.Lua.Interop.Api;

// lua.h "basic stack manipulation".
public static unsafe partial class LuaApi
{
	/// <summary>
	///     <c>int lua_absindex (lua_State *L, int idx)</c>. Converts an acceptable index into an absolute one, which stays
	///     valid while values are pushed above it. Pseudo-indices are returned unchanged.
	/// </summary>
	/// <param name="l">The state.</param>
	/// <param name="idx">Acceptable index (positive, negative or pseudo).</param>
	/// <remarks>Stack: -0 +0. Raises: never.</remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int lua_absindex(lua_State* l, int idx)
	{
		return s_table.lua_absindex(l, idx);
	}

	/// <summary>
	///     <c>int lua_gettop (lua_State *L)</c>. Index of the top element, which is also the number of elements of the
	///     current frame.
	/// </summary>
	/// <param name="l">The state.</param>
	/// <remarks>Stack: -0 +0. Raises: never.</remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int lua_gettop(lua_State* l)
	{
		return s_table.lua_gettop(l);
	}

	/// <summary>
	///     <c>void lua_settop (lua_State *L, int idx)</c>. Sets the top: grows the frame with nils or drops the elements
	///     above <paramref name="idx" />. 0 empties the frame.
	/// </summary>
	/// <param name="l">The state.</param>
	/// <param name="idx">New top as an acceptable index; negative values count from the current top.</param>
	/// <remarks>
	///     Stack: -? +?. Raises: never. Growing beyond the space guaranteed by <see cref="lua_checkstack" /> is undefined
	///     behaviour.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void lua_settop(lua_State* l, int idx)
	{
		s_table.lua_settop(l, idx);
	}

	/// <summary><c>void lua_pushvalue (lua_State *L, int idx)</c>. Pushes a copy of the element at <paramref name="idx" />.</summary>
	/// <param name="l">The state.</param>
	/// <param name="idx">Valid index.</param>
	/// <remarks>Stack: -0 +1. Raises: never.</remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void lua_pushvalue(lua_State* l, int idx)
	{
		s_table.lua_pushvalue(l, idx);
	}

	/// <summary>
	///     <c>void lua_rotate (lua_State *L, int idx, int n)</c>. Rotates the elements between <paramref name="idx" /> and
	///     the top by <paramref name="n" /> positions towards the top (negative: towards the bottom).
	/// </summary>
	/// <param name="l">The state.</param>
	/// <param name="idx">Valid stack index (not a pseudo-index) where the rotated segment starts.</param>
	/// <param name="n">Positions; its absolute value must not exceed the segment length.</param>
	/// <remarks>Stack: -0 +0. Raises: never.</remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void lua_rotate(lua_State* l, int idx, int n)
	{
		s_table.lua_rotate(l, idx, n);
	}

	/// <summary>
	///     <c>void lua_copy (lua_State *L, int fromidx, int toidx)</c>. Overwrites the slot <paramref name="toidx" />
	///     with the value at <paramref name="fromidx" />; nothing moves.
	/// </summary>
	/// <param name="l">The state.</param>
	/// <param name="fromidx">Valid source index.</param>
	/// <param name="toidx">Valid destination index.</param>
	/// <remarks>Stack: -0 +0. Raises: never.</remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void lua_copy(lua_State* l, int fromidx, int toidx)
	{
		s_table.lua_copy(l, fromidx, toidx);
	}

	/// <summary>
	///     <c>int lua_checkstack (lua_State *L, int n)</c>. Ensures room for <paramref name="n" /> more pushes. Returns 0
	///     when the stack cannot grow (limit <see cref="LUAI_MAXSTACK" /> or allocation failure), non-zero otherwise.
	/// </summary>
	/// <param name="l">The state.</param>
	/// <param name="n">Extra slots wanted.</param>
	/// <remarks>Stack: -0 +0. Raises: never. A C function starts with <see cref="LUA_MINSTACK" /> free slots.</remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int lua_checkstack(lua_State* l, int n)
	{
		return s_table.lua_checkstack(l, n);
	}

	/// <summary>
	///     <c>void lua_xmove (lua_State *from, lua_State *to, int n)</c>. Pops <paramref name="n" /> values from one thread
	///     and pushes them onto another thread of the same global state.
	/// </summary>
	/// <param name="from">Source thread.</param>
	/// <param name="to">Destination thread; must share its global state with <paramref name="from" />.</param>
	/// <param name="n">Number of values.</param>
	/// <remarks>Stack: -? +? (across two stacks). Raises: never.</remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void lua_xmove(lua_State* from, lua_State* to, int n)
	{
		s_table.lua_xmove(from, to, n);
	}

	internal partial struct Table
	{
		internal delegate* unmanaged[Cdecl]<lua_State*, int, int> lua_absindex;
		internal delegate* unmanaged[Cdecl]<lua_State*, int> lua_gettop;
		internal delegate* unmanaged[Cdecl]<lua_State*, int, void> lua_settop;
		internal delegate* unmanaged[Cdecl]<lua_State*, int, void> lua_pushvalue;
		internal delegate* unmanaged[Cdecl]<lua_State*, int, int, void> lua_rotate;
		internal delegate* unmanaged[Cdecl]<lua_State*, int, int, void> lua_copy;
		internal delegate* unmanaged[Cdecl]<lua_State*, int, int> lua_checkstack;
		internal delegate* unmanaged[Cdecl]<lua_State*, lua_State*, int, void> lua_xmove;

		private void LoadStack(ref ExportResolver exports)
		{
			lua_absindex = (delegate* unmanaged[Cdecl]<lua_State*, int, int>) exports.Resolve("lua_absindex");
			lua_gettop = (delegate* unmanaged[Cdecl]<lua_State*, int>) exports.Resolve("lua_gettop");
			lua_settop = (delegate* unmanaged[Cdecl]<lua_State*, int, void>) exports.Resolve("lua_settop");
			lua_pushvalue = (delegate* unmanaged[Cdecl]<lua_State*, int, void>) exports.Resolve("lua_pushvalue");
			lua_rotate = (delegate* unmanaged[Cdecl]<lua_State*, int, int, void>) exports.Resolve("lua_rotate");
			lua_copy = (delegate* unmanaged[Cdecl]<lua_State*, int, int, void>) exports.Resolve("lua_copy");
			lua_checkstack = (delegate* unmanaged[Cdecl]<lua_State*, int, int>) exports.Resolve("lua_checkstack");
			lua_xmove = (delegate* unmanaged[Cdecl]<lua_State*, lua_State*, int, void>) exports.Resolve("lua_xmove");
		}
	}
}
