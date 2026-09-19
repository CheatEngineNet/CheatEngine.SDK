using System.Runtime.CompilerServices;
using CESDK.Lua.Interop.Types;

namespace CESDK.Lua.Interop.Api;

// lua.h "access functions (stack -> C)". The predicates return C int (0 or 1), exactly like the exports: under
// DisableRuntimeMarshalling a bool would be read as one unnormalized byte.
public static unsafe partial class LuaApi
{
    /// <summary>
    ///     <c>int lua_isnumber (lua_State *L, int idx)</c>. 1 when the value is a number or a string convertible to a
    ///     number, else 0.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="idx">Acceptable index.</param>
    /// <remarks>Stack: -0 +0. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int lua_isnumber(lua_State* L, int idx)
    {
        return s_table.lua_isnumber(L, idx);
    }

    /// <summary>
    ///     <c>int lua_isstring (lua_State *L, int idx)</c>. 1 when the value is a string or a number (numbers convert to
    ///     strings), else 0.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="idx">Acceptable index.</param>
    /// <remarks>Stack: -0 +0. Raises: never. Use <c>lua_type(L, idx) == LUA_TSTRING</c> to test for a real string.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int lua_isstring(lua_State* L, int idx)
    {
        return s_table.lua_isstring(L, idx);
    }

    /// <summary><c>int lua_iscfunction (lua_State *L, int idx)</c>. 1 when the value is a C function, else 0.</summary>
    /// <param name="L">The state.</param>
    /// <param name="idx">Acceptable index.</param>
    /// <remarks>Stack: -0 +0. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int lua_iscfunction(lua_State* L, int idx)
    {
        return s_table.lua_iscfunction(L, idx);
    }

    /// <summary>
    ///     <c>int lua_isinteger (lua_State *L, int idx)</c>. 1 when the value is a number with the integer subtype, else
    ///     0 (strings never qualify).
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="idx">Acceptable index.</param>
    /// <remarks>Stack: -0 +0. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int lua_isinteger(lua_State* L, int idx)
    {
        return s_table.lua_isinteger(L, idx);
    }

    /// <summary><c>int lua_isuserdata (lua_State *L, int idx)</c>. 1 when the value is a full or a light userdata, else 0.</summary>
    /// <param name="L">The state.</param>
    /// <param name="idx">Acceptable index.</param>
    /// <remarks>Stack: -0 +0. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int lua_isuserdata(lua_State* L, int idx)
    {
        return s_table.lua_isuserdata(L, idx);
    }

    /// <summary>
    ///     <c>int lua_type (lua_State *L, int idx)</c>. Type tag of the value (<c>LUA_T*</c>), <see cref="LUA_TNONE" />
    ///     for an index beyond the top.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="idx">Acceptable index.</param>
    /// <remarks>Stack: -0 +0. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int lua_type(lua_State* L, int idx)
    {
        return s_table.lua_type(L, idx);
    }

    /// <summary>
    ///     <c>const char *lua_typename (lua_State *L, int tp)</c>. Name of a type tag as a NUL-terminated static string
    ///     of the library.
    /// </summary>
    /// <param name="L">The state (unused by the library).</param>
    /// <param name="tp">A <c>LUA_T*</c> tag.</param>
    /// <remarks>Stack: -0 +0. Raises: never. The pointer is valid while the module is loaded.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte* lua_typename(lua_State* L, int tp)
    {
        return s_table.lua_typename(L, tp);
    }

    /// <summary>
    ///     <c>lua_Number lua_tonumberx (lua_State *L, int idx, int *isnum)</c>. Converts the value to a float; numbers and
    ///     numeric strings convert, everything else yields 0.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="idx">Acceptable index.</param>
    /// <param name="isnum">Null, or receives 1 when the conversion succeeded and 0 otherwise.</param>
    /// <remarks>Stack: -0 +0. Raises: never. The stack slot is not modified.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static lua_Number lua_tonumberx(lua_State* L, int idx, int* isnum)
    {
        return s_table.lua_tonumberx(L, idx, isnum);
    }

    /// <summary>
    ///     <c>lua_Integer lua_tointegerx (lua_State *L, int idx, int *isnum)</c>. Converts the value to a 64-bit integer;
    ///     integers, floats with an exact integer value and such strings convert, everything else yields 0.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="idx">Acceptable index.</param>
    /// <param name="isnum">Null, or receives 1 when the conversion succeeded and 0 otherwise.</param>
    /// <remarks>Stack: -0 +0. Raises: never. The stack slot is not modified.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static lua_Integer lua_tointegerx(lua_State* L, int idx, int* isnum)
    {
        return s_table.lua_tointegerx(L, idx, isnum);
    }

    /// <summary>
    ///     <c>int lua_toboolean (lua_State *L, int idx)</c>. 0 for <c>false</c>, nil and none; 1 for every other value
    ///     (including 0 and "").
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="idx">Acceptable index.</param>
    /// <remarks>Stack: -0 +0. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int lua_toboolean(lua_State* L, int idx)
    {
        return s_table.lua_toboolean(L, idx);
    }

    /// <summary>
    ///     <c>const char *lua_tolstring (lua_State *L, int idx, size_t *len)</c>. Bytes of a string value: a pointer into
    ///     Lua's own copy, always followed by a NUL, possibly containing NULs. Null when the value is neither a string nor
    ///     a number.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="idx">Acceptable index.</param>
    /// <param name="len">Null, or receives the length in bytes.</param>
    /// <remarks>
    ///     Stack: -0 +0. Raises: memory. A number is converted to a string <b>in place</b>, which changes the slot and
    ///     breaks a <see cref="lua_next" /> traversal when applied to a key. The pointer is borrowed: valid only while the
    ///     value stays on the stack (or otherwise reachable), so copy or decode before popping.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte* lua_tolstring(lua_State* L, int idx, size_t* len)
    {
        return s_table.lua_tolstring(L, idx, len);
    }

