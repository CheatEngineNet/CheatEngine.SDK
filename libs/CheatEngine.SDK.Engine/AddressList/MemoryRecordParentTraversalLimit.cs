using System;

namespace CheatEngine.SDK.Engine.AddressList;

/// <summary>An explicit bound for the parent-chain validation performed before a record reparenting command.</summary>
public readonly struct MemoryRecordParentTraversalLimit
{
    /// <summary>Initializes a positive parent-chain traversal bound.</summary>
    /// <param name="maximumHops">Maximum parent links inspected before the requested assignment.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maximumHops" /> is not positive.</exception>
    public MemoryRecordParentTraversalLimit(int maximumHops)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumHops);

        MaximumHops = maximumHops;
    }

    /// <summary>Gets the maximum parent links inspected.</summary>
    public int MaximumHops { get; }

    /// <summary>Gets the conservative default validation bound.</summary>
    public static MemoryRecordParentTraversalLimit Default => new(4096);
}
