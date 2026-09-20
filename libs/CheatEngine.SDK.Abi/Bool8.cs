using System;
using System.Runtime.InteropServices;
using CheatEngine.SDK.Abi.Managed;

namespace CheatEngine.SDK.Abi;

/// <summary>
///     The 1-byte boolean of the plugin ABI (Pascal <c>boolean</c>): zero is false, every other value is true.
/// </summary>
/// <remarks>
///     <para>
///         A single <see cref="byte" /> field: blittable, passed and returned exactly like a <see cref="byte" />. Only the
///         low byte of the return register is meaningful for such a function, so a signature that returns this type must
///         never be declared with a 4-byte result (<see cref="Bool32" /> or <see cref="int" />): the upper 24 bits are
///         unspecified. <c>CheatEngine.SDK.Abi.Tests</c> checks on x64 that a callee leaving garbage in the upper bits
///         still reads as false through a <see cref="Bool8" /> signature.
///     </para>
///     <para>
///         <b>Where it is used:</b> <see cref="ManagedExportedFunctions.CheckSynchronize" />. <b>Evidence:</b> the public
///         7.5 host source declares that function with a Pascal <c>boolean</c> result - <i>inferred</i> for 7.7, whose
///         host is closed source. The official managed bootstrap binds the same slot through a delegate returning C#
///         <see langword="bool" /> under default marshalling, which reads 4 bytes; reading 1 byte is correct under both
///         hypotheses
///         for every value a Pascal host can realistically produce (0, 1, or all bits set), reading 4 bytes is not.
///     </para>
///     <para>Immutable value type: safe to use from any thread.</para>
/// </remarks>
/// <remarks>Wraps a raw byte exactly as it crossed the boundary.</remarks>
/// <param name="rawValue">Zero for false; any other value reads as true and is preserved.</param>
[StructLayout(LayoutKind.Sequential)]
public readonly struct Bool8(byte rawValue) : IEquatable<Bool8>
{
    /// <summary>Gets the false value (raw 0).</summary>
    public static Bool8 False => default;

    /// <summary>Gets the canonical true value written by this SDK (raw 1, the Pascal <see langword="true" />).</summary>
    public static Bool8 True => new(1);

    /// <summary>Gets the raw byte. Diagnostic use only: do not compare it with 1.</summary>
    public byte RawValue { get; } = rawValue;

    /// <summary>Gets a value indicating whether the raw byte is non-zero.</summary>
    public bool IsTrue => RawValue != 0;

    /// <summary>Converts a managed boolean to the canonical raw 1 / raw 0 representation.</summary>
    /// <param name="value">The managed boolean.</param>
    /// <returns><see cref="True" /> or <see cref="False" />.</returns>
    public static Bool8 FromBoolean(bool value)
    {
        return new Bool8(value ? (byte)1 : (byte)0);
    }

    /// <summary>Reads the value as a managed boolean (non-zero is true).</summary>
    /// <returns><see langword="true" /> when the raw byte is non-zero.</returns>
    public bool ToBoolean()
    {
        return RawValue != 0;
    }

    /// <summary>Lossless conversion from a managed boolean (raw 1 / raw 0).</summary>
    /// <param name="value">The managed boolean.</param>
    public static implicit operator Bool8(bool value)
    {
        return FromBoolean(value);
    }

    /// <summary>Truthiness of the raw byte. Explicit because the raw bits are dropped.</summary>
    /// <param name="value">The ABI boolean.</param>
    public static explicit operator bool(Bool8 value)
    {
        return value.RawValue != 0;
    }

    /// <summary>Lets the value be used directly as a condition (<c>if (result) ...</c>).</summary>
    /// <param name="value">The ABI boolean.</param>
    /// <returns><see langword="true" /> when the raw byte is non-zero.</returns>
    public static bool operator true(Bool8 value)
    {
        return value.RawValue != 0;
    }

    /// <summary>Counterpart of the <see langword="true" /> operator.</summary>
    /// <param name="value">The ABI boolean.</param>
    /// <returns><see langword="true" /> when the raw byte is zero.</returns>
    public static bool operator false(Bool8 value)
    {
        return value.RawValue == 0;
    }

    /// <summary>Logical negation (<c>if (!result) ...</c>).</summary>
    /// <param name="value">The ABI boolean.</param>
    /// <returns><see langword="true" /> when the raw byte is zero.</returns>
    public static bool operator !(Bool8 value)
    {
        return value.RawValue == 0;
    }

    /// <summary>Compares truthiness, not raw bits.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true" /> when both values are true or both are false.</returns>
    public static bool operator ==(Bool8 left, Bool8 right)
    {
        return left.Equals(right);
    }

    /// <summary>Compares truthiness, not raw bits.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true" /> when exactly one of the values is true.</returns>
    public static bool operator !=(Bool8 left, Bool8 right)
    {
        return !left.Equals(right);
    }

    /// <summary>Compares truthiness, not raw bits.</summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns><see langword="true" /> when both values are true or both are false.</returns>
    public bool Equals(Bool8 other)
    {
        return RawValue != 0 == (other.RawValue != 0);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is Bool8 other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return RawValue != 0 ? 1 : 0;
    }

    /// <summary>Returns <c>"True"</c> or <c>"False"</c>. Allocation-free (both strings are literals).</summary>
    /// <returns>The truthiness of the value as text.</returns>
    public override string ToString()
    {
        return RawValue != 0 ? "True" : "False";
    }
}
