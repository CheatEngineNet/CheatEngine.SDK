using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using CESDK.Annotations.Lifetime;
using CESDK.Annotations.Threading;
using CESDK.Lua.Calls;
using CESDK.Lua.Runtime;
using CESDK.Lua.State;

namespace CESDK.Engine.Objects;

/// <summary>
///     Ownership of one Cheat Engine object that this plugin created: the wrapper that destroys it, explicitly, on
///     <see cref="Dispose" />. The handle it wraps stays a plain <typeparamref name="T" />; ownership is the wrapper,
///     not a flag on the handle.
/// </summary>
/// <typeparam name="T">The handle type: <see cref="CEObject" /> for an untyped object, or a typed wrapper struct.</typeparam>
/// <remarks>
///     <para>
///         <b>Why a generic wrapper and not a class hierarchy.</b> The same Cheat Engine class comes in both ownerships:
///         <c>createMemScan()</c> returns a scanner the plugin owns, <c>getCurrentMemscan()</c> returns the GUI's, which
///         it
///         must never destroy. With one struct per class as the borrowed handle and this wrapper as the owned form,
///         <c>MemScan</c> and <c>Owned&lt;MemScan&gt;</c> are two types with the same API underneath and no <c>Dispose</c>
///         on the borrowed one, so the compiler, not a runtime flag, keeps a borrowed object from being destroyed. A class
///         hierarchy would need either a mutable "suppress destroy" flag or two parallel hierarchies, and would
///         allocate for every borrowed object. The price is one indirection: the typed members live on
///         <see cref="Value" />.
///     </para>
///     <para>
///         <b>Lifetime.</b> Construct one only from a handle that a <c>create*</c> call just returned, or from a handle
///         another owner gave up through <see cref="Release" />: two wrappers for one object would destroy it twice.
///         <see cref="Dispose" /> calls the object's <c>destroy()</c> through a protected call and must run on Cheat
///         Engine's main thread, while the plugin is enabled; there is no finalizer (an object must never be destroyed
///         from
///         the finalizer thread, and a native object must never be freed behind Cheat Engine's back at an arbitrary time),
///         so an undisposed wrapper leaks the object until Cheat Engine exits. Dispose twice is harmless.
///     </para>
///     <para>
///         <b>Failure modes.</b> When the destroy call raises (the object is already gone, or its class refuses), the
///         wrapper is still marked disposed: a destroy is never retried, because the object may be half freed.
///         <see cref="TryDestroy" /> returns that status with the message on the stack; <see cref="Dispose" /> discards
///         it.
///         When the object cannot be reached at the time of <see cref="Dispose" />, because the plugin is not enabled or
///         the
///         host binding has no object pusher, nothing can be called: the wrapper is marked disposed and the object leaks,
///         which is why a plugin disposes what it owns before returning from its disable callback.
///     </para>
///     <para>
///         <b>Threads.</b> One owner, one thread at a time; the type is not synchronized. Reading <see cref="Value" />
///         from
///         another thread while the owner disposes is a race the caller has to prevent. Destroying is main-thread only:
///         Debug builds assert it (<see cref="LuaRuntime.IsMainThread" />) before anything is touched, Release builds do
///         not
///         check at run time, and no analyzer guards it.
///     </para>
/// </remarks>
public sealed class Owned<T> : IDisposable
    where T : struct, ICEObject<T>
{
    private T _value;

    /// <summary>Takes ownership of <paramref name="value" />.</summary>
    /// <param name="value">
    ///     A handle to an object nobody else owns: fresh from a <c>create*</c> call, or released by another
    ///     wrapper.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="value" /> is a null handle.</exception>
    public Owned(T value)
    {
        if (value.Handle.IsNull) throw new ArgumentException("A null handle cannot be owned.", nameof(value));

        _value = value;
    }

    /// <summary>
    ///     Gets the typed handle, for calling the object's members. A borrowed view: do not keep it beyond the wrapper's
    ///     life.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed or released.</exception>
    public T Value
    {
        get
        {
            if (IsDisposed) ThrowDisposed();

            return _value;
        }
    }

    /// <summary>Gets the untyped handle of the owned object.</summary>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed or released.</exception>
    public CEObject Handle => Value.Handle;

    /// <summary>
    ///     Gets a value indicating whether the wrapper no longer owns anything, after <see cref="Dispose" />,
    ///     <see cref="TryDestroy" /> or <see cref="Release" />.
    /// </summary>
    public bool IsDisposed => _value.Handle.IsNull;

    /// <summary>
    ///     Destroys the object through <see cref="TryDestroy" /> on the ambient state, discarding the outcome, and marks
    ///     the wrapper disposed. Never throws; idempotent. When the object cannot be reached, because the plugin is not
    ///     enabled or the host binding has no object pusher, nothing is called and the object is leaked (see the type's
    ///     remarks).
    /// </summary>
    /// <remarks>The Debug-only main-thread assertion of <see cref="TryDestroy" /> applies.</remarks>
    [MainThreadOnly]
    public void Dispose()
    {
        if (IsDisposed) return;

        // Detached, or attached by a host without LuaPushClassInstance: the object cannot be pushed, so destroy()
        // cannot be called. Marking the wrapper disposed keeps this method's no-throw contract; the object leaks.
        if (!LuaRuntime.TryAcquireState(out var state) || LuaRuntime.CurrentBinding.HostObjectPusher == 0)
        {
            _value = default;
            return;
        }

        using LuaFrame frame = new(state);
        _ = TryDestroy(state);
    }

    /// <summary>
    ///     The handle as a borrowed value, for passing the object to an API that does not take ownership. Same as
    ///     <see cref="Value" />, named for the intent.
    /// </summary>
    /// <returns>The typed handle.</returns>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed or released.</exception>
    public T ToBorrowed()
    {
        return Value;
    }

    /// <summary>
    ///     Gives up ownership without destroying the object: the wrapper is disposed, the object lives on, and the
    ///     returned handle is now the caller's to own (hand it to Cheat Engine, or to a new <see cref="Owned{T}" />).
    /// </summary>
    /// <returns>The typed handle.</returns>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed or released.</exception>
    public T Release()
    {
        var value = Value;
        _value = default;
        return value;
    }

    /// <summary>
    ///     Destroys the object now, through a protected <c>destroy()</c> call on <paramref name="state" />, and marks the
    ///     wrapper disposed whatever the outcome. Stack after success: unchanged; after failure: one error value, for
    ///     the caller's frame to read or discard. Already disposed: returns <see cref="LuaStatus.Ok" /> and pushes nothing.
    /// </summary>
    /// <param name="state">The calling thread's state, which must be the main thread's.</param>
    /// <returns>The status of the destroy call.</returns>
    /// <exception cref="InvalidOperationException">
    ///     The plugin is not enabled, or the attached host binding has no object pusher (an embedding without
    ///     <c>LuaPushClassInstance</c>). Nothing was called and the stack is untouched, but the wrapper is marked
    ///     disposed and the object leaks, exactly as <see cref="Dispose" /> does in the same situations.
    /// </exception>
    /// <remarks>
    ///     Debug builds assert that the caller is on the host's main thread (<see cref="LuaRuntime.IsMainThread" />)
    ///     before anything is touched; Release builds have no runtime check.
    /// </remarks>
    [MainThreadOnly]
    [RequiresPluginEnabled]
    public LuaStatus TryDestroy(LuaState state)
    {
        if (IsDisposed) return LuaStatus.Ok;

        AssertMainThread();
        var handle = _value.Handle;
        _value = default;
        return handle.TryDestroy(state);
    }

    /// <summary><c>Owned(CEObject@0x...)</c>, or <c>Owned(disposed)</c>.</summary>
    public override string ToString()
    {
        return IsDisposed ? "Owned(disposed)" : "Owned(" + _value.Handle + ")";
    }

    // The [MainThreadOnly] contract, checked at run time in Debug builds only. One volatile read of the binding, so
    // that a detach racing with the check cannot produce a spurious failure. While detached there is no main thread
    // to compare against: that case is reported by the push (InvalidOperationException) or by Dispose's early exit.
    [Conditional("DEBUG")]
    private static void AssertMainThread()
    {
        var binding = LuaRuntime.CurrentBinding;
        Debug.Assert(
            !binding.IsValid || binding.MainThreadId == Environment.CurrentManagedThreadId,
            "Owned<T> must be destroyed on Cheat Engine's main thread ([MainThreadOnly]): a worker thread has its own Lua state, and freeing a GUI or address-list object there crashes Cheat Engine.");
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowDisposed()
    {
        throw new ObjectDisposedException(typeof(Owned<T>).Name,
            "The wrapper no longer owns an object: it was disposed or released.");
    }
}
