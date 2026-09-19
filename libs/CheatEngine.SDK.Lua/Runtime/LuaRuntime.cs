using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Lua.Callbacks;
using CheatEngine.SDK.Lua.Interop.Api;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Lua.Runtime;

/// <summary>
///     The ambient binding between this SDK copy and its host: one per assembly load context (one per plugin), owned by
///     this assembly so that <c>CheatEngine.SDK.Engine</c> and generated code can reach the host's Lua state without
///     referencing <c>CheatEngine.SDK.Hosting</c> or the ABI.
/// </summary>
/// <remarks>
///     <para>
///         <b>Lifecycle.</b> <c>CheatEngine.SDK.Hosting</c> calls <see cref="Attach" /> in the enable callback, after
///         binding <c>CheatEngine.SDK.Lua.Interop.Api.LuaApi</c>, and <see cref="Detach" /> in the disable callback.
///         Every attach advances <see cref="Epoch" />, which invalidates every <see cref="References.LuaRef" /> created before it (disable and
///         re-enable,
///         and
///         any Lua state reset in between, look the same from here). Detach neutralizes and frees every live
///         <see cref="Callbacks.LuaCallback" /> while the state can still be reached, so no managed state is ever freed
///         behind a
///         closure
///         Lua can still call.
///     </para>
///     <para>
///         <b>Acquiring a state.</b> Cheat Engine hands out one Lua thread per OS thread, so <see cref="AcquireState" />
///         asks
///         the provider once per operation and the result is used on the calling thread for that operation only. Inside a
///         callback the state the callback received is authoritative; do not acquire another. A state must never be
///         stored.
///     </para>
///     <para>
///         <b>Thread safety.</b> Attach and Detach are serialized by a lock and normally run on the host's main thread;
///         the
///         readers (<see cref="IsAttached" />, <see cref="Epoch" />, <see cref="AcquireState" />, ...) are lock-free
///         volatile
///         reads and may run on any thread. A reader that observes the binding while Detach runs completes with the
///         binding
///         it read; the host guarantees that the provider stays callable until the disable callback returns.
///     </para>
/// </remarks>
public static unsafe class LuaRuntime
{
    private static readonly Lock SGate = new();

    private static LuaHostServices? s_services;
    private static int s_epoch;

    /// <summary>Gets a value indicating whether a host binding is attached. Lock-free; any thread.</summary>
    public static bool IsAttached => Volatile.Read(ref s_services) is not null;

