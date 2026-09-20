using System;

namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>A non-empty expression resolved by Cheat Engine's symbol handler.</summary>
/// <remarks>
///     This can be a module name, export or complete Cheat Engine address expression. It is not parsed by managed code:
///     exact syntax and lookup rules remain those of the CE 7.7 symbol handler.
/// </remarks>
public readonly struct SymbolExpression : IEquatable<SymbolExpression>
{
    /// <summary>Creates a symbol expression.</summary>
    /// <param name="value">The non-empty expression passed to Cheat Engine.</param>
    /// <exception cref="ArgumentException"><paramref name="value" /> is null, empty or white space.</exception>
    public SymbolExpression(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A symbol expression must not be empty or white space.", nameof(value));

        Value = value;
    }

    /// <summary>Gets the original expression.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public bool Equals(SymbolExpression other)
    {
        return StringComparer.Ordinal.Equals(Value, other.Value);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is SymbolExpression other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return Value is null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
    }

    /// <summary>Returns the original expression.</summary>
    /// <returns>The expression passed to Cheat Engine.</returns>
    public override string ToString()
    {
        return Value ?? string.Empty;
    }

    /// <summary>Tests two expressions with ordinal comparison.</summary>
    public static bool operator ==(SymbolExpression left, SymbolExpression right)
    {
        return left.Equals(right);
    }

    /// <summary>Tests two expressions with ordinal comparison.</summary>
    public static bool operator !=(SymbolExpression left, SymbolExpression right)
    {
        return !left.Equals(right);
    }
}
