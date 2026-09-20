using System;

namespace CheatEngine.SDK.Engine.Allocation;

/// <summary>
///     The requested number of bytes in one target-process allocation.
/// </summary>
/// <remarks>
///     The value is a positive signed 64-bit count because the CE 7.7 Lua boundary carries the <c>size</c> argument as a
///     Lua integer. It is deliberately not interchangeable with a host pointer or a target address. The host may round a
///     successful request to its page size; <see cref="Value" /> remains the request supplied to
///     <c>allocateMemory</c>, and is the same value passed back to <c>deAlloc</c> by <see cref="AllocatedRegion" />.
/// </remarks>
public readonly struct TargetAllocationSize : IEquatable<TargetAllocationSize>
{
    /// <summary>
    ///     Initializes a positive target allocation size.
    /// </summary>
    /// <param name="value">The number of requested bytes, greater than zero.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value" /> is zero or negative.</exception>
    public TargetAllocationSize(long value)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), value, "An allocation size must be positive.");

        Value = value;
    }

    /// <summary>
    ///     Gets the positive byte count supplied to Cheat Engine.
    /// </summary>
    public long Value { get; }

    /// <inheritdoc />
    public bool Equals(TargetAllocationSize other)
    {
        return Value == other.Value;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is TargetAllocationSize other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    /// <summary>
    ///     Determines whether two allocation sizes have the same byte count.
    /// </summary>
    /// <param name="left">The first allocation size.</param>
    /// <param name="right">The second allocation size.</param>
    /// <returns><see langword="true" /> when the byte counts are equal.</returns>
    public static bool operator ==(TargetAllocationSize left, TargetAllocationSize right)
    {
        return left.Equals(right);
    }

    /// <summary>
    ///     Determines whether two allocation sizes have different byte counts.
    /// </summary>
    /// <param name="left">The first allocation size.</param>
    /// <param name="right">The second allocation size.</param>
    /// <returns><see langword="true" /> when the byte counts differ.</returns>
    public static bool operator !=(TargetAllocationSize left, TargetAllocationSize right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    ///     Formats the byte count with the invariant integer format.
    /// </summary>
    /// <returns>The byte count.</returns>
    public override string ToString()
    {
        return Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

}
