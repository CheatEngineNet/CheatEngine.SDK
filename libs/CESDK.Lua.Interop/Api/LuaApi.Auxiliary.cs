using System.Runtime.CompilerServices;
using CESDK.Lua.Interop.Types;

namespace CESDK.Lua.Interop.Api;

// lauxlib.h, minus what cannot or must not be used from managed code:
//   luaL_error, lua_pushfstring family      C varargs;
//   luaL_check*, luaL_opt*, luaL_arg*       report failure by raising, i.e. they longjmp through the calling frame;
//   luaL_Buffer functions                   raise on overflow, and Cheat Engine's DLL exports luaL_buffinit only with
//                                           a C++-decorated name;
//   luaL_fileresult, luaL_execresult        helpers for the C runtime's errno.
public static unsafe partial class LuaApi
{
    /// <summary>
    ///     <c>int luaL_loadbufferx (lua_State *L, const char *buff, size_t sz, const char *name, const char *mode)</c>.
    ///     Compiles a chunk held in memory and pushes the function, or an error message. Returns <see cref="LUA_OK" />,
    ///     <see cref="LUA_ERRSYNTAX" />, <see cref="LUA_ERRMEM" /> or <see cref="LUA_ERRGCMM" />. The chunk is not run.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="buff">Chunk bytes (source text or binary); only read during the call, no terminator needed.</param>
    /// <param name="sz">Size in bytes.</param>
    /// <param name="name">NUL-terminated chunk name for messages ("=name" shows as is).</param>
    /// <param name="mode">NUL-terminated "t", "b" or "bt"; null means both. Pass "t" for anything that is meant to be source.</param>
    /// <remarks>Stack: -0 +1. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int luaL_loadbufferx(lua_State* L, byte* buff, size_t sz, byte* name, byte* mode)
    {
        return s_table.luaL_loadbufferx(L, buff, sz, name, mode);
    }

    /// <summary>
    ///     <c>int luaL_loadstring (lua_State *L, const char *s)</c>. <see cref="luaL_loadbufferx" /> over a
    ///     NUL-terminated source string, which also serves as the chunk name.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="s">NUL-terminated Lua source.</param>
    /// <remarks>Stack: -0 +1. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int luaL_loadstring(lua_State* L, byte* s)
    {
        return s_table.luaL_loadstring(L, s);
    }

    /// <summary>
    ///     <c>int luaL_loadfilex (lua_State *L, const char *filename, const char *mode)</c>. Compiles a file (stdin when
    ///     <paramref name="filename" /> is null). Returns a load status or <see cref="LUA_ERRFILE" />; pushes the function or
    ///     a message.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="filename">
    ///     NUL-terminated path handed to the C runtime's <c>fopen</c>: on Windows that is the ANSI code
    ///     page, not UTF-8.
    /// </param>
    /// <param name="mode">NUL-terminated "t", "b" or "bt"; null means both.</param>
    /// <remarks>Stack: -0 +1. Raises: memory.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int luaL_loadfilex(lua_State* L, byte* filename, byte* mode)
    {
        return s_table.luaL_loadfilex(L, filename, mode);
    }

    /// <summary>
    ///     <c>int luaL_ref (lua_State *L, int t)</c>. Pops a value, stores it in the table at <paramref name="t" /> under a
    ///     fresh integer key and returns that key; <see cref="LUA_REFNIL" /> for nil (nothing stored).
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="t">Valid index of the table, normally <see cref="LUA_REGISTRYINDEX" />.</param>
    /// <remarks>
    ///     Stack: -1 +0. Raises: memory. The reference keeps the value alive until <see cref="luaL_unref" />; it belongs
    ///     to the global state, so it is valid from every thread of that state and dies with it.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int luaL_ref(lua_State* L, int t)
    {
        return s_table.luaL_ref(L, t);
    }

