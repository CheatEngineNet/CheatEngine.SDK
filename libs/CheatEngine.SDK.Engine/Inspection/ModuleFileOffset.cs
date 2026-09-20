using System;
using System.Globalization;

namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>
///     A byte offset in a module file, distinct from an in-memory target
///     <see cref="CheatEngine.SDK.Engine.Values.Address" />.
/// </summary>
/// <remarks>
///     Cheat Engine names this field <c>FileAddress</c> in <c>enumSectionsOfModule</c>, but its documented meaning is
///     an address in the file on disk. This type prevents that offset from being passed to a target-memory API.
/// </remarks>
public readonly struct ModuleFileOffset : IEquatable<ModuleFileOffset>
{
    /// <summary>Creates a module-file offset.</summary>
    /// <param name="value">The zero-based byte offset in the module file.</param>
    public ModuleFileOffset(ulong value)
    {
        Value = value;
    }

    /// <summary>Gets the zero-based byte offset in the module file.</summary>
    public ulong Value { get; }

    /// <inheritdoc />
    public bool Equals(ModuleFileOffset other)
    {
        return Value == other.Value;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is ModuleFileOffset other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    /// <summary>Formats the offset as invariant hexadecimal.</summary>
    /// <returns>The offset in hexadecimal.</returns>
    public override string ToString()
    {
        return Value.ToString("X", CultureInfo.InvariantCulture);
    }

    /// <summary>Tests two module-file offsets for equality.</summary>
    public static bool operator ==(ModuleFileOffset left, ModuleFileOffset right)
    {
        return left.Equals(right);
    }

    /// <summary>Tests two module-file offsets for inequality.</summary>
    public static bool operator !=(ModuleFileOffset left, ModuleFileOffset right)
    {
        return !left.Equals(right);
    }
}
