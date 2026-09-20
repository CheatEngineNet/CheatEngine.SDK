using System;

namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>A non-empty Cheat Engine module name used as the string form of <c>enumSectionsOfModule</c>'s selector.</summary>
/// <remarks>
///     The value is passed to Lua as UTF-8 text and is not a file-system path. It is an immutable managed value and owns
///     no Cheat Engine object.
/// </remarks>
public readonly struct ModuleName : IEquatable<ModuleName>
{
    /// <summary>Creates a module-name selector.</summary>
    /// <param name="value">The non-empty module name understood by Cheat Engine's symbol handler.</param>
    /// <exception cref="ArgumentException"><paramref name="value" /> is null, empty or white space.</exception>
    public ModuleName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A module name must not be empty or white space.", nameof(value));

        Value = value;
    }

    /// <summary>Gets the module name as supplied by the caller.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public bool Equals(ModuleName other)
    {
        return StringComparer.Ordinal.Equals(Value, other.Value);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is ModuleName other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return Value is null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
    }

    /// <summary>Returns the module name.</summary>
    /// <returns>The original module name.</returns>
    public override string ToString()
    {
        return Value ?? string.Empty;
    }

    /// <summary>Tests two module names with ordinal comparison.</summary>
    public static bool operator ==(ModuleName left, ModuleName right)
    {
        return left.Equals(right);
    }

    /// <summary>Tests two module names with ordinal comparison.</summary>
    public static bool operator !=(ModuleName left, ModuleName right)
    {
        return !left.Equals(right);
    }
}
