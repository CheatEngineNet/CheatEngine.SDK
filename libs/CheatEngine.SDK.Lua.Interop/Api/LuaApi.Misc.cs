using System.Runtime.CompilerServices;
using CheatEngine.SDK.Lua.Interop.Types;

namespace CheatEngine.SDK.Lua.Interop.Api;

// lua.h garbage collection, table traversal and allocator access.
public static unsafe partial class LuaApi
{
    /// <summary>
    ///     <c>int lua_gc (lua_State *L, int what, int data)</c>. Controls the garbage collector; the meaning of
    ///     <paramref name="data" /> and of the result depends on the <c>LUA_GC*</c> command.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="what">A <c>LUA_GC*</c> command.</param>
    /// <param name="data">Command argument (step size, pause, multiplier), otherwise 0.</param>
    /// <remarks>Stack: -0 +0. Raises: memory. A collection runs <c>__gc</c> metamethods, which may be managed callbacks.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int lua_gc(lua_State* L, int what, int data)
    {
        return s_table.lua_gc(L, what, data);
    }

    /// <summary>
    ///     <c>int lua_next (lua_State *L, int idx)</c>. Table traversal step: pops a key and pushes the next key-value
    ///     pair of the table at <paramref name="idx" /> (returns non-zero), or pushes nothing at the end (returns 0). Start
    ///     with a nil key.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="idx">Valid index of a table; use an absolute index, the loop pushes and pops.</param>
    /// <remarks>
    ///     Stack: -1 +(2|0). Raises: any (only when the popped key is not a key of the table). During the traversal pop
    ///     the value and keep the key, do not assign to absent fields, and never call <see cref="lua_tolstring" /> on a key
    ///     that is not already a string: the in-place conversion confuses the next step.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int lua_next(lua_State* L, int idx)
    {
        return s_table.lua_next(L, idx);
    }

    /// <summary>
    ///     <c>lua_Alloc lua_getallocf (lua_State *L, void **ud)</c>. The allocator of the state and, through
    ///     <paramref name="ud" /> when not null, its opaque pointer.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="ud">Null, or receives the allocator's opaque pointer.</param>
    /// <remarks>Stack: -0 +0. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static lua_Alloc lua_getallocf(lua_State* L, void** ud)
    {
        return s_table.lua_getallocf(L, ud);
    }

    /// <summary>
    ///     <c>void lua_setallocf (lua_State *L, lua_Alloc f, void *ud)</c>. Replaces the allocator of the state. The new
    ///     one must be able to free and resize blocks handed out by the old one.
    /// </summary>
    /// <param name="L">The state.</param>
    /// <param name="f">New allocator.</param>
    /// <param name="ud">Opaque pointer passed to <paramref name="f" />.</param>
    /// <remarks>Stack: -0 +0. Raises: never. Never on a state that belongs to Cheat Engine.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void lua_setallocf(lua_State* L, lua_Alloc f, void* ud)
    {
        s_table.lua_setallocf(L, f, ud);
    }

    internal partial struct Table
    {
        internal delegate* unmanaged[Cdecl]<lua_State*, int, int, int> lua_gc;
        internal delegate* unmanaged[Cdecl]<lua_State*, int, int> lua_next;
        internal delegate* unmanaged[Cdecl]<lua_State*, void**, lua_Alloc> lua_getallocf;
        internal delegate* unmanaged[Cdecl]<lua_State*, lua_Alloc, void*, void> lua_setallocf;

        private void LoadMisc(ref ExportResolver exports)
        {
            lua_gc = (delegate* unmanaged[Cdecl]<lua_State*, int, int, int>)exports.Resolve("lua_gc");
            lua_next = (delegate* unmanaged[Cdecl]<lua_State*, int, int>)exports.Resolve("lua_next");
            lua_getallocf = (delegate* unmanaged[Cdecl]<lua_State*, void**, lua_Alloc>)exports.Resolve("lua_getallocf");
            lua_setallocf =
                (delegate* unmanaged[Cdecl]<lua_State*, lua_Alloc, void*, void>)exports.Resolve("lua_setallocf");
        }
    }
}