    /// <summary>
    ///     <c>void luaL_unref (lua_State *L, int t, int ref)</c>. Releases a reference so that its key can be reused.
    ///     <see cref="LUA_NOREF" /> and <see cref="LUA_REFNIL" /> are ignored.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="t">Valid index of the table the reference was created in.</param>
    /// <param name="ref">The reference. Releasing the same live reference twice corrupts the free list.</param>
    /// <remarks>Stack: -0 +0. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void luaL_unref(lua_State* L, int t, int @ref)
    {
        s_table.luaL_unref(L, t, @ref);
    }

    /// <summary>
    ///     <c>int luaL_getmetafield (lua_State *L, int obj, const char *e)</c>. Pushes field <paramref name="e" /> of the
    ///     value's metatable and returns its type tag; returns <see cref="LUA_TNIL" /> and pushes nothing when there is no
    ///     metatable or no such field.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="obj">Acceptable index of the value.</param>
    /// <param name="e">NUL-terminated field name.</param>
    /// <remarks>Stack: -0 +(0|1). Raises: memory.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int luaL_getmetafield(lua_State* L, int obj, byte* e)
    {
        return s_table.luaL_getmetafield(L, obj, e);
    }

    /// <summary>
    ///     <c>int luaL_callmeta (lua_State *L, int obj, const char *e)</c>. Calls metamethod <paramref name="e" /> with
    ///     the value as its only argument, pushes the result and returns 1; returns 0 and pushes nothing when there is no such
    ///     metamethod.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="obj">Acceptable index of the value.</param>
    /// <param name="e">NUL-terminated metamethod name.</param>
    /// <remarks>Stack: -0 +(0|1). Raises: any (the metamethod runs unprotected).</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int luaL_callmeta(lua_State* L, int obj, byte* e)
    {
        return s_table.luaL_callmeta(L, obj, e);
    }

    /// <summary>
    ///     <c>const char *luaL_tolstring (lua_State *L, int idx, size_t *len)</c>. Pushes a printable string for any value
    ///     (honouring <c>__tostring</c>) and returns its bytes. Unlike <see cref="lua_tolstring" /> the original slot is
    ///     left untouched.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="idx">Acceptable index of the value.</param>
    /// <param name="len">Null, or receives the length in bytes.</param>
    /// <remarks>
    ///     Stack: -0 +1. Raises: any (<c>__tostring</c>; a <c>__tostring</c> that does not return a string). The pointer
    ///     is valid while the pushed string stays on the stack.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte* luaL_tolstring(lua_State* L, int idx, size_t* len)
    {
        return s_table.luaL_tolstring(L, idx, len);
    }

    /// <summary>
    ///     <c>int luaL_newmetatable (lua_State *L, const char *tname)</c>. Pushes the registry table named
    ///     <paramref name="tname" />, creating it (with <c>__name</c> set) when absent. Returns 1 when it was created, 0 when
    ///     it already existed.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="tname">NUL-terminated type name, unique in the registry.</param>
    /// <remarks>Stack: -0 +1. Raises: memory.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int luaL_newmetatable(lua_State* L, byte* tname)
    {
        return s_table.luaL_newmetatable(L, tname);
    }

    /// <summary>
    ///     <c>void luaL_setmetatable (lua_State *L, const char *tname)</c>. Sets the registry metatable named
    ///     <paramref name="tname" /> on the value on top of the stack.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="tname">NUL-terminated type name.</param>
    /// <remarks>Stack: -0 +0. Raises: memory.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void luaL_setmetatable(lua_State* L, byte* tname)
    {
        s_table.luaL_setmetatable(L, tname);
    }

    /// <summary>
    ///     <c>void *luaL_testudata (lua_State *L, int ud, const char *tname)</c>. Block address of the userdata at
    ///     <paramref name="ud" /> when its metatable is the registry table named <paramref name="tname" />; null otherwise.
    ///     The non-raising type check.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="ud">Acceptable index of the value.</param>
    /// <param name="tname">NUL-terminated type name.</param>
    /// <remarks>Stack: -0 +0. Raises: memory.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void* luaL_testudata(lua_State* L, int ud, byte* tname)
    {
        return s_table.luaL_testudata(L, ud, tname);
    }

