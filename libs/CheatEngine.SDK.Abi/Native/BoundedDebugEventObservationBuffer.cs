using System;
using System.Collections.Generic;
using System.Threading;

namespace CheatEngine.SDK.Abi.Native;

/// <summary>
///     A bounded, copied-observation handoff for a classic debug-event dispatcher.
/// </summary>
/// <remarks>
///     Publication calls no reader and never awaits asynchronous work. It uses a short monitor lock to protect the
///     queue, so it makes no lock-free or hard-latency claim. Full-buffer loss applies only to copied observations; it
///     cannot change the native continuation disposition.
/// </remarks>
public sealed class BoundedDebugEventObservationBuffer
{
    private readonly Lock _gate = new();
    private readonly Queue<DebugEventObservation> _items;
    private readonly DebugEventObservationOverflowPolicy _overflowPolicy;
    private long _droppedObservationCount;

    /// <summary>
    ///     Initializes a bounded copied-observation buffer.
    /// </summary>
    /// <param name="capacity">The maximum number of copied observations retained at once.</param>
    /// <param name="overflowPolicy">The policy to apply when <paramref name="capacity" /> observations are retained.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="capacity" /> is not positive or <paramref name="overflowPolicy" /> is not a defined policy.
    /// </exception>
    public BoundedDebugEventObservationBuffer(int capacity, DebugEventObservationOverflowPolicy overflowPolicy)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        if (overflowPolicy is not DebugEventObservationOverflowPolicy.DropNewest and not DebugEventObservationOverflowPolicy.DropOldest)
            throw new ArgumentOutOfRangeException(nameof(overflowPolicy));

        Capacity = capacity;
        _overflowPolicy = overflowPolicy;
        _items = new Queue<DebugEventObservation>(capacity);
    }

    /// <summary>Gets the maximum number of copied observations the buffer retains.</summary>
    public int Capacity { get; }

    /// <summary>Gets the configured full-buffer policy.</summary>
    public DebugEventObservationOverflowPolicy OverflowPolicy => _overflowPolicy;

    /// <summary>Gets the number of observations dropped because the bounded buffer was full.</summary>
    public long DroppedObservationCount => Interlocked.Read(ref _droppedObservationCount);

    /// <summary>Gets the current number of retained observations.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _items.Count;
            }
        }
    }

    /// <summary>
    ///     Attempts to publish a copied observation without waiting for a reader.
    /// </summary>
    /// <param name="observation">The scalar observation to retain.</param>
    /// <returns>
    ///     <see langword="true" /> when the input was retained; <see langword="false" /> when
    ///     <see cref="DebugEventObservationOverflowPolicy.DropNewest" /> discarded it.
    /// </returns>
    public bool TryPublish(in DebugEventObservation observation)
    {
        lock (_gate)
        {
            if (_items.Count == Capacity)
            {
                Interlocked.Increment(ref _droppedObservationCount);
                if (_overflowPolicy is DebugEventObservationOverflowPolicy.DropNewest) return false;

                _items.Dequeue();
            }

            _items.Enqueue(observation);
            return true;
        }
    }

    /// <summary>
    ///     Attempts to remove the oldest retained observation.
    /// </summary>
    /// <param name="observation">The removed observation, or the default value when no observation was retained.</param>
    /// <returns><see langword="true" /> when an observation was removed.</returns>
    public bool TryRead(out DebugEventObservation observation)
    {
        lock (_gate)
        {
            if (_items.Count == 0)
            {
                observation = default;
                return false;
            }

            observation = _items.Dequeue();
            return true;
        }
    }
}
