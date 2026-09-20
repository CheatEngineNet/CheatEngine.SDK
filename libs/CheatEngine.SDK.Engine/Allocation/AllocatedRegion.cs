using System;
using System.Threading;
using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Errors;
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
///     address from being freed twice.
/// </remarks>
public sealed class AllocatedRegion : IDisposable
{
    private readonly Address _address;
    private readonly ITargetMemoryAllocationOperations _operations;
    private readonly TargetAllocationSize _size;
    private int _released;

    internal AllocatedRegion(ITargetMemoryAllocationOperations operations, Address address, TargetAllocationSize size)
    {
        ArgumentNullException.ThrowIfNull(operations);
        if (address.IsZero)
            throw new ArgumentException("An allocated region needs a nonzero target address.", nameof(address));
        if (size.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(size), size.Value,
                "An allocated region needs a positive allocation size.");

        _operations = operations;
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
            _ = _operations.TryDeallocate(_address, _size);
        }
        catch (Exception)
        {
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

        if (!_operations.TryDeallocate(_address, _size))
            throw new EngineOperationFailedException("TargetMemoryDeallocate");
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