    /// <summary><c>lua_Integer luaL_len (lua_State *L, int idx)</c>. Length of the value with Lua semantics, as an integer.</summary>
    /// <param name="L">The state.</param>
    /// <param name="idx">Valid index.</param>
    /// <remarks>
    ///     Stack: -0 +0. Raises: any (<c>__len</c>; a length that is not an integer). <see cref="lua_rawlen" /> is the
    ///     non-raising alternative.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static lua_Integer luaL_len(lua_State* L, int idx)
    {
        return s_table.luaL_len(L, idx);
    }

    /// <summary>
    ///     <c>int luaL_getsubtable (lua_State *L, int idx, const char *fname)</c>. Pushes <c>t[fname]</c>, creating and
    ///     storing a new table when the field is not a table. Returns 1 when it already was a table, 0 when it was created.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="idx">Valid index of the parent table.</param>
    /// <param name="fname">NUL-terminated field name.</param>
    /// <remarks>Stack: -0 +1. Raises: any (metamethods of the parent).</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int luaL_getsubtable(lua_State* L, int idx, byte* fname)
    {
        return s_table.luaL_getsubtable(L, idx, fname);
    }

    /// <summary>
    ///     <c>void luaL_traceback (lua_State *L, lua_State *L1, const char *msg, int level)</c>. Pushes onto
    ///     <paramref name="L" /> a traceback of the call stack of <paramref name="L1" />, prefixed by <paramref name="msg" />
    ///     when not null.
    /// </summary>
    /// <param name="L">The state that receives the string.</param>
    /// <param name="L1">The thread whose stack is described (may be <paramref name="L" />).</param>
    /// <param name="msg">NUL-terminated first line, or null.</param>
    /// <param name="level">Call level at which the traceback starts.</param>
    /// <remarks>Stack: -0 +1. Raises: memory.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void luaL_traceback(lua_State* L, lua_State* L1, byte* msg, int level)
    {
        s_table.luaL_traceback(L, L1, msg, level);
    }

    /// <summary>
    ///     <c>void luaL_requiref (lua_State *L, const char *modname, lua_CFunction openf, int glb)</c>. Loads a module the
    ///     way <c>require</c> would when it is not yet in <c>package.loaded</c>: calls <paramref name="openf" /> with the
    ///     name, records the result, optionally stores it in a global, and pushes it.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="modname">NUL-terminated module name (see the <c>LUA_*LIBNAME</c> constants; "_G" for the base library).</param>
    /// <param name="openf">The opener, for example <see cref="luaopen_string" />.</param>
    /// <param name="glb">Non-zero to also assign the module to the global <paramref name="modname" />.</param>
    /// <remarks>Stack: -0 +1. Raises: any (the opener runs unprotected; the stock openers only raise on memory exhaustion).</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void luaL_requiref(lua_State* L, byte* modname, lua_CFunction openf, int glb)
    {
        s_table.luaL_requiref(L, modname, openf, glb);
    }

    /// <summary>
    ///     <c>void luaL_setfuncs (lua_State *L, const luaL_Reg *l, int nup)</c>. Registers every function of the array
    ///     into the table below the <paramref name="nup" /> upvalues on top of the stack; each function becomes a closure
    ///     sharing those upvalues, which are popped.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="l">Array terminated by an all-null entry; only read during the call.</param>
    /// <param name="nup">Number of shared upvalues on top of the stack.</param>
    /// <remarks>Stack: -nup +0. Raises: memory.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void luaL_setfuncs(lua_State* L, luaL_Reg* l, int nup)
    {
        s_table.luaL_setfuncs(L, l, nup);
    }

