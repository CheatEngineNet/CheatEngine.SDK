using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Lua.CompilerServices;

/// <summary>
///     Generator-facing: pushes a global function through a lazily resolved, state-identity-checked <see cref="LuaRef" />,
///     so that
///     a bound global is read from the SDK's private reference table after the first call. Not meant to be called by hand.
/// </summary>
/// <remarks>
///     <para>
///         <b>Hot path</b> (<see cref="TryPush" />): a reference lookup synchronized with release and a complete Lua state
///         identity comparison. <b>Cold path</b> (first use, or the state identity has advanced since the reference was
///         resolved): a protected read of the global (<see cref="LuaState.TryGetGlobal" />), a type check (the value must
///         be a function) and a private-table reference, under a lock so that two threads resolving the same global do
///         not both take a slot. A stale slot from a previous state identity is never released: its registry may be gone
///         or
///         reused, so it is simply forgotten.
///     </para>
///     <para>
///         A cached reference binds to the function value at resolve time: a script that later replaces the global is not
///         seen until the next state identity. That is the intended trade.
///     </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class LuaGlobalFunctions
{
    private static readonly Lock SResolveGate = new();

    /// <summary>
    ///     Pushes the global function <paramref name="name" />, resolving and caching it in <paramref name="cache" /> on
    ///     first use and whenever the cached slot is stale. Stack: +1 on <see langword="true" />; +0 on
    ///     <see langword="false" />.
    /// </summary>
    /// <param name="state">The state to push on; the calling thread's.</param>
    /// <param name="cache">
    ///     The reference that caches the resolution; a <c>static readonly</c> field created with
    ///     <c>new LuaRef()</c>.
    /// </param>
    /// <param name="name">The global name, UTF-8; a <c>"..."u8</c> literal.</param>
    /// <returns>
    ///     <see langword="false" /> when the global is undefined, is not a function, or reading it raised; the stack is
    ///     unchanged then.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryPush(LuaState state, LuaRef cache, ReadOnlySpan<byte> name)
    {
        using var operation = LuaRuntime.EnterStateOperation(state);
        if (state.TryPushRef(cache))
        {
            if (state.IsFunction(-1)) return true;
            state.Pop(1);
        }

        return Resolve(state, cache, name);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool Resolve(LuaState state, LuaRef cache, ReadOnlySpan<byte> name)
    {
        var top = state.Top;
        // Capture both the attachment epoch and state generation before Lua can run. A globals __index handler can
        // execute a supported in-place reset, which preserves the attach epoch but makes old registry slots unsafe.
        var identity = LuaRuntime.CurrentStateIdentity;
        // A globals __index handler can execute arbitrary Lua, including a cross-thread synchronize call.
        // Do not hold the resolution gate while it runs.
        var status = state.TryGetGlobal(name);
        if (!status.IsOk || !state.IsFunction(-1))
        {
            state.SetTop(top);
            return false;
        }

        lock (SResolveGate)
        {
            if (identity != LuaRuntime.CurrentStateIdentity)
            {
                state.SetTop(top);
                return false;
            }

            // Another thread may have resolved it while this one waited for the gate.
            if (state.TryPushRef(cache))
            {
                if (state.IsFunction(-1))
                {
                    state.Remove(-2);
                    return true;
                }

                state.Pop(1);
            }

            state.PushValue(-1);
            status = LuaReferences.Create(state, out var reference);
            if (!status.IsOk)
            {
                state.SetTop(top);
                return false;
            }

            // Use the snapshot from before TryGetGlobal. Rebinding an old slot with the current generation would make it
            // appear usable after a reset that ran from __index.
            cache.Rebind(reference, identity);
            return true;
        }
    }
}
