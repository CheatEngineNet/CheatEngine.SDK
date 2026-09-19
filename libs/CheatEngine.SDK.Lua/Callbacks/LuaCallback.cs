using System;
using System.Runtime.InteropServices;
using System.Threading;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Interop.Protected;
using CheatEngine.SDK.Lua.Protected;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

namespace CheatEngine.SDK.Lua.Callbacks;

/// <summary>
///     A managed <c>lua_CFunction</c> registered with Lua together with a managed state object: the closure Lua calls,
///     the wrapper that turns the error channel into a Lua error, and the <c>GCHandle&lt;T&gt;</c> that carries the state
///     through the closure's first upvalue as a light userdata. Owned by managed code and released explicitly; no
///     finalizer.
/// </summary>
/// <remarks>
///     <para>
///         <b>Creation</b> (<see cref="TryCreate{TState}" />): allocates the handle, builds the C closure with the
///         handle's
///         address as upvalue 1, wraps it with the Lua-side <c>check</c> closure (see <see cref="LuaHelpers" />) and keeps
///         both functions in the registry through two <see cref="LuaRef" />s. Nothing is left on the stack; push the
///         callback
///         with <see cref="TryPush" /> or assign it to a global with <see cref="TryRegister" />.
///     </para>
///     <para>
///         <b>Inside the thunk</b>: <see cref="LuaThunk.TryGetState{TState}" /> reads the state back from the upvalue.
///     </para>
///     <para>
///         <b>Release</b> (<see cref="Release" />, <see cref="Dispose" />, or <see cref="LuaRuntime.Detach" /> for
///         whatever is
///         still alive): the closure's upvalue is set to a null light userdata first, so that a script which kept the
///         function and calls it later gets a "callback released" error instead of touching freed memory; only then is the
///         handle freed and the two registry slots given back. When the closure cannot be reached any more (the reference
///         is
///         stale because the epoch advanced, or no state is available) the handle is deliberately <i>not</i> freed: the
///         state
///         object leaks, which is the safe failure. Releasing while the runtime is attached, in the plugin's disable path,
///         avoids that, and <see cref="LuaRuntime.Detach" /> does it for every callback the plugin forgot.
///     </para>
///     <para>
///         <b>Threads.</b> Release on the thread whose state is passed. The thunk runs on whatever thread runs the Lua
///         code
///         that calls it.
///     </para>
/// </remarks>
public abstract class LuaCallback : IDisposable
{
    private readonly LuaRef _closure;
    private readonly LuaRef _wrapped;
    private GCHandle<object> _handle;
    private bool _released;

    private protected LuaCallback(GCHandle<object> handle, LuaRef closure, LuaRef wrapped)
    {
        _handle = handle;
        _closure = closure;
        _wrapped = wrapped;
    }

    /// <summary>
    ///     Gets a value indicating whether the callback has been released (explicitly, or by
    ///     <see cref="LuaRuntime.Detach" />).
    /// </summary>
    public bool IsReleased => Volatile.Read(ref _released);

    /// <summary>
    ///     Gets a value indicating whether the callback can still be pushed: not released, and created in the current
    ///     <see cref="LuaRuntime.Epoch" />.
    /// </summary>
    public bool IsCurrent => !IsReleased && _wrapped.IsCurrent;

    /// <summary>Gets the managed state object, untyped; <see langword="null" /> after release.</summary>
    public object? StateObject
    {
        get
        {
            lock (LuaCallbackRegistry.Gate)
            {
                return IsReleased || !_handle.IsAllocated ? null : _handle.Target;
            }
        }
    }

    internal LuaCallback? Next { get; set; }

    internal LuaCallback? Previous { get; set; }

    internal bool IsLinked { get; set; }

