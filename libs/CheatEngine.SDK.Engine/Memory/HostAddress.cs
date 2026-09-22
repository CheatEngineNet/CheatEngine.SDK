using System;
using System.Globalization;

namespace CheatEngine.SDK.Engine.Memory;

/// <summary>An address in Cheat Engine's own process, distinct from an <c>Address</c> in the attached target process.</summary>
/// <remarks>
///     <para>
///         CE 7.7's <c>*Local</c> memory globals address Cheat Engine itself. This value intentionally does not convert
///         implicitly to or from <c>CheatEngine.SDK.Engine.Values.Address</c>: a target process may have a different
///         pointer width from the host, and mixing the two address spaces is a correctness error rather than a numeric
///         conversion.
///     </para>
///     <para>
///         The value is pointer-sized because the <c>readPointerLocal</c> and <c>writePointerLocal</c> globals use the
///         architecture of the Cheat Engine host. The supported CE 7.7 plugin host is Windows x64; no managed pointer is
///         ever dereferenced by this type.
///     </para>
/// </remarks>
public readonly struct HostAddress : IEquatable<HostAddress>, IFormattable
{
	/// <summary>Initializes an address in Cheat Engine's host process.</summary>
	/// <param name="value">The pointer-sized address.</param>
	public HostAddress(nuint value)
	{
		Value = value;
	}

	/// <summary>Gets the null address.</summary>
	public static HostAddress Zero => default;

	/// <summary>Gets the pointer-sized numeric value.</summary>
	public nuint Value
	{
		get;
	}

	/// <summary>Gets a value indicating whether this is the null address.</summary>
	public bool IsZero => Value == 0;

	/// <summary>Converts the value to the signed Lua integer representation without changing its bits.</summary>
	/// <returns>The pointer bits represented as a signed 64-bit Lua integer.</returns>
	public long ToInt64()
	{
		return unchecked((long) Value);
	}

	/// <summary>Creates a host address from the low pointer-sized bits of a Lua integer.</summary>
	/// <param name="value">The signed Lua integer returned by Cheat Engine.</param>
	/// <returns>The same low pointer-sized bits as a host address.</returns>
	public static HostAddress FromInt64(long value)
	{
		return new HostAddress(unchecked((nuint) value));
	}

	/// <summary>Compares two host-process addresses.</summary>
	/// <param name="left">The first address.</param>
	/// <param name="right">The second address.</param>
	/// <returns><see langword="true" /> when the values are equal.</returns>
	public static bool operator ==(HostAddress left, HostAddress right)
	{
		return left.Value == right.Value;
	}

	/// <summary>Compares two host-process addresses.</summary>
	/// <param name="left">The first address.</param>
	/// <param name="right">The second address.</param>
	/// <returns><see langword="true" /> when the values differ.</returns>
	public static bool operator !=(HostAddress left, HostAddress right)
	{
		return left.Value != right.Value;
	}

	/// <inheritdoc />
	public bool Equals(HostAddress other)
	{
		return Value == other.Value;
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is HostAddress other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return Value.GetHashCode();
	}

	/// <summary>Formats the address as uppercase hexadecimal with the host pointer width.</summary>
	/// <returns>The hexadecimal representation, without a prefix.</returns>
	public override string ToString()
	{
		return Value.ToString(IntPtr.Size == 8 ? "X16" : "X8", CultureInfo.InvariantCulture);
	}

	/// <inheritdoc />
	public string ToString(string? format, IFormatProvider? formatProvider)
	{
		return Value.ToString(format, formatProvider);
	}
}
