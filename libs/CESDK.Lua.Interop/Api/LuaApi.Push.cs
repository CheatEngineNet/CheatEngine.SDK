using System.Runtime.CompilerServices;
using CESDK.Lua.Interop.Types;

namespace CESDK.Lua.Interop.Api;

// lua.h "push functions (C -> stack)". The printf-style lua_pushfstring / lua_pushvfstring are not bound: C varargs
// cannot be expressed as a blittable function pointer.
public static unsafe partial class LuaApi
{
    /// <summary><c>void lua_pushnil (lua_State *L)</c>. Pushes nil.</summary>
    /// <param name="L">The state.</param>
    /// <remarks>Stack: -0 +1. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void lua_pushnil(lua_State* L)
    {
        s_table.lua_pushnil(L);
    }

    /// <summary><c>void lua_pushnumber (lua_State *L, lua_Number n)</c>. Pushes a float.</summary>
    /// <param name="L">The state.</param>
    /// <param name="n">The value; it keeps the float subtype even when it is integral.</param>
    /// <remarks>Stack: -0 +1. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void lua_pushnumber(lua_State* L, lua_Number n)
    {
        s_table.lua_pushnumber(L, n);
    }

    /// <summary><c>void lua_pushinteger (lua_State *L, lua_Integer n)</c>. Pushes a 64-bit integer.</summary>
    /// <param name="L">The state.</param>
    /// <param name="n">The value.</param>
    /// <remarks>Stack: -0 +1. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void lua_pushinteger(lua_State* L, lua_Integer n)
    {
        s_table.lua_pushinteger(L, n);
    }

    /// <summary>
    ///     <c>const char *lua_pushlstring (lua_State *L, const char *s, size_t len)</c>. Pushes a copy of
    ///     <paramref name="len" /> bytes, which may contain NULs. Returns a pointer to Lua's internal copy.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="s">First byte; only read during the call. May be null only when <paramref name="len" /> is 0.</param>
    /// <param name="len">Number of bytes.</param>
    /// <remarks>
    ///     Stack: -0 +1. Raises: memory. Lua does not interpret the bytes: the encoding is the caller's contract (UTF-8
    ///     in this SDK).
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte* lua_pushlstring(lua_State* L, byte* s, size_t len)
    {
        return s_table.lua_pushlstring(L, s, len);
    }

    /// <summary>
    ///     <c>const char *lua_pushstring (lua_State *L, const char *s)</c>. Pushes a copy of a NUL-terminated string, or
    ///     nil when <paramref name="s" /> is null. Returns a pointer to Lua's internal copy (null for nil).
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="s">NUL-terminated bytes (a <c>u8</c> literal qualifies), or null.</param>
    /// <remarks>Stack: -0 +1. Raises: memory.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte* lua_pushstring(lua_State* L, byte* s)
    {
        return s_table.lua_pushstring(L, s);
    }

    /// <summary>
    ///     <c>void lua_pushcclosure (lua_State *L, lua_CFunction fn, int n)</c>. Pops <paramref name="n" /> values and
    ///     pushes a C function that owns them as upvalues (reachable through <see cref="lua_upvalueindex" />).
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="fn">
    ///     The function: the address of a static <c>[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]</c> method
    ///     for managed code. It must never let an exception escape and must never call a raising API.
    /// </param>
    /// <param name="n">Number of upvalues, 0 to 255. With 0 the result is a light C function and nothing is allocated.</param>
    /// <remarks>Stack: -n +1. Raises: memory (only when <paramref name="n" /> is greater than 0).</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void lua_pushcclosure(lua_State* L, lua_CFunction fn, int n)
    {
        s_table.lua_pushcclosure(L, fn, n);
    }

    /// <summary>
    ///     <c>void lua_pushboolean (lua_State *L, int b)</c>. Pushes <c>true</c> when <paramref name="b" /> is non-zero,
    ///     else <c>false</c>.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="b">C truth value.</param>
    /// <remarks>Stack: -0 +1. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void lua_pushboolean(lua_State* L, int b)
    {
        s_table.lua_pushboolean(L, b);
    }

    /// <summary>
    ///     <c>void lua_pushlightuserdata (lua_State *L, void *p)</c>. Pushes a bare pointer value. Lua never dereferences
    ///     or frees it; equal pointers are equal values.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="p">Any pointer-sized value.</param>
    /// <remarks>Stack: -0 +1. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void lua_pushlightuserdata(lua_State* L, void* p)
    {
        s_table.lua_pushlightuserdata(L, p);
    }

    /// <summary>
    ///     <c>int lua_pushthread (lua_State *L)</c>. Pushes the thread <paramref name="L" /> itself. Returns 1 when it is
    ///     the main thread of its state.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <remarks>Stack: -0 +1. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int lua_pushthread(lua_State* L)
    {
        return s_table.lua_pushthread(L);
    }

    internal partial struct Table
    {
        internal delegate* unmanaged[Cdecl]<lua_State*, void> lua_pushnil;
        internal delegate* unmanaged[Cdecl]<lua_State*, lua_Number, void> lua_pushnumber;
        internal delegate* unmanaged[Cdecl]<lua_State*, lua_Integer, void> lua_pushinteger;
        internal delegate* unmanaged[Cdecl]<lua_State*, byte*, size_t, byte*> lua_pushlstring;
        internal delegate* unmanaged[Cdecl]<lua_State*, byte*, byte*> lua_pushstring;
        internal delegate* unmanaged[Cdecl]<lua_State*, lua_CFunction, int, void> lua_pushcclosure;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, void> lua_pushboolean;
        internal delegate* unmanaged[Cdecl]<lua_State*, void*, void> lua_pushlightuserdata;
        internal delegate* unmanaged[Cdecl]<lua_State*, int> lua_pushthread;

        private void LoadPush(ref ExportResolver exports)
        {
            lua_pushnil = (delegate* unmanaged[Cdecl]<lua_State*, void>)exports.Resolve("lua_pushnil");
            lua_pushnumber =
                (delegate* unmanaged[Cdecl]<lua_State*, lua_Number, void>)exports.Resolve("lua_pushnumber");
            lua_pushinteger =
                (delegate* unmanaged[Cdecl]<lua_State*, lua_Integer, void>)exports.Resolve("lua_pushinteger");
            lua_pushlstring =
                (delegate* unmanaged[Cdecl]<lua_State*, byte*, size_t, byte*>)exports.Resolve("lua_pushlstring");
            lua_pushstring = (delegate* unmanaged[Cdecl]<lua_State*, byte*, byte*>)exports.Resolve("lua_pushstring");
            lua_pushcclosure =
                (delegate* unmanaged[Cdecl]<lua_State*, lua_CFunction, int, void>)exports.Resolve("lua_pushcclosure");
            lua_pushboolean = (delegate* unmanaged[Cdecl]<lua_State*, int, void>)exports.Resolve("lua_pushboolean");
            lua_pushlightuserdata =
                (delegate* unmanaged[Cdecl]<lua_State*, void*, void>)exports.Resolve("lua_pushlightuserdata");
            lua_pushthread = (delegate* unmanaged[Cdecl]<lua_State*, int>)exports.Resolve("lua_pushthread");
        }
    }
}
