using System;
using System.Threading;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Lua.Registration;

/// <summary>Owns one published Lua registration set until it is released or disposed.</summary>
/// <remarks>
///     The lease captures the complete Lua resource identity and exact wrapped closures. Release first rejects a stale
///     attachment/reset generation, then compares every effective global with <see cref="LuaState.RawEquals" /> before
///     it writes. It is therefore not a cross-plugin compare-and-set primitive: protected global lookup and assignment
///     may invoke user <c>__index</c>/<c>__newindex</c> metamethods, whose arbitrary side effects cannot be rolled back.
/// </remarks>
public sealed class LuaRegistrationLease : IDisposable
{
    private LuaRegistrationSet.LeaseEntry[]? _entries;
    private LuaRegistrationReleaseOutcome _lastReleaseOutcome;

    internal LuaRegistrationLease(LuaStateIdentity identity, LuaRegistrationSet.LeaseEntry[] entries)
    {
        Identity = identity;
        _entries = entries;
        _lastReleaseOutcome = LuaRegistrationReleaseOutcome.NotAttempted();
    }

    /// <summary>Gets the attachment epoch and reset generation captured at publication.</summary>
    public LuaStateIdentity Identity { get; }

    /// <summary>Gets whether ownership has already been consumed by <see cref="ReleaseWithOutcome()" /> or <see cref="Dispose" />.</summary>
    public bool IsDisposed => Volatile.Read(ref _entries) is null;

    /// <summary>Gets the factual outcome of the one completed release attempt.</summary>
    public LuaRegistrationReleaseOutcome LastReleaseOutcome => _lastReleaseOutcome;

    /// <summary>Releases this set through a newly acquired operation and returns every factual cleanup result.</summary>
    /// <remarks>
    ///     Ownership is consumed before the release begins. Repeated calls are idempotent and return
    ///     <see cref="LuaRegistrationReleaseKind.AlreadyReleased" />; a protected cleanup failure is not retried
    ///     implicitly because a metamethod may have performed an uncertain side effect.
    /// </remarks>
    public LuaRegistrationReleaseOutcome ReleaseWithOutcome()
    {
        var entries = Interlocked.Exchange(ref _entries, value: null);
        if (entries is null) return LuaRegistrationReleaseOutcome.AlreadyReleased();

        if (!LuaRuntime.TryAcquireOperation(out var operation))
        {
            LuaRegistrationSet.Forget(entries);
            return Store(LuaRegistrationReleaseOutcome.Stale(entries.Length));
        }

        using (operation)
        {
            return Store(LuaRegistrationSet.Release(operation.State, Identity, entries, retainFailures: false,
                out _));
        }
    }

    /// <summary>Releases this set through a caller-owned, admitted Lua state.</summary>
    /// <param name="state">The calling thread's state for the currently attached Lua universe.</param>
    /// <returns>Every factual cleanup result; a stale lease performs no Lua operation.</returns>
    public LuaRegistrationReleaseOutcome ReleaseWithOutcome(LuaState state)
    {
        if (state.IsNull) throw new ArgumentException("A registration lease needs a non-null Lua state.", nameof(state));

        var entries = Interlocked.Exchange(ref _entries, value: null);
        if (entries is null) return LuaRegistrationReleaseOutcome.AlreadyReleased();

        return Store(LuaRegistrationSet.Release(state, Identity, entries, retainFailures: false, out _));
    }

    /// <summary>Best-effort, no-throw ownership release. Calling this more than once performs no further Lua mutation.</summary>
    public void Dispose()
    {
        try
        {
            _ = ReleaseWithOutcome();
        }
        catch (Exception)
        {
            // IDisposable cleanup must never hide a caller failure. Ownership was atomically consumed before Lua work.
        }
    }

    private LuaRegistrationReleaseOutcome Store(LuaRegistrationReleaseOutcome outcome)
    {
        _lastReleaseOutcome = outcome;
        return outcome;
    }
}
