using System.Runtime.CompilerServices;
using CheatEngine.SDK.Lua.Interop.Types;

namespace CheatEngine.SDK.Lua.Interop.Api;

// lua.h "state manipulation" + luaL_newstate.
public static unsafe partial class LuaApi
{
    /// <summary>
    ///     <c>lua_State *lua_newstate (lua_Alloc f, void *ud)</c>. Creates an independent state that allocates through
    ///     <paramref name="f" />. Returns null when the first allocations fail.
    /// </summary>
    /// <param name="f">
    ///     Allocator with <c>realloc</c>-like semantics; must stay callable until <see cref="lua_close" />
    ///     returns.
    /// </param>
    /// <param name="ud">Opaque pointer passed back to every call of <paramref name="f" />.</param>
    /// <remarks>Stack: n/a. Raises: never. The caller owns the state and must close it on the thread that uses it.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static lua_State* lua_newstate(lua_Alloc f, void* ud)
    {
        return s_table.lua_newstate(f, ud);
    }

    /// <summary>
    ///     <c>void lua_close (lua_State *L)</c>. Destroys a main state: runs pending <c>__gc</c> metamethods and frees all
    ///     memory. Only for states the caller created; never for a state that belongs to Cheat Engine.
    /// </summary>
    /// <param name="L">A main state (not a coroutine thread).</param>
    /// <remarks>Stack: n/a. Raises: never. Every pointer previously obtained from the state dangles afterwards.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void lua_close(lua_State* L)
    {
        s_table.lua_close(L);
    }

    /// <summary>
    ///     <c>lua_State *lua_newthread (lua_State *L)</c>. Creates a coroutine thread that shares the globals and registry
    ///     of <paramref name="L" />, pushes it, and returns it.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <remarks>
    ///     Stack: -0 +1. Raises: memory. The thread is garbage collected: keep it referenced (stack, registry) while in
    ///     use.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static lua_State* lua_newthread(lua_State* L)
    {
        return s_table.lua_newthread(L);
    }

    /// <summary>
    ///     <c>lua_CFunction lua_atpanic (lua_State *L, lua_CFunction panicf)</c>. Installs the function Lua calls when an
    ///     error is raised outside any protected call, and returns the previous one.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="panicf">New panic function; when it returns, Lua aborts the process.</param>
    /// <remarks>Stack: -0 +0. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static lua_CFunction lua_atpanic(lua_State* L, lua_CFunction panicf)
    {
        return s_table.lua_atpanic(L, panicf);
    }

    /// <summary>
    ///     <c>const lua_Number *lua_version (lua_State *L)</c>. Address of the version number stored in the state's global
    ///     state, or of the library's own copy when <paramref name="L" /> is null. The value is <see cref="LUA_VERSION_NUM" />
    ///     for 5.3.
    /// </summary>
    /// <param name="L">The state, or null.</param>
    /// <remarks>Stack: -0 +0. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static lua_Number* lua_version(lua_State* L)
    {
        return s_table.lua_version(L);
    }

    /// <summary>
    ///     <c>lua_State *luaL_newstate (void)</c>. Creates a state with the C allocator of the Lua library and a panic
    ///     function that prints to stderr. Returns null on allocation failure. No library is opened.
    /// </summary>
    /// <remarks>Stack: n/a. Raises: never. The caller owns the state and must <see cref="lua_close" /> it.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static lua_State* luaL_newstate()
    {
        return s_table.luaL_newstate();
    }

    internal partial struct Table
    {
        internal delegate* unmanaged[Cdecl]<lua_Alloc, void*, lua_State*> lua_newstate;
        internal delegate* unmanaged[Cdecl]<lua_State*, void> lua_close;
        internal delegate* unmanaged[Cdecl]<lua_State*, lua_State*> lua_newthread;
        internal delegate* unmanaged[Cdecl]<lua_State*, lua_CFunction, lua_CFunction> lua_atpanic;
        internal delegate* unmanaged[Cdecl]<lua_State*, lua_Number*> lua_version;
        internal delegate* unmanaged[Cdecl]<lua_State*> luaL_newstate;

        private void LoadState(ref ExportResolver exports)
        {
            lua_newstate = (delegate* unmanaged[Cdecl]<lua_Alloc, void*, lua_State*>)exports.Resolve("lua_newstate");
            lua_close = (delegate* unmanaged[Cdecl]<lua_State*, void>)exports.Resolve("lua_close");
            lua_newthread = (delegate* unmanaged[Cdecl]<lua_State*, lua_State*>)exports.Resolve("lua_newthread");
            lua_atpanic =
                (delegate* unmanaged[Cdecl]<lua_State*, lua_CFunction, lua_CFunction>)exports.Resolve("lua_atpanic");
            lua_version = (delegate* unmanaged[Cdecl]<lua_State*, lua_Number*>)exports.Resolve("lua_version");
            luaL_newstate = (delegate* unmanaged[Cdecl]<lua_State*>)exports.Resolve("luaL_newstate");
        }
    }
}
