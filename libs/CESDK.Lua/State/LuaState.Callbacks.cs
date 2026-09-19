using System;
using CESDK.Annotations.Lua;
using CESDK.Lua.Callbacks;
using CESDK.Lua.Calls;
using CESDK.Lua.Protected;
using static CESDK.Lua.Interop.Api.LuaApi;

namespace CESDK.Lua.State;

// Pushing managed functions: with the error channel (the normal form) or without it.
public readonly unsafe partial struct LuaState
{
    /// <summary>
    ///     Pushes a managed function without state, wrapped so that a failure it reports through
    ///     <see cref="LuaThunk.Fail(LuaState,System.ReadOnlySpan{byte})" /> becomes a Lua error in the caller.
    ///     Stack: +1 (the
    ///     function) on success; +1 (the error value) on failure.
    /// </summary>
    /// <param name="thunk">The managed <c>lua_CFunction</c>; see <see cref="LuaNativeFunction" /> for its rules.</param>
    /// <returns>The status of installing the helpers or running the wrapper.</returns>
    /// <exception cref="ArgumentException"><paramref name="thunk" /> is the null function.</exception>
    /// <remarks>
    ///     Allocates, inside Lua, the C closure and the wrapper closure: a registration-time cost. Assign the result to a
    ///     global with <see cref="TrySetGlobal" />, or keep it with
    ///     <see cref="CreateRef" />.
    /// </remarks>
    public LuaStatus TryPushFunction(LuaNativeFunction thunk)
    {
        if (thunk.IsNull) throw new ArgumentException("The thunk is the null function.", nameof(thunk));

        var status = LuaHelpers.Push(Pointer, LuaHelper.Wrap);
        if (!status.IsOk) return status;

        lua_pushcclosure(Pointer, thunk.Pointer, 0);
        return TryCall(1, 1);
    }

    /// <summary>
    ///     Pushes a managed function as a bare C function (<c>lua_pushcclosure</c> with no upvalues): no error channel,
    ///     so what the thunk returns is exactly what the Lua caller receives. For functions that cannot fail. Never raises.
    /// </summary>
    /// <param name="thunk">The managed <c>lua_CFunction</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="thunk" /> is the null function.</exception>
    [LuaStackEffect(1)]
    public void PushUncheckedFunction(LuaNativeFunction thunk)
    {
        if (thunk.IsNull) throw new ArgumentException("The thunk is the null function.", nameof(thunk));

        lua_pushcclosure(Pointer, thunk.Pointer, 0);
    }
}
