using System;
using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Lua.Callbacks;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Protected;
using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

namespace CheatEngine.SDK.Lua.State;

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

        try
        {
            PushUncheckedFunction(thunk);
        }
        catch (InvalidOperationException)
        {
            // The wrapper occupies the top slot. Replace it with the documented allocation-free nil error value when
            // the bare function's immediate stack reservation failed.
            lua_settop(Pointer, -2);
            lua_pushnil(Pointer);
            return LuaStatus.MemoryError;
        }

        return TryCall(1, 1);
    }

    /// <summary>
    ///     Pushes a managed function as a bare C function (<c>lua_pushcclosure</c> with no upvalues): no error channel,
    ///     so what the thunk returns is exactly what the Lua caller receives. For functions that cannot fail.
    /// </summary>
    /// <param name="thunk">The managed <c>lua_CFunction</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="thunk" /> is the null function.</exception>
    /// <exception cref="InvalidOperationException">
    ///     Lua cannot reserve the one stack slot required for the light-C-function fast path. The stack is unchanged.
    /// </exception>
    /// <remarks>
    ///     CE's pinned Lua 5.3 implementation stores a zero-upvalue C function as a light C function, so the push does
    ///     not allocate after the stack slot is reserved. This method performs that <c>lua_checkstack(L, 1)</c> check
    ///     immediately before the push; do not replace it with a generic closure call or move another Lua call between
    ///     the check and the push.
    /// </remarks>
    [LuaStackEffect(1)]
    public void PushUncheckedFunction(LuaNativeFunction thunk)
    {
        if (thunk.IsNull) throw new ArgumentException("The thunk is the null function.", nameof(thunk));
        if (lua_checkstack(Pointer, 1) == 0)
            throw new InvalidOperationException(
                "Lua could not reserve one stack slot for the bare C function; the stack is unchanged.");

        lua_pushcclosure(Pointer, thunk.Pointer, 0);
    }
}
