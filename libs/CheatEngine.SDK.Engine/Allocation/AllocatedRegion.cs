using System;
using System.Threading;
using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.SDK.Engine.Allocation;

/// <summary>
///     The sole owner of one target-process allocation returned by <see cref="TargetMemoryAllocator" />.
/// </summary>
/// <remarks>
///     The region is not a managed allocation and has no finalizer: <c>deAlloc</c> must execute while the plugin remains
///     enabled. CE 7.7's catalog establishes no GUI-thread affinity for this global, so this type intentionally carries
///     no <c>MainThreadOnly</c> assertion until a live probe provides that evidence. Call <see cref="Dispose" /> in a
///     <see langword="using" /> block for best-effort, no-throw cleanup, or <see cref="Release" /> when the caller must observe a
///     failure. Both paths consume ownership before invoking CE; an expected failure, a binding/marshalling failure, or a
///     Lua exception never causes a retry. This makes concurrent and repeated cleanup deterministic and prevents a stale
///     address from being freed twice. Before invoking CE, the owner reads the current selection and refuses when it is
///     not the captured process incarnation; it never selects a process for cleanup. CE exposes no primitive that makes
///     that observation atomic with a following ambient-target Lua call, so a selection change in that external interval
///     remains unqualified rather than being represented as a stronger guarantee.
/// </remarks>
public sealed class AllocatedRegion : IDisposable
{
    private readonly Address _address;
    private readonly ITargetBoundMemoryAllocationOperations _targetBoundOperations;
    private readonly TargetProcessIncarnation _targetIncarnation;
    private readonly TargetAllocationSize _size;
    private TargetReleaseOutcome _lastReleaseOutcome;
    private int _released;

    internal AllocatedRegion(ITargetBoundMemoryAllocationOperations targetBoundOperations, Address address,
        TargetAllocationSize size,
        TargetProcessIncarnation targetIncarnation)
    {
        ArgumentNullException.ThrowIfNull(targetBoundOperations);
        if (address.IsZero)
            throw new ArgumentException("An allocated region needs a nonzero target address.", nameof(address));
        if (size.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(size), size.Value,
                "An allocated region needs a positive allocation size.");

        _targetBoundOperations = targetBoundOperations;
        _targetIncarnation = targetIncarnation;
        _address = address;
        _size = size;
    }

    /// <summary>
    ///     Gets the owned address in the attached target process.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Ownership was released or disposed.</exception>
    public Address Address
    {
        get
        {
            ThrowIfReleased();
            return _address;
        }
    }

    /// <summary>
    ///     Gets the positive byte count originally passed to <c>allocateMemory</c>.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Ownership was released or disposed.</exception>
    public TargetAllocationSize Size
    {
        get
        {
            ThrowIfReleased();
            return _size;
        }
    }

    /// <summary>
    ///     Gets a value indicating whether ownership has been consumed by <see cref="Release" /> or <see cref="Dispose" />.
    /// </summary>
    public bool IsDisposed => Volatile.Read(ref _released) != 0;

    /// <summary>Gets the copied process incarnation that was qualified when this allocation was created.</summary>
    public TargetProcessIncarnation TargetIncarnation => _targetIncarnation;

    /// <summary>Gets the factual outcome of the one release attempt, including a safe target refusal.</summary>
    public TargetReleaseOutcome LastReleaseOutcome => _lastReleaseOutcome;

    /// <summary>
    ///     Best-effort no-throw release of the target allocation. Idempotent, including concurrent calls.
    /// </summary>
    /// <remarks>
    ///     This method is intended for <see langword="using" />/<see langword="finally" /> cleanup. It intentionally discards expected CE,
    ///     Lua, binding, and marshalling failures, but still consumes ownership so a later call never retries a possibly
    ///     partial deallocation. Call <see cref="Release" /> when the outcome must be observed.
    /// </remarks>
    [RequiresPluginEnabled]
    public void Dispose()
    {
        if (!TryTakeOwnership()) return;

        try
        {
            _ = ReleaseTakenWithOutcome();
        }
        catch (Exception)
        {
            _lastReleaseOutcome = TargetReleaseOutcome.Unconfirmed(failureKind: null);
            // IDisposable cleanup must not hide another failure or retry a possibly partial CE deallocation.
        }
    }

