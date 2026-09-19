using System.Runtime.CompilerServices;
using CESDK.Lua.Interop.Types;

namespace CESDK.Lua.Interop.Api;

// lua.h "set functions (stack -> Lua)".
public static unsafe partial class LuaApi
{
    /// <summary><c>void lua_setglobal (lua_State *L, const char *name)</c>. Pops a value and assigns it to a global.</summary>
    /// <param name="l">The state.</param>
    /// <param name="name">NUL-terminated global name.</param>
    /// <remarks>Stack: -1 +0. Raises: any (a <c>__newindex</c> metamethod on the globals table; memory).</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void lua_setglobal(lua_State* l, byte* name)
    {
        s_table.lua_setglobal(l, name);
    }

    /// <summary>
    ///     <c>void lua_settable (lua_State *L, int idx)</c>. Does <c>t[key] = value</c> for the value at
    ///     <paramref name="idx" />, with the value on top and the key below it; pops both.
    /// </summary>
    /// <param name="l">The state.</param>
    /// <param name="idx">Valid index of the indexed value.</param>
    /// <remarks>Stack: -2 +0. Raises: any (<c>__newindex</c>; nil or NaN key; memory).</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void lua_settable(lua_State* l, int idx)
    {
        s_table.lua_settable(l, idx);
    }

    /// <summary>
    ///     <c>void lua_setfield (lua_State *L, int idx, const char *k)</c>. Does <c>t[k] = value</c> with the value on
    ///     top; pops it.
    /// </summary>
    /// <param name="l">The state.</param>
    /// <param name="idx">Valid index of the indexed value.</param>
    /// <param name="k">NUL-terminated field name.</param>
    /// <remarks>
    ///     Stack: -1 +0. Raises: any (<c>__newindex</c>, which is how Cheat Engine object properties are written;
    ///     memory).
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void lua_setfield(lua_State* l, int idx, byte* k)
    {
        s_table.lua_setfield(l, idx, k);
    }

    /// <summary>
    ///     <c>void lua_seti (lua_State *L, int idx, lua_Integer n)</c>. Does <c>t[n] = value</c> with the value on top;
    ///     pops it.
    /// </summary>
    /// <param name="l">The state.</param>
    /// <param name="idx">Valid index of the indexed value.</param>
    /// <param name="n">Integer key.</param>
    /// <remarks>Stack: -1 +0. Raises: any (<c>__newindex</c>; memory).</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void lua_seti(lua_State* l, int idx, lua_Integer n)
    {
        s_table.lua_seti(l, idx, n);
    }

    /// <summary>
    ///     <c>void lua_rawset (lua_State *L, int idx)</c>. Like <see cref="lua_settable" /> without metamethods. The
    ///     value at <paramref name="idx" /> must be a table.
    /// </summary>
    /// <param name="l">The state.</param>
    /// <param name="idx">Valid index of a table.</param>
    /// <remarks>Stack: -2 +0. Raises: memory (and on a nil or NaN key).</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void lua_rawset(lua_State* l, int idx)
    {
        s_table.lua_rawset(l, idx);
    }

    /// <summary>
    ///     <c>void lua_rawseti (lua_State *L, int idx, lua_Integer n)</c>. Does <c>t[n] = value</c> without metamethods,
    ///     with the value on top; pops it.
    /// </summary>
    /// <param name="l">The state.</param>
    /// <param name="idx">Valid index of a table.</param>
    /// <param name="n">Integer key.</param>
    /// <remarks>Stack: -1 +0. Raises: memory.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void lua_rawseti(lua_State* l, int idx, lua_Integer n)
    {
        s_table.lua_rawseti(l, idx, n);
    }

    /// <summary>
    ///     <c>void lua_rawsetp (lua_State *L, int idx, const void *p)</c>. Does <c>t[p] = value</c> where the key is the
    ///     light userdata <paramref name="p" />, without metamethods; pops the value.
    /// </summary>
    /// <param name="l">The state.</param>
    /// <param name="idx">Valid index of a table.</param>
    /// <param name="p">Pointer used as key.</param>
    /// <remarks>Stack: -1 +0. Raises: memory.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void lua_rawsetp(lua_State* l, int idx, void* p)
    {
        s_table.lua_rawsetp(l, idx, p);
    }

    /// <summary>
    ///     <c>int lua_setmetatable (lua_State *L, int objindex)</c>. Pops a table or nil and makes it the metatable of
    ///     the value at <paramref name="objindex" />. The result carries no information in 5.3 (always 1).
    /// </summary>
    /// <param name="l">The state.</param>
    /// <param name="objindex">Acceptable index of the value.</param>
    /// <remarks>Stack: -1 +0. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int lua_setmetatable(lua_State* l, int objindex)
    {
        return s_table.lua_setmetatable(l, objindex);
    }

    /// <summary>
    ///     <c>void lua_setuservalue (lua_State *L, int idx)</c>. Pops a value and attaches it to the full userdata at
    ///     <paramref name="idx" />.
    /// </summary>
    /// <param name="l">The state.</param>
    /// <param name="idx">Valid index of a full userdata.</param>
    /// <remarks>Stack: -1 +0. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void lua_setuservalue(lua_State* l, int idx)
    {
        s_table.lua_setuservalue(l, idx);
    }

    internal partial struct Table
    {
        internal delegate* unmanaged[Cdecl]<lua_State*, byte*, void> lua_setglobal;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, void> lua_settable;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, byte*, void> lua_setfield;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, lua_Integer, void> lua_seti;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, void> lua_rawset;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, lua_Integer, void> lua_rawseti;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, void*, void> lua_rawsetp;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, int> lua_setmetatable;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, void> lua_setuservalue;

        private void LoadSet(ref ExportResolver exports)
        {
            lua_setglobal = (delegate* unmanaged[Cdecl]<lua_State*, byte*, void>)exports.Resolve("lua_setglobal");
            lua_settable = (delegate* unmanaged[Cdecl]<lua_State*, int, void>)exports.Resolve("lua_settable");
            lua_setfield = (delegate* unmanaged[Cdecl]<lua_State*, int, byte*, void>)exports.Resolve("lua_setfield");
            lua_seti = (delegate* unmanaged[Cdecl]<lua_State*, int, lua_Integer, void>)exports.Resolve("lua_seti");
            lua_rawset = (delegate* unmanaged[Cdecl]<lua_State*, int, void>)exports.Resolve("lua_rawset");
            lua_rawseti =
                (delegate* unmanaged[Cdecl]<lua_State*, int, lua_Integer, void>)exports.Resolve("lua_rawseti");
            lua_rawsetp = (delegate* unmanaged[Cdecl]<lua_State*, int, void*, void>)exports.Resolve("lua_rawsetp");
            lua_setmetatable = (delegate* unmanaged[Cdecl]<lua_State*, int, int>)exports.Resolve("lua_setmetatable");
            lua_setuservalue = (delegate* unmanaged[Cdecl]<lua_State*, int, void>)exports.Resolve("lua_setuservalue");
        }
    }
}
