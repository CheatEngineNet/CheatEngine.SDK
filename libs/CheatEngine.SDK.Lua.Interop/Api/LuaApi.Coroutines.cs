using System.Runtime.CompilerServices;
using CheatEngine.SDK.Lua.Interop.Types;

namespace CheatEngine.SDK.Lua.Interop.Api;

// lua.h "coroutine functions". lua_yieldk is not bound: called from a C function it never returns (it unwinds with
// longjmp), which makes it unusable from managed code by construction.
public static unsafe partial class LuaApi
{
    /// <summary>
    ///     <c>int lua_resume (lua_State *L, lua_State *from, int narg)</c>. Starts or continues a coroutine on thread
    ///     <paramref name="L" />. Returns <see cref="LUA_YIELD" /> (yielded values on the thread's stack),
    ///     <see cref="LUA_OK" /> (results on the stack) or an error status (error value on top; the stack is not unwound).
    /// </summary>
    /// <param name="L">The coroutine thread. To start it: push the function, then <paramref name="narg" /> arguments.</param>
    /// <param name="from">The thread that resumes, or null.</param>
    /// <param name="narg">Number of arguments (start) or of values handed to the pending yield (continue).</param>
    /// <remarks>Stack: -? +?. Raises: never (errors inside the coroutine come back as the status).</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int lua_resume(lua_State* L, lua_State* from, int narg)
    {
        return s_table.lua_resume(L, from, narg);
    }

    /// <summary>
    ///     <c>int lua_status (lua_State *L)</c>. <see cref="LUA_OK" /> for a normal or finished thread,
    ///     <see cref="LUA_YIELD" /> for a suspended one, or the error status that killed it.
    /// </summary>
    /// <param name="L">The thread.</param>
    /// <remarks>Stack: -0 +0. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int lua_status(lua_State* L)
    {
        return s_table.lua_status(L);
    }

    /// <summary>
    ///     <c>int lua_isyieldable (lua_State *L)</c>. 1 when the running code of <paramref name="L" /> could yield (it is
    ///     inside a coroutine and no C boundary forbids it), else 0.
    /// </summary>
    /// <param name="L">The thread.</param>
    /// <remarks>Stack: -0 +0. Raises: never.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int lua_isyieldable(lua_State* L)
    {
        return s_table.lua_isyieldable(L);
    }

    internal partial struct Table
    {
        internal delegate* unmanaged[Cdecl]<lua_State*, lua_State*, int, int> lua_resume;
        internal delegate* unmanaged[Cdecl]<lua_State*, int> lua_status;
        internal delegate* unmanaged[Cdecl]<lua_State*, int> lua_isyieldable;

        private void LoadCoroutines(ref ExportResolver exports)
        {
            lua_resume = (delegate* unmanaged[Cdecl]<lua_State*, lua_State*, int, int>)exports.Resolve("lua_resume");
            lua_status = (delegate* unmanaged[Cdecl]<lua_State*, int>)exports.Resolve("lua_status");
            lua_isyieldable = (delegate* unmanaged[Cdecl]<lua_State*, int>)exports.Resolve("lua_isyieldable");
        }
    }
}