    /// <summary>
    ///     Releases the target allocation and reports every failure to the caller.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Ownership was already released or disposed.</exception>
    /// <exception cref="EngineOperationFailedException">Cheat Engine reported that deallocation did not complete.</exception>
    /// <exception cref="EngineGlobalUnavailableException">The required CE global is absent or non-callable.</exception>
    /// <exception cref="EngineBindingException">The CE binding cannot uphold its documented contract.</exception>
    /// <exception cref="EngineMarshallingException">The binding returned an invalid success/failure shape.</exception>
    /// <exception cref="EngineLuaException">The protected CE Lua call failed.</exception>
    /// <remarks>
    ///     Ownership is consumed before the CE call. If the call throws, the region remains disposed and cannot be retried;
    ///     this is safer than attempting to free an allocation whose native state is unknown.
    /// </remarks>
    [RequiresPluginEnabled]
    public void Release()
    {
        if (!TryTakeOwnership()) ThrowDisposed();

        var outcome = ReleaseTakenWithOutcome();
        if (outcome.IsSuccess) return;

        ThrowForReleaseOutcome(outcome);
    }

    /// <summary>
    ///     Releases the target allocation and returns a structured factual outcome instead of translating an expected
    ///     or Engine-boundary result into an exception.
    /// </summary>
    /// <returns>The outcome of the one permitted deallocation attempt.</returns>
    /// <remarks>
    ///     Ownership is consumed before the CE call just as it is for <see cref="Release" />. This method does not
    ///     retry an expected failure or a boundary failure. It exposes the allocation binding outcome and a separate
    ///     <see cref="LastReleaseOutcome" /> for the target-incarnation check without inspecting exception text.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Ownership was already released or disposed.</exception>
    [RequiresPluginEnabled]
    public TargetMemoryOperationOutcome ReleaseWithOutcome()
    {
        if (!TryTakeOwnership()) ThrowDisposed();

        return ReleaseTakenWithOutcome();
    }

    /// <summary>Releases this owner and returns the target-bound outcome, including safe target refusal.</summary>
    /// <exception cref="ObjectDisposedException">Ownership was already released or disposed.</exception>
    [RequiresPluginEnabled]
    public TargetReleaseOutcome ReleaseWithTargetOutcome()
    {
        if (!TryTakeOwnership()) ThrowDisposed();

        _ = ReleaseTakenWithOutcome();
        return LastReleaseOutcome;
    }

    private TargetMemoryOperationOutcome ReleaseTakenWithOutcome()
    {
        try
        {
            var outcome = _targetBoundOperations.DeallocateBoundWithOutcome(_targetIncarnation, _address, _size,
                out var targetCheck);
            _lastReleaseOutcome = targetCheck.IsCurrent
                ? outcome.IsSuccess
                    ? TargetReleaseOutcome.Released()
                    : TargetReleaseOutcome.Unconfirmed(outcome.FailureKind)
                : TargetReleaseOutcome.Refused(targetCheck);
            return outcome;
        }
        catch (EngineException exception)
        {
            _lastReleaseOutcome = TargetReleaseOutcome.Unconfirmed(exception.Kind);
            throw;
        }
    }

    private void ThrowForReleaseOutcome(TargetMemoryOperationOutcome outcome)
    {
        if (outcome.Kind is TargetMemoryOperationOutcomeKind.TargetIdentityUnavailable or
            TargetMemoryOperationOutcomeKind.TargetIdentityMismatch)
        {
            throw new EngineTargetIdentityException("TargetMemoryDeallocate",
                LastReleaseOutcome.TargetCheck.GetValueOrDefault());
        }

        if (outcome.Kind == TargetMemoryOperationOutcomeKind.ExpectedFailure)
            throw new EngineOperationFailedException("TargetMemoryDeallocate");

        if (outcome.Kind == TargetMemoryOperationOutcomeKind.GlobalUnavailable)
            throw new EngineGlobalUnavailableException("TargetMemoryDeallocate");

        if (outcome.Kind == TargetMemoryOperationOutcomeKind.CapabilityUnavailable)
            throw new EngineCapabilityUnavailableException("TargetMemoryAllocation");

        if (outcome.Kind == TargetMemoryOperationOutcomeKind.ProtectedLuaFailure)
            throw new EngineLuaException("TargetMemoryDeallocate", outcome.LuaStatus);

        if (outcome.Kind == TargetMemoryOperationOutcomeKind.MarshallingFailure)
            throw new EngineMarshallingException("TargetMemoryDeallocate", EngineMarshallingDirection.Result,
                "a Boolean deallocation result", "a non-Boolean result");

        throw new EngineBindingException("TargetMemoryDeallocate");
    }

    private bool TryTakeOwnership()
    {
        return Interlocked.Exchange(ref _released, 1) == 0;
    }

    private void ThrowIfReleased()
    {
        if (IsDisposed) ThrowDisposed();
    }

    private static void ThrowDisposed()
    {
        throw new ObjectDisposedException(nameof(AllocatedRegion),
            "The target allocation is no longer owned: it was released or disposed.");
    }
}
