using System;
using System.Runtime.CompilerServices;

using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Interop.Protected;

using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

namespace CheatEngine.SDK.Lua.State;

// Raw table access bypasses metamethods. Allocations can run finalizers and use the native protection boundary.
public readonly unsafe partial struct LuaState
{
	/// <summary>Pushes a new empty table (<c>lua_createtable</c>), pre-sized when the hints are known.</summary>
	/// <param name="arraySlots">Expected number of sequence elements.</param>
	/// <param name="recordSlots">Expected number of other keys.</param>
	/// <remarks>Allocates inside Lua.</remarks>
	[LuaStackEffect(1)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void CreateTable(int arraySlots = 0, int recordSlots = 0)
	{
		CheckProtectedResult(new LuaStatus(LuaProtectedApi.CreateTable(Pointer, arraySlots, recordSlots)));
	}

	/// <summary>Pushes a one-based Lua sequence table containing the supplied bytes as Lua integers.</summary>
	/// <param name="bytes">The byte values copied into the new Lua table.</param>
	/// <remarks>Allocates and fills the table inside one native protected operation.</remarks>
	[LuaStackEffect(1)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void PushByteTable(ReadOnlySpan<byte> bytes)
	{
		CheckProtectedResult(new LuaStatus(LuaProtectedApi.PushByteTable(Pointer, bytes)));
	}

	/// <summary>Pops a key and pushes <c>t[key]</c> without metamethods (<c>lua_rawget</c>).</summary>
	/// <param name="tableIndex">A valid index of a table.</param>
	/// <returns>The type of the pushed value.</returns>
	[LuaStackEffect(0)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public LuaType RawGet(int tableIndex)
	{
		return (LuaType) lua_rawget(Pointer, tableIndex);
	}

	/// <summary>Pushes <c>t[n]</c> without metamethods (<c>lua_rawgeti</c>).</summary>
	/// <param name="tableIndex">A valid index of a table, <see cref="RegistryIndex" /> included.</param>
	/// <param name="key">The integer key.</param>
	/// <returns>The type of the pushed value.</returns>
	[LuaStackEffect(1)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public LuaType RawGetIndex(int tableIndex, long key)
	{
		return (LuaType) lua_rawgeti(Pointer, tableIndex, key);
	}

	/// <summary>
	///     Pushes <c>t[p]</c> for a light-userdata key without metamethods (<c>lua_rawgetp</c>); the idiom for registry
	///     entries private to one library.
	/// </summary>
	/// <param name="tableIndex">A valid index of a table, <see cref="RegistryIndex" /> included.</param>
	/// <param name="key">The pointer used as key.</param>
	/// <returns>The type of the pushed value.</returns>
	[LuaStackEffect(1)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public LuaType RawGetPointer(int tableIndex, nint key)
	{
		return (LuaType) lua_rawgetp(Pointer, tableIndex, (void*) key);
	}

	/// <summary>
	///     Pops a value and a key and does <c>t[key] = value</c> without metamethods (<c>lua_rawset</c>), unless the key
	///     is <c>nil</c> or NaN, which Lua refuses by raising: those keys are refused here instead, before the C call.
	/// </summary>
	/// <param name="tableIndex">A valid index of a table.</param>
	/// <returns>
	///     <see langword="false" /> when the key was <c>nil</c> or NaN; the table is unchanged. The key and the value are
	///     popped in every case.
	/// </returns>
	/// <remarks>
	///     Allocates inside Lua when the table grows. One <c>lua_type</c> call more than the raw C function (two more for
	///     a float key).
	/// </remarks>
	[LuaStackEffect(-2)]
	public bool TryRawSet(int tableIndex)
	{
		int keyType = lua_type(Pointer, -2);
		if (keyType == LUA_TNIL || (keyType == LUA_TNUMBER && lua_isinteger(Pointer, -2) == 0 &&
		                            double.IsNaN(lua_tonumberx(Pointer, -2, null))))
		{
			lua_settop(Pointer, -3);
			return false;
		}

		LuaStatus status = new(LuaProtectedApi.RawSet(Pointer, tableIndex));
		CheckProtectedResult(status);
		return true;
	}

	/// <summary>Pops a value and does <c>t[n] = value</c> without metamethods (<c>lua_rawseti</c>).</summary>
	/// <param name="tableIndex">A valid index of a table, <see cref="RegistryIndex" /> included.</param>
	/// <param name="key">The integer key.</param>
	/// <remarks>Allocates inside Lua when the table grows.</remarks>
	[LuaStackEffect(-1)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void RawSetIndex(int tableIndex, long key)
	{
		CheckProtectedResult(new LuaStatus(LuaProtectedApi.RawSetI(Pointer, tableIndex, key)));
	}

	/// <summary>Pops a value and does <c>t[p] = value</c> for a light-userdata key without metamethods (<c>lua_rawsetp</c>).</summary>
	/// <param name="tableIndex">A valid index of a table, <see cref="RegistryIndex" /> included.</param>
	/// <param name="key">The pointer used as key.</param>
	/// <remarks>Allocates inside Lua when the table grows.</remarks>
	[LuaStackEffect(-1)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void RawSetPointer(int tableIndex, nint key)
	{
		CheckProtectedResult(new LuaStatus(LuaProtectedApi.RawSetP(Pointer, tableIndex, key)));
	}

	/// <summary>Whether two values are primitively equal, without <c>__eq</c> (<c>lua_rawequal</c>).</summary>
	/// <param name="index1">An acceptable index.</param>
	/// <param name="index2">An acceptable index.</param>
	[LuaStackEffect(0)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool RawEquals(int index1, int index2)
	{
		return lua_rawequal(Pointer, index1, index2) != 0;
	}

	/// <summary>Pushes the metatable of the value at <paramref name="index" /> when it has one (<c>lua_getmetatable</c>).</summary>
	/// <param name="index">A valid index.</param>
	/// <returns><see langword="true" /> and the metatable pushed; <see langword="false" /> and nothing pushed.</returns>
	public bool TryGetMetatable(int index)
	{
		return lua_getmetatable(Pointer, index) != 0;
	}

	/// <summary>
	///     Pops a table (or <c>nil</c>) and makes it the metatable of the value at <paramref name="index" /> (
	///     <c>lua_setmetatable</c>).
	/// </summary>
	/// <param name="index">A valid index.</param>
	[LuaStackEffect(-1)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void SetMetatable(int index)
	{
		_ = lua_setmetatable(Pointer, index);
	}

	/// <summary>Pushes a new full userdata of <paramref name="size" /> bytes and returns its block (<c>lua_newuserdata</c>).</summary>
	/// <param name="size">Block size in bytes.</param>
	/// <returns>The address of the block: not zeroed, never moves, owned by Lua and freed by its collector.</returns>
	/// <remarks>Allocates inside Lua.</remarks>
	[LuaStackEffect(1)]
	public nint NewUserdata(nuint size)
	{
		CheckProtectedResult(new LuaStatus(LuaProtectedApi.NewUserdata(Pointer, size)));
		return (nint) lua_touserdata(Pointer, -1);
	}
}