    /// <summary>
    ///     <c>size_t lua_rawlen (lua_State *L, int idx)</c>. Raw length: bytes of a string, border of a table without
    ///     metamethods, block size of a full userdata, 0 for anything else.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="idx">Acceptable index.</param>
    /// <remarks>Stack: -0 +0. Raises: never. The native return type is <c>size_t</c>, not <c>int</c>.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static size_t lua_rawlen(lua_State* L, int idx)
    {
        return s_table.lua_rawlen(L, idx);
    }

    /// <summary>
    ///     <c>lua_CFunction lua_tocfunction (lua_State *L, int idx)</c>. The C function behind the value, or null when it
    ///     is not a C function.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="idx">Acceptable index.</param>
    /// <remarks>Stack: -0 +0. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static lua_CFunction lua_tocfunction(lua_State* L, int idx)
    {
        return s_table.lua_tocfunction(L, idx);
    }

    /// <summary>
    ///     <c>void *lua_touserdata (lua_State *L, int idx)</c>. Block address of a full userdata, or the pointer of a light
    ///     userdata; null for anything else.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="idx">Acceptable index.</param>
    /// <remarks>
    ///     Stack: -0 +0. Raises: never. A full userdata block is owned by Lua and lives as long as the userdata is
    ///     reachable: read what you need before popping it.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void* lua_touserdata(lua_State* L, int idx)
    {
        return s_table.lua_touserdata(L, idx);
    }

    /// <summary>
    ///     <c>lua_State *lua_tothread (lua_State *L, int idx)</c>. The thread behind the value, or null when it is not a
    ///     thread.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="idx">Acceptable index.</param>
    /// <remarks>Stack: -0 +0. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static lua_State* lua_tothread(lua_State* L, int idx)
    {
        return s_table.lua_tothread(L, idx);
    }

    /// <summary>
    ///     <c>const void *lua_topointer (lua_State *L, int idx)</c>. An identity pointer for tables, functions, threads and
    ///     userdata (distinct objects give distinct pointers); null for other types. Only good for hashing and diagnostics.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="idx">Acceptable index.</param>
    /// <remarks>Stack: -0 +0. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void* lua_topointer(lua_State* L, int idx)
    {
        return s_table.lua_topointer(L, idx);
    }

    internal partial struct Table
    {
        internal delegate* unmanaged[Cdecl]<lua_State*, int, int> lua_isnumber;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, int> lua_isstring;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, int> lua_iscfunction;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, int> lua_isinteger;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, int> lua_isuserdata;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, int> lua_type;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, byte*> lua_typename;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, int*, lua_Number> lua_tonumberx;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, int*, lua_Integer> lua_tointegerx;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, int> lua_toboolean;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, size_t*, byte*> lua_tolstring;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, size_t> lua_rawlen;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, lua_CFunction> lua_tocfunction;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, void*> lua_touserdata;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, lua_State*> lua_tothread;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, void*> lua_topointer;

        private void LoadAccess(ref ExportResolver exports)
        {
            lua_isnumber = (delegate* unmanaged[Cdecl]<lua_State*, int, int>)exports.Resolve("lua_isnumber");
            lua_isstring = (delegate* unmanaged[Cdecl]<lua_State*, int, int>)exports.Resolve("lua_isstring");
            lua_iscfunction = (delegate* unmanaged[Cdecl]<lua_State*, int, int>)exports.Resolve("lua_iscfunction");
            lua_isinteger = (delegate* unmanaged[Cdecl]<lua_State*, int, int>)exports.Resolve("lua_isinteger");
            lua_isuserdata = (delegate* unmanaged[Cdecl]<lua_State*, int, int>)exports.Resolve("lua_isuserdata");
            lua_type = (delegate* unmanaged[Cdecl]<lua_State*, int, int>)exports.Resolve("lua_type");
            lua_typename = (delegate* unmanaged[Cdecl]<lua_State*, int, byte*>)exports.Resolve("lua_typename");
            lua_tonumberx =
                (delegate* unmanaged[Cdecl]<lua_State*, int, int*, lua_Number>)exports.Resolve("lua_tonumberx");
            lua_tointegerx =
                (delegate* unmanaged[Cdecl]<lua_State*, int, int*, lua_Integer>)exports.Resolve("lua_tointegerx");
            lua_toboolean = (delegate* unmanaged[Cdecl]<lua_State*, int, int>)exports.Resolve("lua_toboolean");
            lua_tolstring =
                (delegate* unmanaged[Cdecl]<lua_State*, int, size_t*, byte*>)exports.Resolve("lua_tolstring");
            lua_rawlen = (delegate* unmanaged[Cdecl]<lua_State*, int, size_t>)exports.Resolve("lua_rawlen");
            lua_tocfunction =
                (delegate* unmanaged[Cdecl]<lua_State*, int, lua_CFunction>)exports.Resolve("lua_tocfunction");
            lua_touserdata = (delegate* unmanaged[Cdecl]<lua_State*, int, void*>)exports.Resolve("lua_touserdata");
            lua_tothread = (delegate* unmanaged[Cdecl]<lua_State*, int, lua_State*>)exports.Resolve("lua_tothread");
            lua_topointer = (delegate* unmanaged[Cdecl]<lua_State*, int, void*>)exports.Resolve("lua_topointer");
        }
    }
}
