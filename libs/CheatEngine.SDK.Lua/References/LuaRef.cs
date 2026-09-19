using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using CheatEngine.SDK.Lua.Interop.Api;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Lua.References;

/// <summary>
///     A reference to a Lua value held in an SDK-private registry table, stamped with the <see cref="LuaRuntime.Epoch" />
///     it was created in. The way this SDK keeps a Lua value alive and reachable across calls without leaving it on the
///     stack: cached global functions, host objects owned by managed code, bound method closures.
/// </summary>
/// <remarks>
///     <para>
///         <b>Epoch invalidation.</b> The registry belongs to the Lua state; when the host detaches and re-attaches
///         (plugin disabled and enabled again, or the state reset in between) the epoch advances and every reference
///         created
///         before is <i>stale</i>: <see cref="IsCurrent" /> is <see langword="false" />,
///         <see cref="LuaState.TryPushRef" />
///         pushes nothing, and releasing it does nothing, because its slot number may now designate another value in
///         another registry. Code that caches a reference re-resolves it when it finds it stale.
///     </para>
///     <para>
///         <b>Ownership.</b> The holder of a <see cref="LuaRef" /> owns one registry slot and releases it explicitly with
///         <see cref="Release" /> (or <see cref="Dispose" />, which acquires the ambient state); there is no finalizer, a
///         forgotten reference keeps its value alive until the state dies. Releasing is idempotent and thread-agnostic as
///         far
///         as this type is concerned; the state passed to it follows the usual rule (the calling thread's state).
///     </para>
///     <para>
///         <b>Representation.</b> Slot number and epoch are packed in one 64-bit field written atomically, so a reader on
///         another thread never sees a slot from one epoch paired with the stamp of another. Unresolved and released
///         references hold <c>LUA_NOREF</c>. Pushing and releasing use the same gate; creating one allocates this object and
///         a
///         registry slot, which is why references are created once and reused, never per call.
///     </para>
/// </remarks>
public sealed class LuaRef : IDisposable
{
    private const int NoReference = LuaApi.LUA_NOREF;

    // High 32 bits: epoch. Low 32 bits: registry slot (LUA_NOREF when unresolved or released).
    private long _packed;

    /// <summary>
    ///     Creates an unresolved reference: <see cref="IsResolved" /> is <see langword="false" /> until code binds it
    ///     (see <c>CheatEngine.SDK.Lua.CompilerServices.LuaGlobalFunctions</c>).
    /// </summary>
    /// <remarks>Runs no Lua code, so it may be a static field initializer of a class that binds globals lazily.</remarks>
    public LuaRef()
    {
        _packed = Pack(NoReference, 0);
    }

    internal LuaRef(int reference, int epoch)
    {
        _packed = Pack(reference, epoch);
    }

    /// <summary>
    ///     Gets the slot in the SDK's private reference table, or <c>LUA_NOREF</c> (-2) when unresolved or released. <c>LUA_REFNIL</c> (-1) is a
    ///     valid reference to <c>nil</c>.
    /// </summary>
    public int Reference => Unpack(Volatile.Read(ref _packed), out _);

    /// <summary>Gets the <see cref="LuaRuntime.Epoch" /> the reference was created in; 0 for an unresolved one.</summary>
    public int Epoch
    {
        get
        {
            _ = Unpack(Volatile.Read(ref _packed), out var epoch);
            return epoch;
        }
    }

    /// <summary>
    ///     Gets a value indicating whether the reference holds a slot at all (resolved and not released), whatever its
    ///     epoch.
    /// </summary>
    public bool IsResolved => Reference != NoReference;

    /// <summary>
    ///     Gets a value indicating whether the reference holds a slot created in the current
    ///     <see cref="LuaRuntime.Epoch" />: the only state in which it may be pushed or released.
    /// </summary>
    public bool IsCurrent
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => TryGetCurrent(out _);
    }

    /// <summary>
    ///     <see cref="Release" /> with the state acquired from <see cref="LuaRuntime" />. When the runtime is detached the
    ///     slot cannot be reached and the reference is only marked released. Prefer <see cref="Release" /> where a state is at
    ///     hand.
    /// </summary>
    public void Dispose()
    {
        Release(LuaRuntime.TryAcquireState(out var state) ? state : default);
    }

    /// <summary>Reads the slot when the reference is current. One volatile read and one comparison.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryGetCurrent(out int reference)
    {
        reference = Unpack(Volatile.Read(ref _packed), out var epoch);
        return reference != NoReference && epoch == LuaRuntime.Epoch;
    }

    /// <summary>
    ///     Points the reference at a slot of the given epoch. The previous slot, if any, is not released: the caller
    ///     decides whether it still belongs to a live state.
    /// </summary>
    internal void Rebind(int reference, int epoch)
    {
        lock (LuaReferences.Gate)
        {
            Volatile.Write(ref _packed, Pack(reference, epoch));
        }
    }

    /// <summary>
    ///     Releases the registry slot (<c>luaL_unref</c>) when the reference is current, and marks the reference released
    ///     in every case. Safe to call on an unresolved, stale or already released reference: nothing happens then.
    /// </summary>
    /// <param name="state">A state of the Lua universe the reference was created in; the calling thread's state.</param>
    public unsafe void Release(LuaState state)
    {
        lock (LuaReferences.Gate)
        {
            var packed = Interlocked.Exchange(ref _packed, Pack(NoReference, 0));
            var reference = Unpack(packed, out var epoch);
            if (reference != NoReference && epoch == LuaRuntime.Epoch && !state.IsNull)
                LuaReferences.Release(state, reference);
        }
    }

    /// <summary><c>LuaRef(slot, epoch N)</c>, or <c>LuaRef(unresolved)</c>.</summary>
    public override string ToString()
    {
        var reference = Unpack(Volatile.Read(ref _packed), out var epoch);
        return reference == NoReference
            ? "LuaRef(unresolved)"
            : string.Create(CultureInfo.InvariantCulture, $"LuaRef({reference}, epoch {epoch})");
    }

    private static long Pack(int reference, int epoch)
    {
        return ((long)epoch << 32) | (uint)reference;
    }

    private static int Unpack(long packed, out int epoch)
    {
        epoch = (int)(packed >> 32);
        return (int)(uint)packed;
    }
}
