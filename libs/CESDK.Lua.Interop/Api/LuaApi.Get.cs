using System.Runtime.CompilerServices;
using CESDK.Lua.Interop.Types;

namespace CESDK.Lua.Interop.Api;

// lua.h "get functions (Lua -> stack)". The int results are the type tag of the pushed value.
public static unsafe partial class LuaApi
{
    /// <summary>
    ///     <c>int lua_getglobal (lua_State *L, const char *name)</c>. Pushes the value of a global and returns its type
    ///     tag.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="name">NUL-terminated global name.</param>
    /// <remarks>Stack: -0 +1. Raises: any (an <c>__index</c> metamethod on the globals table; memory for the key string).</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int lua_getglobal(lua_State* L, byte* name)
    {
        return s_table.lua_getglobal(L, name);
    }

    /// <summary>
    ///     <c>int lua_gettable (lua_State *L, int idx)</c>. Pops a key and pushes <c>t[key]</c> for the value at
    ///     <paramref name="idx" />; returns the type tag of the result.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="idx">Valid index of the indexed value.</param>
    /// <remarks>Stack: -1 +1. Raises: any (<c>__index</c>; indexing a value that cannot be indexed).</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int lua_gettable(lua_State* L, int idx)
    {
        return s_table.lua_gettable(L, idx);
    }

    /// <summary>
    ///     <c>int lua_getfield (lua_State *L, int idx, const char *k)</c>. Pushes <c>t[k]</c> for the value at
    ///     <paramref name="idx" />; returns the type tag of the result.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="idx">Valid index of the indexed value.</param>
    /// <param name="k">NUL-terminated field name.</param>
    /// <remarks>
    ///     Stack: -0 +1. Raises: any (<c>__index</c>, which is how every Cheat Engine object answers; indexing a value
    ///     that cannot be indexed).
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int lua_getfield(lua_State* L, int idx, byte* k)
    {
        return s_table.lua_getfield(L, idx, k);
    }

    /// <summary>
    ///     <c>int lua_geti (lua_State *L, int idx, lua_Integer n)</c>. Pushes <c>t[n]</c> for the value at
    ///     <paramref name="idx" />; returns the type tag of the result.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="idx">Valid index of the indexed value.</param>
    /// <param name="n">Integer key.</param>
    /// <remarks>Stack: -0 +1. Raises: any (<c>__index</c>; indexing a value that cannot be indexed).</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int lua_geti(lua_State* L, int idx, lua_Integer n)
    {
        return s_table.lua_geti(L, idx, n);
    }

    /// <summary>
    ///     <c>int lua_rawget (lua_State *L, int idx)</c>. Like <see cref="lua_gettable" /> without metamethods. The value
    ///     at <paramref name="idx" /> must be a table.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="idx">Valid index of a table.</param>
    /// <remarks>Stack: -1 +1. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int lua_rawget(lua_State* L, int idx)
    {
        return s_table.lua_rawget(L, idx);
    }

    /// <summary>
    ///     <c>int lua_rawgeti (lua_State *L, int idx, lua_Integer n)</c>. Pushes <c>t[n]</c> without metamethods; returns
    ///     its type tag. This is how a registry reference is resolved.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="idx">Valid index of a table (or <see cref="LUA_REGISTRYINDEX" />).</param>
    /// <param name="n">Integer key.</param>
    /// <remarks>Stack: -0 +1. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int lua_rawgeti(lua_State* L, int idx, lua_Integer n)
    {
        return s_table.lua_rawgeti(L, idx, n);
    }

    /// <summary>
    ///     <c>int lua_rawgetp (lua_State *L, int idx, const void *p)</c>. Pushes <c>t[p]</c> where the key is the light
    ///     userdata <paramref name="p" />, without metamethods; returns its type tag.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="idx">Valid index of a table.</param>
    /// <param name="p">Pointer used as key (the address of a static is the usual unique key).</param>
    /// <remarks>Stack: -0 +1. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int lua_rawgetp(lua_State* L, int idx, void* p)
    {
        return s_table.lua_rawgetp(L, idx, p);
    }

