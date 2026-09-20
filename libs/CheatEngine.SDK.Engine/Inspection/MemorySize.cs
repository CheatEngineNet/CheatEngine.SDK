using System;
using System.Globalization;

namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>An unsigned byte count reported by Cheat Engine for a target module, section, symbol or memory region.</summary>
/// <remarks>
///     A size is deliberately not an <see cref="CheatEngine.SDK.Engine.Values.Address" />: it is a quantity, not a
///     target-process location.
///     The immutable value owns nothing and can be copied between threads.
/// </remarks>
public readonly struct MemorySize : IEquatable<MemorySize>, IComparable<MemorySize>
{
    /// <summary>Creates a byte count.</summary>
    /// <param name="value">The count in bytes.</param>
    public MemorySize(ulong value)
    {
        Value = value;
    }

    /// <summary>Gets the count in bytes.</summary>
    public ulong Value { get; }

    /// <inheritdoc />
    public bool Equals(MemorySize other)
    {
        return Value == other.Value;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is MemorySize other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    /// <inheritdoc />
    public int CompareTo(MemorySize other)
    {
        return Value.CompareTo(other.Value);
    }

    /// <summary>Formats the count as invariant decimal digits.</summary>
    /// <returns>The byte count.</returns>
    public override string ToString()
    {
        return Value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Tests two sizes for equality.</summary>
    public static bool operator ==(MemorySize left, MemorySize right)
    {
        return left.Equals(right);
    }

    /// <summary>Tests two sizes for inequality.</summary>
    public static bool operator !=(MemorySize left, MemorySize right)
    {
        return !left.Equals(right);
    }

    /// <summary>Orders two sizes by their unsigned byte counts.</summary>
    public static bool operator <(MemorySize left, MemorySize right)
    {
        return left.Value < right.Value;
    }

    /// <summary>Orders two sizes by their unsigned byte counts.</summary>
    public static bool operator >(MemorySize left, MemorySize right)
    {
        return left.Value > right.Value;
    }

    /// <summary>Orders two sizes by their unsigned byte counts.</summary>
    public static bool operator <=(MemorySize left, MemorySize right)
    {
        return left.Value <= right.Value;
    }

    /// <summary>Orders two sizes by their unsigned byte counts.</summary>
    public static bool operator >=(MemorySize left, MemorySize right)
    {
        return left.Value >= right.Value;
    }
}