    internal partial struct Table
    {
        internal delegate* unmanaged[Cdecl]<lua_State*, byte*, size_t, byte*, byte*, int> luaL_loadbufferx;
        internal delegate* unmanaged[Cdecl]<lua_State*, byte*, int> luaL_loadstring;
        internal delegate* unmanaged[Cdecl]<lua_State*, byte*, byte*, int> luaL_loadfilex;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, int> luaL_ref;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, int, void> luaL_unref;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, byte*, int> luaL_getmetafield;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, byte*, int> luaL_callmeta;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, size_t*, byte*> luaL_tolstring;
        internal delegate* unmanaged[Cdecl]<lua_State*, byte*, int> luaL_newmetatable;
        internal delegate* unmanaged[Cdecl]<lua_State*, byte*, void> luaL_setmetatable;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, byte*, void*> luaL_testudata;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, lua_Integer> luaL_len;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, byte*, int> luaL_getsubtable;
        internal delegate* unmanaged[Cdecl]<lua_State*, lua_State*, byte*, int, void> luaL_traceback;
        internal delegate* unmanaged[Cdecl]<lua_State*, byte*, lua_CFunction, int, void> luaL_requiref;
        internal delegate* unmanaged[Cdecl]<lua_State*, luaL_Reg*, int, void> luaL_setfuncs;

        private void LoadAuxiliary(ref ExportResolver exports)
        {
            luaL_loadbufferx =
                (delegate* unmanaged[Cdecl]<lua_State*, byte*, size_t, byte*, byte*, int>)exports.Resolve(
                    "luaL_loadbufferx");
            luaL_loadstring = (delegate* unmanaged[Cdecl]<lua_State*, byte*, int>)exports.Resolve("luaL_loadstring");
            luaL_loadfilex =
                (delegate* unmanaged[Cdecl]<lua_State*, byte*, byte*, int>)exports.Resolve("luaL_loadfilex");
            luaL_ref = (delegate* unmanaged[Cdecl]<lua_State*, int, int>)exports.Resolve("luaL_ref");
            luaL_unref = (delegate* unmanaged[Cdecl]<lua_State*, int, int, void>)exports.Resolve("luaL_unref");
            luaL_getmetafield =
                (delegate* unmanaged[Cdecl]<lua_State*, int, byte*, int>)exports.Resolve("luaL_getmetafield");
            luaL_callmeta = (delegate* unmanaged[Cdecl]<lua_State*, int, byte*, int>)exports.Resolve("luaL_callmeta");
            luaL_tolstring =
                (delegate* unmanaged[Cdecl]<lua_State*, int, size_t*, byte*>)exports.Resolve("luaL_tolstring");
            luaL_newmetatable =
                (delegate* unmanaged[Cdecl]<lua_State*, byte*, int>)exports.Resolve("luaL_newmetatable");
            luaL_setmetatable =
                (delegate* unmanaged[Cdecl]<lua_State*, byte*, void>)exports.Resolve("luaL_setmetatable");
            luaL_testudata =
                (delegate* unmanaged[Cdecl]<lua_State*, int, byte*, void*>)exports.Resolve("luaL_testudata");
            luaL_len = (delegate* unmanaged[Cdecl]<lua_State*, int, lua_Integer>)exports.Resolve("luaL_len");
            luaL_getsubtable =
                (delegate* unmanaged[Cdecl]<lua_State*, int, byte*, int>)exports.Resolve("luaL_getsubtable");
            luaL_traceback =
                (delegate* unmanaged[Cdecl]<lua_State*, lua_State*, byte*, int, void>)exports.Resolve("luaL_traceback");
            luaL_requiref =
                (delegate* unmanaged[Cdecl]<lua_State*, byte*, lua_CFunction, int, void>)exports.Resolve(
                    "luaL_requiref");
            luaL_setfuncs =
                (delegate* unmanaged[Cdecl]<lua_State*, luaL_Reg*, int, void>)exports.Resolve("luaL_setfuncs");
        }
    }
}