    /// <summary>
    ///     <c>void lua_createtable (lua_State *L, int narr, int nrec)</c>. Pushes a new empty table with preallocated
    ///     space.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="narr">Expected number of sequence elements.</param>
    /// <param name="nrec">Expected number of other elements.</param>
    /// <remarks>Stack: -0 +1. Raises: memory.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void lua_createtable(lua_State* L, int narr, int nrec)
    {
        s_table.lua_createtable(L, narr, nrec);
    }

    /// <summary>
    ///     <c>void *lua_newuserdata (lua_State *L, size_t sz)</c>. Allocates a block of <paramref name="sz" /> bytes owned by
    ///     Lua, pushes the full userdata that represents it, and returns the block address.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="sz">Block size in bytes.</param>
    /// <remarks>
    ///     Stack: -0 +1. Raises: memory. The block is not zeroed, never moves, and is freed by Lua's collector after an
    ///     optional <c>__gc</c> metamethod.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void* lua_newuserdata(lua_State* L, size_t sz)
    {
        return s_table.lua_newuserdata(L, sz);
    }

    /// <summary>
    ///     <c>int lua_getmetatable (lua_State *L, int objindex)</c>. Pushes the metatable of the value and returns 1;
    ///     returns 0 and pushes nothing when it has none.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="objindex">Acceptable index of the value.</param>
    /// <remarks>Stack: -0 +(0|1). Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int lua_getmetatable(lua_State* L, int objindex)
    {
        return s_table.lua_getmetatable(L, objindex);
    }

    /// <summary>
    ///     <c>int lua_getuservalue (lua_State *L, int idx)</c>. Pushes the Lua value attached to a full userdata; returns
    ///     its type tag.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="idx">Valid index of a full userdata.</param>
    /// <remarks>Stack: -0 +1. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int lua_getuservalue(lua_State* L, int idx)
    {
        return s_table.lua_getuservalue(L, idx);
    }

    internal partial struct Table
    {
        internal delegate* unmanaged[Cdecl]<lua_State*, byte*, int> lua_getglobal;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, int> lua_gettable;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, byte*, int> lua_getfield;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, lua_Integer, int> lua_geti;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, int> lua_rawget;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, lua_Integer, int> lua_rawgeti;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, void*, int> lua_rawgetp;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, int, void> lua_createtable;
        internal delegate* unmanaged[Cdecl]<lua_State*, size_t, void*> lua_newuserdata;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, int> lua_getmetatable;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, int> lua_getuservalue;

        private void LoadGet(ref ExportResolver exports)
        {
            lua_getglobal = (delegate* unmanaged[Cdecl]<lua_State*, byte*, int>)exports.Resolve("lua_getglobal");
            lua_gettable = (delegate* unmanaged[Cdecl]<lua_State*, int, int>)exports.Resolve("lua_gettable");
            lua_getfield = (delegate* unmanaged[Cdecl]<lua_State*, int, byte*, int>)exports.Resolve("lua_getfield");
            lua_geti = (delegate* unmanaged[Cdecl]<lua_State*, int, lua_Integer, int>)exports.Resolve("lua_geti");
            lua_rawget = (delegate* unmanaged[Cdecl]<lua_State*, int, int>)exports.Resolve("lua_rawget");
            lua_rawgeti = (delegate* unmanaged[Cdecl]<lua_State*, int, lua_Integer, int>)exports.Resolve("lua_rawgeti");
            lua_rawgetp = (delegate* unmanaged[Cdecl]<lua_State*, int, void*, int>)exports.Resolve("lua_rawgetp");
            lua_createtable =
                (delegate* unmanaged[Cdecl]<lua_State*, int, int, void>)exports.Resolve("lua_createtable");
            lua_newuserdata = (delegate* unmanaged[Cdecl]<lua_State*, size_t, void*>)exports.Resolve("lua_newuserdata");
            lua_getmetatable = (delegate* unmanaged[Cdecl]<lua_State*, int, int>)exports.Resolve("lua_getmetatable");
            lua_getuservalue = (delegate* unmanaged[Cdecl]<lua_State*, int, int>)exports.Resolve("lua_getuservalue");
        }
    }
}
