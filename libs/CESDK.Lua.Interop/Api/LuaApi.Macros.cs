using System;
using System.Runtime.CompilerServices;
using CESDK.Lua.Interop.Types;

namespace CESDK.Lua.Interop.Api;

// The function-like macros of lua.h as inlined static methods. They add no behaviour of their own: each body is the
// macro's expansion. The type predicates return bool because they are managed code (the exported predicates return
// C int because that is their ABI). lua_yield is absent because lua_yieldk is not bound.
public static unsafe partial class LuaApi
{
    /// <summary><c>lua_upvalueindex(i)</c>: pseudo-index of upvalue <paramref name="i" /> (1-based) of the running C closure.</summary>
    /// <param name="i">Upvalue number, 1 to 255.</param>
    /// <remarks>Pure arithmetic: usable without a bound table.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int lua_upvalueindex(int i)
    {
        return LUA_REGISTRYINDEX - i;
    }

    /// <summary><c>lua_call(L,n,r)</c>: <see cref="lua_callk" /> without continuation. Unprotected: see the warning there.</summary>
    /// <param name="L">The state.</param>
    /// <param name="n">Number of arguments.</param>
    /// <param name="r">Number of results, or <see cref="LUA_MULTRET" />.</param>
    /// <remarks>Stack: -(n+1) +r. Raises: any.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void lua_call(lua_State* L, int n, int r)
    {
        lua_callk(L, n, r, 0, null);
    }

    /// <summary><c>lua_pcall(L,n,r,f)</c>: <see cref="lua_pcallk" /> without continuation.</summary>
    /// <param name="L">The state.</param>
    /// <param name="n">Number of arguments.</param>
    /// <param name="r">Number of results, or <see cref="LUA_MULTRET" />.</param>
    /// <param name="f">0, or the stack index of a message handler.</param>
    /// <remarks>Stack: -(n+1) +(r|1). Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int lua_pcall(lua_State* L, int n, int r, int f)
    {
        return lua_pcallk(L, n, r, f, 0, null);
    }

    /// <summary>
    ///     <c>lua_getextraspace(L)</c>: address of the <see cref="LUA_EXTRASPACE" /> bytes of scratch memory that precede
    ///     the state. A new thread starts with a copy of the main thread's area.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <remarks>Pure arithmetic. Never write there for a state that belongs to Cheat Engine: the host may use the area itself.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void* lua_getextraspace(lua_State* L)
    {
        return (byte*)L - sizeof(void*);
    }

    /// <summary><c>lua_tonumber(L,i)</c>: <see cref="lua_tonumberx" /> without the success flag (0 is ambiguous).</summary>
    /// <param name="L">The state.</param>
    /// <param name="i">Acceptable index.</param>
    /// <remarks>Stack: -0 +0. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static lua_Number lua_tonumber(lua_State* L, int i)
    {
        return lua_tonumberx(L, i, null);
    }

    /// <summary><c>lua_tointeger(L,i)</c>: <see cref="lua_tointegerx" /> without the success flag (0 is ambiguous).</summary>
    /// <param name="L">The state.</param>
    /// <param name="i">Acceptable index.</param>
    /// <remarks>Stack: -0 +0. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static lua_Integer lua_tointeger(lua_State* L, int i)
    {
        return lua_tointegerx(L, i, null);
    }

    /// <summary><c>lua_pop(L,n)</c>: drops the top <paramref name="n" /> elements.</summary>
    /// <param name="L">The state.</param>
    /// <param name="n">Number of elements.</param>
    /// <remarks>Stack: -n +0. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void lua_pop(lua_State* L, int n)
    {
        lua_settop(L, -n - 1);
    }

    /// <summary><c>lua_newtable(L)</c>: pushes a new empty table without preallocation.</summary>
    /// <param name="L">The state.</param>
    /// <remarks>Stack: -0 +1. Raises: memory.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void lua_newtable(lua_State* L)
    {
        lua_createtable(L, 0, 0);
    }

    /// <summary><c>lua_register(L,n,f)</c>: assigns the C function <paramref name="f" /> to the global <paramref name="n" />.</summary>
    /// <param name="L">The state.</param>
    /// <param name="n">NUL-terminated global name.</param>
    /// <param name="f">The function; see <see cref="lua_pushcclosure" /> for the rules a managed one must follow.</param>
    /// <remarks>Stack: -0 +0. Raises: any (through <see cref="lua_setglobal" />).</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void lua_register(lua_State* L, byte* n, lua_CFunction f)
    {
        lua_pushcfunction(L, f);
        lua_setglobal(L, n);
    }

    /// <summary><c>lua_pushcfunction(L,f)</c>: pushes a C function without upvalues (nothing is allocated).</summary>
    /// <param name="L">The state.</param>
    /// <param name="f">The function; see <see cref="lua_pushcclosure" /> for the rules a managed one must follow.</param>
    /// <remarks>Stack: -0 +1. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void lua_pushcfunction(lua_State* L, lua_CFunction f)
    {
        lua_pushcclosure(L, f, 0);
    }

    /// <summary><c>lua_isfunction(L,n)</c>: whether the value is a function (Lua or C).</summary>
    /// <param name="L">The state.</param>
    /// <param name="n">Acceptable index.</param>
    /// <remarks>Stack: -0 +0. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool lua_isfunction(lua_State* L, int n)
    {
        return lua_type(L, n) == LUA_TFUNCTION;
    }