    /// <summary>
    ///     <see cref="Release" /> with the state acquired from <see cref="LuaRuntime" />; abandons the handle when the
    ///     runtime is detached.
    /// </summary>
    public void Dispose()
    {
        Release(LuaRuntime.TryAcquireState(out var state) ? state : default);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Creates a callback: the C closure over <paramref name="thunk" /> with <paramref name="state" /> as its upvalue,
    ///     wrapped by the error-channel closure. Stack: +0 on success; +1 (the error value) on failure.
    /// </summary>
    /// <typeparam name="TState">
    ///     The state's type, a class; the thunk reads it back with
    ///     <see cref="LuaThunk.TryGetState{TState}" />.
    /// </typeparam>
    /// <param name="state">The state to build on; the calling thread's.</param>
    /// <param name="thunk">The managed <c>lua_CFunction</c>.</param>
    /// <param name="stateObject">The object to carry; kept alive by the callback until release.</param>
    /// <param name="callback">The callback on success; <see langword="null" /> on failure.</param>
    /// <returns>The status of installing the helpers or running the wrapper; <see cref="LuaStatus.Ok" /> on success.</returns>
    /// <exception cref="ArgumentException"><paramref name="thunk" /> is the null function.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="stateObject" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     Allocates the handle, two <see cref="LuaRef" />s, the callback object and, inside Lua, two closures and two
    ///     registry slots: a registration-time cost, never per call.
    /// </remarks>
    public static unsafe LuaStatus TryCreate<TState>(LuaState state, LuaNativeFunction thunk, TState stateObject,
        out LuaCallback<TState>? callback)
        where TState : class
    {
        if (thunk.IsNull) throw new ArgumentException("The thunk is the null function.", nameof(thunk));

        ArgumentNullException.ThrowIfNull(stateObject);
        callback = null;
        var l = state.Pointer;
        var top = state.Top;

        GCHandle<object> handle = new(stateObject);
        LuaRef? closure = null;
        LuaRef? wrapped = null;
        var transferred = false;
        try
        {
            lua_pushlightuserdata(l, (void*)GCHandle<object>.ToIntPtr(handle));
            var status = new LuaStatus(LuaProtectedApi.PushClosure(l, (nint)thunk.Pointer, 1));
            if (!status.IsOk) return status;

            lua_pushvalue(l, -1);
            status = state.TryCreateRef(out closure);
            if (!status.IsOk) return state.KeepProtectedError(top, status);

            // [closure] -> [closure wrap] -> [wrap closure] -> [wrapped]
            status = LuaHelpers.Push(l, LuaHelper.Wrap);
            if (!status.IsOk) return state.KeepProtectedError(top, status);

            lua_rotate(l, -2, 1);
            status = state.TryCall(1, 1);
            if (!status.IsOk) return status;

            status = state.TryCreateRef(out wrapped);
            if (!status.IsOk) return status;

            LuaCallback<TState> created = new(handle, closure!, wrapped!);
            LuaCallbackRegistry.Add(created);
            callback = created;
            transferred = true;
            return LuaStatus.Ok;
        }
        finally
        {
            if (!transferred)
            {
                closure?.Release(state);
                wrapped?.Release(state);
                if (handle.IsAllocated) handle.Dispose();
            }
        }
    }

    /// <summary>
    ///     Pushes the Lua-callable function (the wrapped closure). Stack: +1 on <see langword="true" />, +0 when the
    ///     callback is released or stale.
    /// </summary>
    /// <param name="state">The state to push on; the calling thread's.</param>
    /// <returns><see langword="true" /> when the function was pushed.</returns>
    public bool TryPush(LuaState state)
    {
        return !IsReleased && state.TryPushRef(_wrapped);
    }

    /// <summary>
    ///     Assigns the Lua-callable function to a global under protection (<see cref="LuaState.TrySetGlobal" />).
    ///     Stack: +0 on success, +1 (the error value) on failure.
    /// </summary>
    /// <param name="state">The state to register on; the calling thread's.</param>
    /// <param name="globalName">The global name, UTF-8.</param>
    /// <returns>
    ///     The status of the assignment; <see cref="LuaStatus.RuntimeError" /> with a message on the stack when the
    ///     callback is released or stale (<see cref="IsCurrent" /> is <see langword="false" />), reported the same way as
    ///     any other failed protected operation rather than thrown.
    /// </returns>
    public LuaStatus TryRegister(LuaState state, ReadOnlySpan<byte> globalName)
    {
        if (TryPush(state)) return state.TrySetGlobal(globalName);
        var status = state.TryPushString("the callback has been released, or was created in an earlier host epoch"u8);
        return status.IsOk ? LuaStatus.RuntimeError : status;
    }

    /// <summary>
    ///     Neutralizes the closure, frees the managed state and gives the registry slots back; see the type remarks for
    ///     what happens when the closure cannot be reached. Idempotent.
    /// </summary>
    /// <param name="state">
    ///     A state of the Lua universe the callback was created in; the calling thread's. May be
    ///     <see cref="LuaState.IsNull" />, which forces the abandon path.
    /// </param>
    public void Release(LuaState state)
    {
        lock (LuaCallbackRegistry.Gate)
        {
            ReleaseUnderGate(state);
        }
    }

    // Private so that Release, which holds the gate, is the only way in: the flag test, GCHandle<T>.Dispose (not thread
    // safe) and the unlink all depend on it.
    private unsafe void ReleaseUnderGate(LuaState state)
    {
        if (_released)
        {
            LuaCallbackRegistry.Remove(this);
            return;
        }

        Volatile.Write(ref _released, true);
        var neutralized = false;
        if (!state.IsNull && state.TryPushRef(_closure))
        {
            var l = state.Pointer;
            lua_pushlightuserdata(l, null);
            _ = lua_setupvalue(l, -2, 1);
            lua_settop(l, -2);
            neutralized = true;
        }

        try
        {
            _closure.Release(state);
        }
        finally
        {
            try
            {
                _wrapped.Release(state);
            }
            finally
            {
                if (neutralized && _handle.IsAllocated) _handle.Dispose();
                LuaCallbackRegistry.Remove(this);
            }
        }
    }
}
