using System;
using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Abi;

/// <summary>
///     The 4-byte boolean of the plugin ABI (Win32 <c>BOOL</c> in the C header, <c>BOOL</c>/<c>LongBool</c> on the
///     Pascal side): zero is false, <b>every</b> other bit pattern is true.
/// </summary>
/// <remarks>
///     <para>
///         A single <see cref="int" /> field, so the type is blittable and is passed and returned exactly like an
///         <see cref="int" /> by the unmanaged calling conventions used here. That equivalence is exercised by
///         <c>CheatEngine.SDK.Abi.Tests</c> on x64 (a function returning a raw <see cref="int" /> is called through a
///         signature returning <see cref="Bool32" />, and the other way round). C# <see cref="bool" /> never appears in
///         a layout or in an unmanaged signature of this assembly: with runtime marshalling disabled it would be a 1-byte,
///         non-normalised value.
///     </para>
///     <para>
///         <b>Why a dedicated type instead of <see cref="int" />:</b> comparing a host-produced value with 1 is a bug
///         waiting to happen. The Pascal side is free to represent true with all bits set (the Pascal
///         language rules for <c>LongBool</c> allow it), so truthiness is the only portable reading. Equality on this
///         type is therefore defined on truthiness, not on the raw bits.
///     </para>
///     <para>
///         <b>Evidence for the width:</b> every boolean result and flag in <c>cepluginsdk.h</c> is declared with the
///         Win32 <c>BOOL</c> typedef and <c>cepluginsdk.pas</c> uses the Windows unit's <c>BOOL</c> for the same
///         positions (verified by reading both files of the CE 7.7.0.10621 installation). The few places where the Pascal
///         unit uses the 1-byte <c>boolean</c> instead are modelled with <see cref="Bool8" /> or left unmapped.
///     </para>
///     <para>Immutable value type: safe to use from any thread.</para>
/// </remarks>
/// <remarks>Wraps a raw 32-bit value exactly as it crossed the boundary.</remarks>
/// <param name="rawValue">Zero for false; any other value reads as true and is preserved bit for bit.</param>
[StructLayout(LayoutKind.Sequential)]
public readonly struct Bool32(int rawValue) : IEquatable<Bool32>
{
	/// <summary>Gets the false value (raw 0).</summary>
	public static Bool32 False => default;

	/// <summary>Gets the canonical true value written by this SDK (raw 1, the Win32 <c>TRUE</c>).</summary>
	public static Bool32 True => new(1);

	/// <summary>Gets the raw 32-bit pattern. Diagnostic use only: do not compare it with 1.</summary>
	public int RawValue
	{
		get;
	} = rawValue;

	/// <summary>Gets a value indicating whether the raw pattern is non-zero.</summary>
	public bool IsTrue => RawValue != 0;

	/// <summary>Converts a managed boolean to the canonical raw 1 / raw 0 representation.</summary>
	/// <param name="value">The managed boolean.</param>
	/// <returns><see cref="True" /> or <see cref="False" />.</returns>
	public static Bool32 FromBoolean(bool value)
	{
		return new Bool32(value ? 1 : 0);
	}

	/// <summary>Reads the value as a managed boolean (non-zero is true).</summary>
	/// <returns><see langword="true" /> when the raw pattern is non-zero.</returns>
	public bool ToBoolean()
	{
		return RawValue != 0;
	}

	/// <summary>Lossless conversion from a managed boolean (raw 1 / raw 0).</summary>
	/// <param name="value">The managed boolean.</param>
	public static implicit operator Bool32(bool value)
	{
		return FromBoolean(value);
	}

	/// <summary>Truthiness of the raw pattern. Explicit because the raw bits are dropped.</summary>
	/// <param name="value">The ABI boolean.</param>
	public static explicit operator bool(Bool32 value)
	{
		return value.RawValue != 0;
	}

	/// <summary>Lets the value be used directly as a condition (<c>if (result) ...</c>).</summary>
	/// <param name="value">The ABI boolean.</param>
	/// <returns><see langword="true" /> when the raw pattern is non-zero.</returns>
	public static bool operator true(Bool32 value)
	{
		return value.RawValue != 0;
	}

	/// <summary>Counterpart of the <see langword="true" /> operator.</summary>
	/// <param name="value">The ABI boolean.</param>
	/// <returns><see langword="true" /> when the raw pattern is zero.</returns>
	public static bool operator false(Bool32 value)
	{
		return value.RawValue == 0;
	}

	/// <summary>Logical negation (<c>if (!result) ...</c>).</summary>
	/// <param name="value">The ABI boolean.</param>
	/// <returns><see langword="true" /> when the raw pattern is zero.</returns>
	public static bool operator !(Bool32 value)
	{
		return value.RawValue == 0;
	}

	/// <summary>Compares truthiness, not raw bits: raw 1 and raw -1 are equal.</summary>
	/// <param name="left">The first value.</param>
	/// <param name="right">The second value.</param>
	/// <returns><see langword="true" /> when both values are true or both are false.</returns>
	public static bool operator ==(Bool32 left, Bool32 right)
	{
		return left.Equals(right);
	}

	/// <summary>Compares truthiness, not raw bits.</summary>
	/// <param name="left">The first value.</param>
	/// <param name="right">The second value.</param>
	/// <returns><see langword="true" /> when exactly one of the values is true.</returns>
	public static bool operator !=(Bool32 left, Bool32 right)
	{
		return !left.Equals(right);
	}

	/// <summary>Compares truthiness, not raw bits.</summary>
	/// <param name="other">The value to compare with.</param>
	/// <returns><see langword="true" /> when both values are true or both are false.</returns>
	public bool Equals(Bool32 other)
	{
		return RawValue != 0 == (other.RawValue != 0);
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is Bool32 other && Equals(other);
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
