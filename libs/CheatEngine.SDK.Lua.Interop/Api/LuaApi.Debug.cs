using System.Runtime.CompilerServices;

using CheatEngine.SDK.Lua.Interop.Types;

namespace CheatEngine.SDK.Lua.Interop.Api;

// lua.h "Debug API".
public static unsafe partial class LuaApi
{
	/// <summary>
	///     <c>int lua_getstack (lua_State *L, int level, lua_Debug *ar)</c>. Identifies the activation record at a call
	///     level (0 = the running function, 1 = its caller, ...) for later <see cref="lua_getinfo" /> calls. Returns 0 when
	///     the level is deeper than the stack.
	/// </summary>
	/// <param name="L">The state.</param>
	/// <param name="level">Call level.</param>
	/// <param name="ar">Caller-owned record; only its private part is written.</param>
	/// <remarks>Stack: -0 +0. Raises: never.</remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int lua_getstack(lua_State* L, int level, lua_Debug* ar)
	{
		return s_table.lua_getstack(L, level, ar);
	}

	/// <summary>
	///     <c>int lua_getinfo (lua_State *L, const char *what, lua_Debug *ar)</c>. Fills the fields of <paramref name="ar" />
	///     selected by the letters of <paramref name="what" /> ("n", "S", "l", "u", "t"; "f" pushes the function, "L" pushes
	///     a table of valid lines). A leading "&gt;" takes the function from the top of the stack (and pops it) instead of
	///     from <paramref name="ar" />. Returns 0 on an invalid option letter.
	/// </summary>
	/// <param name="L">The state.</param>
	/// <param name="what">NUL-terminated option letters.</param>
	/// <param name="ar">Record prepared by <see cref="lua_getstack" /> or received by a hook (not needed with "&gt;").</param>
	/// <remarks>
	///     Stack: -(0|1) +(0|1|2). Raises: any, which is how the Lua 5.3 manual classifies it (marker <c>e</c>). The only
	///     raising path in the 5.3 sources is the allocation of the "L" table, so in practice it behaves like "memory"
	///     with "L" and like "never" without it; the manual stays the contract.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int lua_getinfo(lua_State* L, byte* what, lua_Debug* ar)
	{
		return s_table.lua_getinfo(L, what, ar);
	}

	/// <summary>
	///     <c>const char *lua_getlocal (lua_State *L, const lua_Debug *ar, int n)</c>. Pushes the value of local number
	///     <paramref name="n" /> of an active function and returns its name; with a null <paramref name="ar" /> it only
	///     returns the parameter name of the function on top of the stack. Null (nothing pushed) when there is no such local.
	/// </summary>
	/// <param name="L">The state.</param>
	/// <param name="ar">Activation record, or null.</param>
	/// <param name="n">1-based local index.</param>
	/// <remarks>Stack: -0 +(0|1). Raises: never.</remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static byte* lua_getlocal(lua_State* L, lua_Debug* ar, int n)
	{
		return s_table.lua_getlocal(L, ar, n);
	}

	/// <summary>
	///     <c>const char *lua_setlocal (lua_State *L, const lua_Debug *ar, int n)</c>. Pops a value into local number
	///     <paramref name="n" /> and returns its name; null when there is no such local (the value is still popped).
	/// </summary>
	/// <param name="L">The state.</param>
	/// <param name="ar">Activation record.</param>
	/// <param name="n">1-based local index.</param>
	/// <remarks>Stack: -(0|1) +0. Raises: never.</remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static byte* lua_setlocal(lua_State* L, lua_Debug* ar, int n)
	{
		return s_table.lua_setlocal(L, ar, n);
	}