    /// <summary><c>lua_istable(L,n)</c>: whether the value is a table.</summary>
    /// <param name="L">The state.</param>
    /// <param name="n">Acceptable index.</param>
    /// <remarks>Stack: -0 +0. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool lua_istable(lua_State* L, int n)
    {
        return lua_type(L, n) == LUA_TTABLE;
    }

    /// <summary><c>lua_islightuserdata(L,n)</c>: whether the value is a light userdata.</summary>
    /// <param name="L">The state.</param>
    /// <param name="n">Acceptable index.</param>
    /// <remarks>Stack: -0 +0. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool lua_islightuserdata(lua_State* L, int n)
    {
        return lua_type(L, n) == LUA_TLIGHTUSERDATA;
    }

    /// <summary><c>lua_isnil(L,n)</c>: whether the value is nil.</summary>
    /// <param name="L">The state.</param>
    /// <param name="n">Acceptable index.</param>
    /// <remarks>Stack: -0 +0. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool lua_isnil(lua_State* L, int n)
    {
        return lua_type(L, n) == LUA_TNIL;
    }

    /// <summary><c>lua_isboolean(L,n)</c>: whether the value is a boolean.</summary>
    /// <param name="L">The state.</param>
    /// <param name="n">Acceptable index.</param>
    /// <remarks>Stack: -0 +0. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool lua_isboolean(lua_State* L, int n)
    {
        return lua_type(L, n) == LUA_TBOOLEAN;
    }

    /// <summary><c>lua_isthread(L,n)</c>: whether the value is a thread.</summary>
    /// <param name="L">The state.</param>
    /// <param name="n">Acceptable index.</param>
    /// <remarks>Stack: -0 +0. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool lua_isthread(lua_State* L, int n)
    {
        return lua_type(L, n) == LUA_TTHREAD;
    }

    /// <summary><c>lua_isnone(L,n)</c>: whether the index is beyond the top of the frame.</summary>
    /// <param name="L">The state.</param>
    /// <param name="n">Acceptable index.</param>
    /// <remarks>Stack: -0 +0. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool lua_isnone(lua_State* L, int n)
    {
        return lua_type(L, n) == LUA_TNONE;
    }

    /// <summary><c>lua_isnoneornil(L,n)</c>: whether the index is beyond the top or holds nil (an absent optional argument).</summary>
    /// <param name="L">The state.</param>
    /// <param name="n">Acceptable index.</param>
    /// <remarks>Stack: -0 +0. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool lua_isnoneornil(lua_State* L, int n)
    {
        return lua_type(L, n) <= 0;
    }

    /// <summary>
    ///     <c>lua_pushliteral(L,s)</c>: pushes a string whose length is known without scanning. The C macro takes a string
    ///     literal; the managed equivalent of that is a <c>"..."u8</c> literal, but any span works (NULs included).
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="s">The bytes; copied by Lua during the call.</param>
    /// <remarks>Stack: -0 +1. Raises: memory.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void lua_pushliteral(lua_State* L, ReadOnlySpan<byte> s)
    {
        // An empty span pins to null; Lua is handed a valid address anyway, whatever it does with a zero length.
        byte empty = 0;
        fixed (byte* p = s)
        {
            _ = lua_pushlstring(L, p is null ? &empty : p, (size_t)s.Length);
        }
    }

    /// <summary>
    ///     <c>lua_pushglobaltable(L)</c>: pushes the globals table; the result is the macro's value, the type tag (
    ///     <see cref="LUA_TTABLE" />).
    /// </summary>
    /// <param name="L">The state.</param>
    /// <remarks>Stack: -0 +1. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int lua_pushglobaltable(lua_State* L)
    {
        return lua_rawgeti(L, LUA_REGISTRYINDEX, LUA_RIDX_GLOBALS);
    }

    /// <summary>
    ///     <c>lua_tostring(L,i)</c>: <see cref="lua_tolstring" /> without the length; only good for text known to contain
    ///     no NUL.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="i">Acceptable index.</param>
    /// <remarks>Stack: -0 +0. Raises: memory. Same in-place conversion and pointer lifetime as <see cref="lua_tolstring" />.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte* lua_tostring(lua_State* L, int i)
    {
        return lua_tolstring(L, i, null);
    }

    /// <summary>
    ///     <c>lua_insert(L,idx)</c>: moves the top element down to <paramref name="idx" />, shifting the elements above
    ///     it up.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="idx">Valid stack index (not a pseudo-index).</param>
    /// <remarks>Stack: -1 +1. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void lua_insert(lua_State* L, int idx)
    {
        lua_rotate(L, idx, 1);
    }

    /// <summary><c>lua_remove(L,idx)</c>: removes the element at <paramref name="idx" />, shifting the elements above it down.</summary>
    /// <param name="L">The state.</param>
    /// <param name="idx">Valid stack index (not a pseudo-index).</param>
    /// <remarks>Stack: -1 +0. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void lua_remove(lua_State* L, int idx)
    {
        lua_rotate(L, idx, -1);
        lua_pop(L, 1);
    }

    /// <summary><c>lua_replace(L,idx)</c>: pops the top element into the slot <paramref name="idx" />; nothing shifts.</summary>
    /// <param name="L">The state.</param>
    /// <param name="idx">Valid index.</param>
    /// <remarks>Stack: -1 +0. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void lua_replace(lua_State* L, int idx)
    {
        lua_copy(L, -1, idx);
        lua_pop(L, 1);
    }
}
