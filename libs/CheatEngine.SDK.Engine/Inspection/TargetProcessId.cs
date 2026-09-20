using System;
using System.Globalization;

namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>A positive Windows process identifier supplied to Cheat Engine's optional <c>enumModules(processid)</c> argument.</summary>
/// <remarks>
///     This is intentionally distinct from an address and from a host handle. It is a copied scalar, owns no operating
///     system resource, and can be used from any thread; the Lua operation that consumes it is main-thread-only.
/// </remarks>
public readonly struct TargetProcessId : IEquatable<TargetProcessId>
{
    /// <summary>Creates a target process identifier.</summary>
    /// <param name="value">A positive Windows process identifier.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value" /> is zero or negative.</exception>
    public TargetProcessId(int value)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), value, "A target process identifier must be positive.");

        Value = value;
    }

    /// <summary>Gets the integer value accepted by Cheat Engine.</summary>
    public int Value { get; }

    /// <inheritdoc />
    public bool Equals(TargetProcessId other)
    {
        return Value == other.Value;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is TargetProcessId other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return Value;
    }

    /// <summary>Formats the identifier using invariant decimal digits.</summary>
    /// <returns>The decimal process identifier.</returns>
    public override string ToString()
    {
        return Value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Tests two process identifiers for equality.</summary>
    public static bool operator ==(TargetProcessId left, TargetProcessId right)
    {
        return left.Equals(right);
    }

    /// <summary>Tests two process identifiers for inequality.</summary>
    public static bool operator !=(TargetProcessId left, TargetProcessId right)
    {
        return !left.Equals(right);
    }
}