	/// <summary>
	///     <c>const char *lua_getupvalue (lua_State *L, int funcindex, int n)</c>. Pushes upvalue number
	///     <paramref name="n" /> of a closure and returns its name ("" for C closures); null (nothing pushed) when out of
	///     range.
	/// </summary>
	/// <param name="L">The state.</param>
	/// <param name="funcindex">Valid index of the closure.</param>
	/// <param name="n">1-based upvalue index.</param>
	/// <remarks>Stack: -0 +(0|1). Raises: never.</remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static byte* lua_getupvalue(lua_State* L, int funcindex, int n)
	{
		return s_table.lua_getupvalue(L, funcindex, n);
	}

	/// <summary>
	///     <c>const char *lua_setupvalue (lua_State *L, int funcindex, int n)</c>. Pops a value into upvalue number
	///     <paramref name="n" /> of a closure and returns its name; null (nothing popped) when out of range.
	/// </summary>
	/// <param name="L">The state.</param>
	/// <param name="funcindex">Valid index of the closure.</param>
	/// <param name="n">1-based upvalue index.</param>
	/// <remarks>Stack: -(0|1) +0. Raises: never.</remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static byte* lua_setupvalue(lua_State* L, int funcindex, int n)
	{
		return s_table.lua_setupvalue(L, funcindex, n);
	}

	/// <summary>
	///     <c>void *lua_upvalueid (lua_State *L, int fidx, int n)</c>. Identity of an upvalue: two closures share an
	///     upvalue exactly when the ids are equal.
	/// </summary>
	/// <param name="L">The state.</param>
	/// <param name="fidx">Valid index of the closure.</param>
	/// <param name="n">1-based upvalue index; must be in range.</param>
	/// <remarks>Stack: -0 +0. Raises: never.</remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void* lua_upvalueid(lua_State* L, int fidx, int n)
	{
		return s_table.lua_upvalueid(L, fidx, n);
	}

	/// <summary>
	///     <c>void lua_upvaluejoin (lua_State *L, int fidx1, int n1, int fidx2, int n2)</c>. Makes upvalue
	///     <paramref name="n1" /> of the first Lua closure refer to upvalue <paramref name="n2" /> of the second.
	/// </summary>
	/// <param name="L">The state.</param>
	/// <param name="fidx1">Valid index of the closure that is changed.</param>
	/// <param name="n1">1-based upvalue index in the first closure.</param>
	/// <param name="fidx2">Valid index of the closure that owns the shared upvalue.</param>
	/// <param name="n2">1-based upvalue index in the second closure.</param>
	/// <remarks>Stack: -0 +0. Raises: never.</remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void lua_upvaluejoin(lua_State* L, int fidx1, int n1, int fidx2, int n2)
	{
		s_table.lua_upvaluejoin(L, fidx1, n1, fidx2, n2);
	}

	/// <summary>
	///     <c>void lua_sethook (lua_State *L, lua_Hook func, int mask, int count)</c>. Installs a debug hook on this
	///     thread; a null function or a zero mask removes it.
	/// </summary>
	/// <param name="L">The thread.</param>
	/// <param name="func">
	///     Hook: for managed code a static <c>[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]</c>
	///     method that lets nothing escape.
	/// </param>
	/// <param name="mask">Combination of <c>LUA_MASK*</c> bits.</param>
	/// <param name="count">Instruction interval for <see cref="LUA_MASKCOUNT" />.</param>
	/// <remarks>
	///     Stack: -0 +0. Raises: never. The hook runs on the thread that executes the Lua code, inside the interpreter
	///     loop.
	/// </remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void lua_sethook(lua_State* L, lua_Hook func, int mask, int count)
	{
		s_table.lua_sethook(L, func, mask, count);
	}

	/// <summary><c>lua_Hook lua_gethook (lua_State *L)</c>. The installed hook, or null.</summary>
	/// <param name="L">The thread.</param>
	/// <remarks>Stack: -0 +0. Raises: never.</remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static lua_Hook lua_gethook(lua_State* L)
	{
		return s_table.lua_gethook(L);
	}