    /// <summary>
    ///     Gets the attach counter: 0 before the first <see cref="Attach" />, incremented by every attach, unchanged by
    ///     <see cref="Detach" />. A <see cref="References.LuaRef" /> is current only while its epoch equals this value.
    ///     Lock-free; any
    ///     thread.
    /// </summary>
    public static int Epoch
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref s_epoch);
    }

    /// <summary>
    ///     Gets a value indicating whether the calling thread is the host's main thread. <see langword="false" /> while
    ///     detached.
    /// </summary>
    public static bool IsMainThread
    {
        get
        {
            var services = Volatile.Read(ref s_services);
            return services is not null && services.MainThreadId == Environment.CurrentManagedThreadId;
        }
    }

    /// <summary>Gets the attached binding, or <see langword="default" /> while detached.</summary>
    public static LuaHostBinding CurrentBinding => Volatile.Read(ref s_services)?.Binding ?? default;

    /// <summary>
    ///     Publishes a host binding and advances <see cref="Epoch" />. Called by the host's enable callback; when a binding
    ///     is already attached it is replaced (its live callbacks are neutralized first, as in <see cref="Detach" />).
    /// </summary>
    /// <param name="binding">The binding; its state provider must not be zero.</param>
    /// <exception cref="ArgumentException"><paramref name="binding" /> has no state provider.</exception>
    /// <remarks>
    ///     Requires <c>CheatEngine.SDK.Lua.Interop.Api.LuaApi</c> to be bound already (asserted in Debug builds): every
    ///     operation after this call goes through it.
    /// </remarks>
    public static void Attach(in LuaHostBinding binding)
    {
        if (!binding.IsValid)
            throw new ArgumentException("The host binding has no Lua state provider.", nameof(binding));

        Debug.Assert(LuaApi.IsInitialized,
            "CheatEngine.SDK.Lua.Interop.Api.LuaApi must be bound before the runtime is attached.");

        lock (SGate)
        {
            var previous = s_services;
            if (previous is not null) LuaCallbackRegistry.DetachAll(previous);

            Interlocked.Increment(ref s_epoch);
            Volatile.Write(ref s_services, new LuaHostServices(binding));
        }
    }

    /// <summary>
    ///     Withdraws the host binding. Called by the host's disable callback while the provider is still valid: every
    ///     live <see cref="LuaCallback" /> is neutralized on the Lua side and its managed state freed before the binding
    ///     goes away. Idempotent. Does not change <see cref="Epoch" />.
    /// </summary>
    public static void Detach()
    {
        lock (SGate)
        {
            var services = s_services;
            if (services is null) return;

            LuaCallbackRegistry.DetachAll(services);
            Volatile.Write(ref s_services, null);
        }
    }

    /// <summary>
    ///     Asks the host for the Lua state of the calling thread: one provider call. Use the result for the current
    ///     operation on the current thread, then let it go.
    /// </summary>
    /// <returns>The state view; never <see cref="LuaState.IsNull" />.</returns>
    /// <exception cref="InvalidOperationException">
    ///     No binding is attached (the plugin is not enabled), or the host returned no
    ///     state for this thread.
    /// </exception>
    [RequiresPluginEnabled]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LuaState AcquireState()
    {
        var services = Volatile.Read(ref s_services);
        if (services is null) ThrowDetached();

        var l = services.Provider();
        if (l is null) ThrowNoState();

        return new LuaState(l);
    }

    /// <summary>Non-throwing <see cref="AcquireState" />.</summary>
    /// <param name="state">The state view, or <see cref="LuaState.IsNull" /> on failure.</param>
    /// <returns><see langword="false" /> while detached or when the host returned no state for this thread.</returns>
    public static bool TryAcquireState(out LuaState state)
    {
        var services = Volatile.Read(ref s_services);
        if (services is null)
        {
            state = default;
            return false;
        }

        var l = services.Provider();
        state = new LuaState(l);
        return l is not null;
    }

    /// <summary>
    ///     Pushes the host's userdata for a native object (Cheat Engine's <c>LuaPushClassInstance</c>): the only way a
    ///     host object gets onto the stack. Stack: +1.
    /// </summary>
    /// <param name="state">The state to push on; the calling thread's.</param>
    /// <param name="nativeObject">
    ///     The native object pointer as the host knows it; zero is passed through and is the host's
    ///     business.
    /// </param>
    /// <exception cref="InvalidOperationException">No binding is attached, or the binding has no pusher.</exception>
    /// <remarks>
    ///     What the pusher does inside the host (allocating a userdata, building a metatable) is outside this SDK's
    ///     control; it is assumed not to raise into managed frames.
    /// </remarks>
    [RequiresPluginEnabled]
    [LuaStackEffect(1)]
    public static void PushHostObject(LuaState state, nint nativeObject)
    {
        var services = Volatile.Read(ref s_services);
        if (services is null) ThrowDetached();

        if (services.Pusher is null) ThrowNoPusher();

        services.Pusher(state.Pointer, (void*)nativeObject);
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowDetached()
    {
        throw new InvalidOperationException(
            "No host binding is attached: the plugin is not enabled, so there is no Lua state to talk to.");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowNoState()
    {
        throw new InvalidOperationException("The host returned no Lua state for the calling thread.");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowNoPusher()
    {
        throw new InvalidOperationException("The attached host binding has no host-object pusher.");
    }
}
