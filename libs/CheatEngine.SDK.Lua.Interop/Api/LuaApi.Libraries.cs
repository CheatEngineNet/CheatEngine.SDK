using System.Runtime.CompilerServices;
using CheatEngine.SDK.Lua.Interop.Types;

namespace CheatEngine.SDK.Lua.Interop.Api;

// lualib.h. The openers are lua_CFunctions meant to be run by Lua (through luaL_requiref), not called directly, so
// they are exposed as function-pointer values. luaopen_bit32 is not bound: the library is deprecated in 5.3 and a
// default build only ships a stub for it.
public static unsafe partial class LuaApi
{
    /// <summary>
    ///     <c>int luaopen_base (lua_State *L)</c> as a value for <see cref="luaL_requiref" /> (module name "_G"): the
    ///     basic functions.
    /// </summary>
    public static lua_CFunction luaopen_base => s_table.luaopen_base;

    /// <summary>
    ///     <c>int luaopen_coroutine (lua_State *L)</c> as a value for <see cref="luaL_requiref" /> (
    ///     <see cref="LUA_COLIBNAME" />).
    /// </summary>
    public static lua_CFunction luaopen_coroutine => s_table.luaopen_coroutine;

    /// <summary>
    ///     <c>int luaopen_table (lua_State *L)</c> as a value for <see cref="luaL_requiref" /> (
    ///     <see cref="LUA_TABLIBNAME" />).
    /// </summary>
    public static lua_CFunction luaopen_table => s_table.luaopen_table;

    /// <summary>
    ///     <c>int luaopen_io (lua_State *L)</c> as a value for <see cref="luaL_requiref" /> (<see cref="LUA_IOLIBNAME" />
    ///     ). Gives scripts file access.
    /// </summary>
    public static lua_CFunction luaopen_io => s_table.luaopen_io;

    /// <summary>
    ///     <c>int luaopen_os (lua_State *L)</c> as a value for <see cref="luaL_requiref" /> (<see cref="LUA_OSLIBNAME" />
    ///     ). Gives scripts process and file-system access.
    /// </summary>
    public static lua_CFunction luaopen_os => s_table.luaopen_os;

    /// <summary>
    ///     <c>int luaopen_string (lua_State *L)</c> as a value for <see cref="luaL_requiref" /> (
    ///     <see cref="LUA_STRLIBNAME" />).
    /// </summary>
    public static lua_CFunction luaopen_string => s_table.luaopen_string;

    /// <summary>
    ///     <c>int luaopen_utf8 (lua_State *L)</c> as a value for <see cref="luaL_requiref" /> (
    ///     <see cref="LUA_UTF8LIBNAME" />).
    /// </summary>
    public static lua_CFunction luaopen_utf8 => s_table.luaopen_utf8;

    /// <summary>
    ///     <c>int luaopen_math (lua_State *L)</c> as a value for <see cref="luaL_requiref" /> (
    ///     <see cref="LUA_MATHLIBNAME" />).
    /// </summary>
    public static lua_CFunction luaopen_math => s_table.luaopen_math;

    /// <summary>
    ///     <c>int luaopen_debug (lua_State *L)</c> as a value for <see cref="luaL_requiref" /> (
    ///     <see cref="LUA_DBLIBNAME" />).
    /// </summary>
    public static lua_CFunction luaopen_debug => s_table.luaopen_debug;

    /// <summary>
    ///     <c>int luaopen_package (lua_State *L)</c> as a value for <see cref="luaL_requiref" /> (
    ///     <see cref="LUA_LOADLIBNAME" />). Gives scripts <c>require</c> and native library loading.
    /// </summary>
    public static lua_CFunction luaopen_package => s_table.luaopen_package;

    /// <summary>
    ///     <c>void luaL_openlibs (lua_State *L)</c>. Opens every standard library into the state, as the stand-alone
    ///     interpreter does.
    /// </summary>
    /// <param name="L">A state the caller created.</param>
    /// <remarks>Stack: -0 +0. Raises: any (in practice memory only).</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void luaL_openlibs(lua_State* L)
    {
        s_table.luaL_openlibs(L);
    }

    internal partial struct Table
    {
        internal delegate* unmanaged[Cdecl]<lua_State*, void> luaL_openlibs;
        internal lua_CFunction luaopen_base;
        internal lua_CFunction luaopen_coroutine;
        internal lua_CFunction luaopen_table;
        internal lua_CFunction luaopen_io;
        internal lua_CFunction luaopen_os;
        internal lua_CFunction luaopen_string;
        internal lua_CFunction luaopen_utf8;
        internal lua_CFunction luaopen_math;
        internal lua_CFunction luaopen_debug;
        internal lua_CFunction luaopen_package;

        private void LoadLibraries(ref ExportResolver exports)
        {
            luaL_openlibs = (delegate* unmanaged[Cdecl]<lua_State*, void>)exports.Resolve("luaL_openlibs");
            luaopen_base = (lua_CFunction)exports.Resolve("luaopen_base");
            luaopen_coroutine = (lua_CFunction)exports.Resolve("luaopen_coroutine");
            luaopen_table = (lua_CFunction)exports.Resolve("luaopen_table");
            luaopen_io = (lua_CFunction)exports.Resolve("luaopen_io");
            luaopen_os = (lua_CFunction)exports.Resolve("luaopen_os");
            luaopen_string = (lua_CFunction)exports.Resolve("luaopen_string");
            luaopen_utf8 = (lua_CFunction)exports.Resolve("luaopen_utf8");
            luaopen_math = (lua_CFunction)exports.Resolve("luaopen_math");
            luaopen_debug = (lua_CFunction)exports.Resolve("luaopen_debug");
            luaopen_package = (lua_CFunction)exports.Resolve("luaopen_package");
        }
    }
}