	/// <summary><c>int lua_gethookmask (lua_State *L)</c>. The installed hook mask.</summary>
	/// <param name="L">The thread.</param>
	/// <remarks>Stack: -0 +0. Raises: never.</remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int lua_gethookmask(lua_State* L)
	{
		return s_table.lua_gethookmask(L);
	}

	/// <summary><c>int lua_gethookcount (lua_State *L)</c>. The installed hook count.</summary>
	/// <param name="L">The thread.</param>
	/// <remarks>Stack: -0 +0. Raises: never.</remarks>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int lua_gethookcount(lua_State* L)
	{
		return s_table.lua_gethookcount(L);
	}

	internal partial struct Table
	{
		internal delegate* unmanaged[Cdecl]<lua_State*, int, lua_Debug*, int> lua_getstack;
		internal delegate* unmanaged[Cdecl]<lua_State*, byte*, lua_Debug*, int> lua_getinfo;
		internal delegate* unmanaged[Cdecl]<lua_State*, lua_Debug*, int, byte*> lua_getlocal;
		internal delegate* unmanaged[Cdecl]<lua_State*, lua_Debug*, int, byte*> lua_setlocal;
		internal delegate* unmanaged[Cdecl]<lua_State*, int, int, byte*> lua_getupvalue;
		internal delegate* unmanaged[Cdecl]<lua_State*, int, int, byte*> lua_setupvalue;
		internal delegate* unmanaged[Cdecl]<lua_State*, int, int, void*> lua_upvalueid;
		internal delegate* unmanaged[Cdecl]<lua_State*, int, int, int, int, void> lua_upvaluejoin;
		internal delegate* unmanaged[Cdecl]<lua_State*, lua_Hook, int, int, void> lua_sethook;
		internal delegate* unmanaged[Cdecl]<lua_State*, lua_Hook> lua_gethook;
		internal delegate* unmanaged[Cdecl]<lua_State*, int> lua_gethookmask;
		internal delegate* unmanaged[Cdecl]<lua_State*, int> lua_gethookcount;

		private void LoadDebug(ref ExportResolver exports)
		{
			lua_getstack =
				(delegate* unmanaged[Cdecl]<lua_State*, int, lua_Debug*, int>) exports.Resolve("lua_getstack");
			lua_getinfo =
				(delegate* unmanaged[Cdecl]<lua_State*, byte*, lua_Debug*, int>) exports.Resolve("lua_getinfo");
			lua_getlocal =
				(delegate* unmanaged[Cdecl]<lua_State*, lua_Debug*, int, byte*>) exports.Resolve("lua_getlocal");
			lua_setlocal =
				(delegate* unmanaged[Cdecl]<lua_State*, lua_Debug*, int, byte*>) exports.Resolve("lua_setlocal");
			lua_getupvalue =
				(delegate* unmanaged[Cdecl]<lua_State*, int, int, byte*>) exports.Resolve("lua_getupvalue");
			lua_setupvalue =
				(delegate* unmanaged[Cdecl]<lua_State*, int, int, byte*>) exports.Resolve("lua_setupvalue");
			lua_upvalueid = (delegate* unmanaged[Cdecl]<lua_State*, int, int, void*>) exports.Resolve("lua_upvalueid");
			lua_upvaluejoin =
				(delegate* unmanaged[Cdecl]<lua_State*, int, int, int, int, void>) exports.Resolve("lua_upvaluejoin");
			lua_sethook =
				(delegate* unmanaged[Cdecl]<lua_State*, lua_Hook, int, int, void>) exports.Resolve("lua_sethook");
			lua_gethook = (delegate* unmanaged[Cdecl]<lua_State*, lua_Hook>) exports.Resolve("lua_gethook");
			lua_gethookmask = (delegate* unmanaged[Cdecl]<lua_State*, int>) exports.Resolve("lua_gethookmask");
			lua_gethookcount = (delegate* unmanaged[Cdecl]<lua_State*, int>) exports.Resolve("lua_gethookcount");
		}
	}
}
