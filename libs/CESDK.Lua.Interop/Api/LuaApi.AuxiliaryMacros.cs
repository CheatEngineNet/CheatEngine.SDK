using System.Runtime.CompilerServices;
using CESDK.Lua.Interop.Types;

namespace CESDK.Lua.Interop.Api;

// The function-like macros of lauxlib.h that stay usable from managed code. Not provided, because they expand to
// raising or unbound functions: luaL_checkversion, luaL_newlib, luaL_newlibtable (needs a C array size),
// luaL_argcheck, luaL_checkstring, luaL_optstring, luaL_opt, and the luaL_Buffer macros.
public static unsafe partial class LuaApi
{
    /// <summary><c>luaL_loadfile(L,f)</c>: <see cref="luaL_loadfilex" /> accepting text and binary chunks.</summary>
    /// <param name="L">The state.</param>
    /// <param name="f">NUL-terminated path in the C runtime's encoding, or null for stdin.</param>
    /// <remarks>Stack: -0 +1. Raises: memory.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int luaL_loadfile(lua_State* L, byte* f)
    {
        return luaL_loadfilex(L, f, null);
    }

    /// <summary><c>luaL_loadbuffer(L,s,sz,n)</c>: <see cref="luaL_loadbufferx" /> accepting text and binary chunks.</summary>
    /// <param name="L">The state.</param>
    /// <param name="s">Chunk bytes.</param>
    /// <param name="sz">Size in bytes.</param>
    /// <param name="n">NUL-terminated chunk name.</param>
    /// <remarks>Stack: -0 +1. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int luaL_loadbuffer(lua_State* L, byte* s, size_t sz, byte* n)
    {
        return luaL_loadbufferx(L, s, sz, n, null);
    }

    /// <summary>
    ///     <c>luaL_typename(L,i)</c>: name of the type of the value at <paramref name="i" />, as a NUL-terminated static
    ///     string of the library.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="i">Acceptable index.</param>
    /// <remarks>Stack: -0 +0. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte* luaL_typename(lua_State* L, int i)
    {
        return lua_typename(L, lua_type(L, i));
    }

    /// <summary>
    ///     <c>luaL_dostring(L,s)</c>: loads and runs a NUL-terminated source string under a protected call, keeping all
    ///     results. Like the C macro (<c>load || pcall</c>) the result is a truth value, not a status: 0 on success, 1
    ///     when loading or running failed, in which case the error value is on top of the stack.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="s">NUL-terminated Lua source.</param>
    /// <remarks>Stack: -0 +?. Raises: never.</remarks>
    public static int luaL_dostring(lua_State* L, byte* s)
    {
        return luaL_loadstring(L, s) != LUA_OK || lua_pcall(L, 0, LUA_MULTRET, 0) != LUA_OK ? 1 : 0;
    }

    /// <summary>
    ///     <c>luaL_dofile(L,fn)</c>: loads and runs a file under a protected call, keeping all results. Same truth-value
    ///     result as <see cref="luaL_dostring" />.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="fn">NUL-terminated path in the C runtime's encoding, or null for stdin.</param>
    /// <remarks>Stack: -0 +?. Raises: memory.</remarks>
    public static int luaL_dofile(lua_State* L, byte* fn)
    {
        return luaL_loadfile(L, fn) != LUA_OK || lua_pcall(L, 0, LUA_MULTRET, 0) != LUA_OK ? 1 : 0;
    }

    /// <summary>
    ///     <c>luaL_getmetatable(L,n)</c>: pushes the registry entry named <paramref name="n" /> (a metatable created by
    ///     <see cref="luaL_newmetatable" />, or nil); the result is its type tag.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="n">NUL-terminated type name.</param>
    /// <remarks>Stack: -0 +1. Raises: memory (the registry has no metatable, so no metamethod can run).</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int luaL_getmetatable(lua_State* L, byte* n)
    {
        return lua_getfield(L, LUA_REGISTRYINDEX, n);
    }
}
