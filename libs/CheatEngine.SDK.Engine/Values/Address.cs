using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;

using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Values;

/// <summary>
///     An address in the target process: an unsigned 64-bit value with the two representations Cheat Engine's Lua API
///     uses for it, a Lua integer (the same bits, so that an address above <see cref="long.MaxValue" /> travels as a
///     negative <c>lua_Integer</c>) and a hexadecimal string, optionally prefixed with <c>0x</c>.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why a type and not <see cref="ulong" />.</b> A target address is not a pointer of this process (Cheat Engine
///         can open a 32-bit or a 64-bit target from the same 64-bit plugin), it is never dereferenced by managed code,
///         and
///         its text form is hexadecimal by convention: <c>"10"</c> is sixteen, not ten. Wrapping the value keeps that
///         convention in one place and stops a plain integer from being pushed where Cheat Engine expects an address or
///         parsed with the decimal rules of the BCL.
///     </para>
///     <para>
///         <b>Reading from Lua.</b> <see cref="TryRead" /> accepts a number (read by bit reinterpretation,
///         allocation-free)
///         or a string (parsed as hexadecimal from its UTF-8 bytes, allocation-free); anything else is
///         <see langword="false" />.
///         Cheat Engine returns addresses in both forms (<c>getAddress</c> as a number, <c>FoundList.getAddress</c> as
///         text), which is why this type, and not <see cref="AddressMarshaller" />, is the marshaller a wrapper declares
///         for an address result. The type is its own <see cref="ILuaMarshaller{T}" />: generic code names it twice,
///         <c>TryGetProperty&lt;Address, Address&gt;</c>.
///     </para>
///     <para>
///         <b>Formatting.</b> Culture-invariant everywhere. The default format (<c>G</c>) is Cheat Engine's own display
///         convention: uppercase hexadecimal, padded to 8 digits when the value fits 32 bits and to 16 otherwise, no
///         prefix. <c>X</c>, <c>x</c> and <c>Xn</c>/<c>xn</c> behave exactly like the same formats on <see cref="ulong" />
///         (minimal digits, or at least <c>n</c>).
///     </para>
///     <para>
///         <b>Parsing.</b> Hexadecimal digits with an optional <c>0x</c> or <c>0X</c> prefix, surrounded by optional ASCII
///         whitespace; any number of leading zeros; no sign, no decimal form, no separators. Overflow beyond 64 bits is a
///         failure, not a truncation.
///     </para>
///     <para>
///         Immutable value; usable from any thread. Arithmetic wraps like pointer arithmetic does.
///     </para>
/// </remarks>
public readonly struct Address :
	IEquatable<Address>,
	IComparable<Address>,
	IComparable,
	ISpanFormattable,
	IUtf8SpanFormattable,
	ILuaMarshaller<Address>
{
	/// <summary>Number of hexadecimal digits of a value that fits 32 bits, in the default format.</summary>
	private const int NarrowDigits = 8;

	/// <summary>Number of hexadecimal digits of a value that needs more than 32 bits, in the default format.</summary>
	private const int WideDigits = 16;

	/// <summary>Wraps a raw address.</summary>
	/// <param name="value">The address; 0 is <see cref="Zero" />.</param>
	public Address(ulong value)
	{
		Value = value;
	}

	/// <summary>Gets the null address, which Cheat Engine's functions return for "not found" in the numeric form.</summary>
	public static Address Zero => default;

	/// <summary>Gets the raw address.</summary>
	public ulong Value
	{
		get;
	}

	/// <summary>Gets a value indicating whether this is <see cref="Zero" />.</summary>
	public bool IsZero => Value == 0;

	/// <summary>Converts a raw value; the named form of the implicit conversion.</summary>
	/// <param name="value">The address.</param>
	/// <returns>The wrapped address.</returns>
	public static Address FromUInt64(ulong value)
	{
		return new Address(value);
	}

	/// <summary>
	///     Converts a Lua integer to an address by bit reinterpretation: the form in which Cheat Engine's functions
	///     take and return addresses as numbers, where an address above <see cref="long.MaxValue" /> is negative.
	/// </summary>
	/// <param name="bits">The 64-bit signed value.</param>
	/// <returns>The address with the same bits.</returns>
	public static Address FromInt64(long bits)
	{
		return new Address(unchecked((ulong) bits));
	}

	/// <summary>Converts a raw value.</summary>
	/// <param name="value">The address.</param>
	public static implicit operator Address(ulong value)
	{
		return new Address(value);
	}

	/// <summary>Unwraps the raw value; the named form of the explicit conversion.</summary>
	/// <returns>The raw address.</returns>
	public ulong ToUInt64()
	{
		return Value;
	}

	/// <summary>The address as the Lua integer that carries it: the same bits as a signed 64-bit value.</summary>
	/// <returns>The bits, negative above <see cref="long.MaxValue" />.</returns>
	public long ToInt64()
	{
		return unchecked((long) Value);
	}

	/// <summary>Unwraps the raw value.</summary>
	/// <param name="address">The address.</param>
	public static explicit operator ulong(Address address)
	{
		return address.Value;
	}

	/// <summary>Offsets an address; wraps on overflow like pointer arithmetic.</summary>
	/// <param name="address">The base.</param>
	/// <param name="offset">The offset, negative to go down.</param>
	/// <returns>The offset address.</returns>
	public static Address operator +(Address address, long offset)
	{
		return new Address(unchecked(address.Value + (ulong) offset));
	}

	/// <summary>Offsets an address downwards; wraps on underflow like pointer arithmetic.</summary>
	/// <param name="address">The base.</param>
	/// <param name="offset">The offset to subtract, negative to go up.</param>
	/// <returns>The offset address.</returns>
	public static Address operator -(Address address, long offset)
	{
		return new Address(unchecked(address.Value - (ulong) offset));
	}

	/// <summary>Offsets an address; the named form of <c>+</c>.</summary>
	/// <param name="offset">The offset, negative to go down.</param>
	/// <returns>The offset address.</returns>
	public Address Add(long offset)
	{
		return this + offset;
	}

	/// <summary>Offsets an address downwards; the named form of <c>-</c>.</summary>
	/// <param name="offset">The offset to subtract, negative to go up.</param>
	/// <returns>The offset address.</returns>
	public Address Subtract(long offset)
	{
		return this - offset;
	}

	/// <summary>Compares two addresses.</summary>
	/// <param name="left">First address.</param>
	/// <param name="right">Second address.</param>
	public static bool operator ==(Address left, Address right)
	{
		return left.Value == right.Value;
	}

	/// <summary>Compares two addresses.</summary>
	/// <param name="left">First address.</param>
	/// <param name="right">Second address.</param>
	public static bool operator !=(Address left, Address right)
	{
		return left.Value != right.Value;
	}

	/// <summary>Orders two addresses as unsigned values.</summary>
	/// <param name="left">First address.</param>
	/// <param name="right">Second address.</param>
	public static bool operator <(Address left, Address right)
	{
		return left.Value < right.Value;
	}

	/// <summary>Orders two addresses as unsigned values.</summary>
	/// <param name="left">First address.</param>
	/// <param name="right">Second address.</param>
	public static bool operator >(Address left, Address right)
	{
		return left.Value > right.Value;
	}

	/// <summary>Orders two addresses as unsigned values.</summary>
	/// <param name="left">First address.</param>
	/// <param name="right">Second address.</param>
	public static bool operator <=(Address left, Address right)
	{
		return left.Value <= right.Value;
	}

	/// <summary>Orders two addresses as unsigned values.</summary>
	/// <param name="left">First address.</param>
	/// <param name="right">Second address.</param>
	public static bool operator >=(Address left, Address right)
	{
		return left.Value >= right.Value;
	}

	/// <summary>
	///     Parses hexadecimal UTF-8 text: optional ASCII whitespace around it, an optional <c>0x</c>/<c>0X</c> prefix,
	///     then one or more hexadecimal digits (leading zeros allowed). Never allocates; culture-independent by
	///     construction.
	/// </summary>
	/// <param name="utf8">The text.</param>
	/// <param name="address">The parsed address, or <see cref="Zero" /> on failure.</param>
	/// <returns>
	///     <see langword="false" /> for empty text, a prefix without digits, any other character, or a value that does
	///     not fit 64 bits.
	/// </returns>
	public static bool TryParse(ReadOnlySpan<byte> utf8, out Address address)
	{
		ReadOnlySpan<byte> digits = TrimAsciiWhitespace(utf8);
		if (digits.Length >= 2 && digits[0] == (byte) '0' && (digits[1] | 0x20) == (byte) 'x')
		{
			digits = digits[2..];
		}

		if (digits.IsEmpty)
		{
			address = default;
			return false;
		}

		ulong value = 0;
		foreach (byte b in digits)
		{
			int digit = HexDigitValue(b);
			if (digit < 0 || value >> 60 != 0)
			{
				address = default;
				return false;
			}

			value = (value << 4) | (uint) digit;
		}

		address = new Address(value);
		return true;
	}

	/// <summary>
	///     Parses hexadecimal UTF-16 text with the rules of <see cref="TryParse(ReadOnlySpan{byte}, out Address)" />.
	///     Never allocates.
	/// </summary>
	/// <param name="text">The text.</param>
	/// <param name="address">The parsed address, or <see cref="Zero" /> on failure.</param>
	/// <returns><see langword="false" /> when the text is not a hexadecimal address.</returns>
	public static bool TryParse(ReadOnlySpan<char> text, out Address address)
	{
		ReadOnlySpan<char> digits = TrimAsciiWhitespace(text);
		if (digits.Length >= 2 && digits[0] == '0' && (digits[1] | 0x20) == 'x')
		{
			digits = digits[2..];
		}

		if (digits.IsEmpty)
		{
			address = default;
			return false;
		}

		ulong value = 0;
		foreach (char c in digits)
		{
			int digit = c <= 0x7F ? HexDigitValue((byte) c) : -1;
			if (digit < 0 || value >> 60 != 0)
			{
				address = default;
				return false;
			}

			value = (value << 4) | (uint) digit;
		}

		address = new Address(value);
		return true;
	}

	/// <summary>
	///     Parses hexadecimal text with the rules of <see cref="TryParse(ReadOnlySpan{byte}, out Address)" />;
	///     <see langword="null" /> is a failure.
	/// </summary>
	/// <param name="text">The text, or <see langword="null" />.</param>
	/// <param name="address">The parsed address, or <see cref="Zero" /> on failure.</param>
	/// <returns><see langword="false" /> when the text is not a hexadecimal address.</returns>
	public static bool TryParse(string? text, out Address address)
	{
		return TryParse(text.AsSpan(), out address);
	}

	/// <summary>Parses hexadecimal text, throwing when it is not an address.</summary>
	/// <param name="text">The text.</param>
	/// <returns>The parsed address.</returns>
	/// <exception cref="FormatException">
	///     The text is not a hexadecimal address (see
	///     <see cref="TryParse(ReadOnlySpan{char}, out Address)" />).
	/// </exception>
	public static Address Parse(ReadOnlySpan<char> text)
	{
		if (!TryParse(text, out Address address))
		{
			ThrowFormat(text);
		}

		return address;
	}

	/// <summary>Parses hexadecimal text, throwing when it is not an address.</summary>
	/// <param name="text">The text.</param>
	/// <returns>The parsed address.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="text" /> is <see langword="null" />.</exception>
	/// <exception cref="FormatException">The text is not a hexadecimal address.</exception>
	public static Address Parse(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		return Parse(text.AsSpan());
	}

	/// <summary>Pushes the address as a Lua integer with the same bits (<see cref="ToInt64" />).</summary>
	/// <param name="state">The state to push on.</param>
	/// <param name="value">The address.</param>
	[LuaStackEffect(1)]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Push(LuaState state, Address value)
	{
		state.PushInteger(value.ToInt64());
	}

	/// <summary>
	///     Reads an address in either of Cheat Engine's forms: a number (bit reinterpretation of the Lua integer; a float
	///     with an exact integral value qualifies) or a string (hexadecimal, optional <c>0x</c>). The stack is not
	///     modified and nothing is allocated.
	/// </summary>
	/// <param name="state">The state to read from.</param>
	/// <param name="index">An acceptable index.</param>
	/// <param name="value">The address, or <see cref="Zero" /> when the value is neither a number nor a hexadecimal string.</param>
	/// <returns><see langword="true" /> when <paramref name="value" /> holds the address.</returns>
	/// <remarks>
	///     Two C API calls either way: <c>lua_type</c> then <c>lua_tolstring</c> for a string (the type check is the one
	///     inside <see cref="LuaState.TryReadUtf8" />, so it is not repeated here), <c>lua_type</c> then
	///     <c>lua_tointegerx</c> for anything else. The string case is decided first and never falls through to the
	///     number read: Lua's own conversion would read <c>"10"</c> as ten, and <c>"1e1"</c> as a float.
	/// </remarks>
	[LuaStackEffect(0)]
	public static bool TryRead(LuaState state, int index, out Address value)
	{
		if (state.TryReadUtf8(index, out ReadOnlySpan<byte> utf8))
		{
			return TryParse(utf8, out value);
		}

		if (state.TryReadInteger(index, out long bits))
		{
			value = FromInt64(bits);
			return true;
		}

		value = default;
		return false;
	}

	/// <inheritdoc />
	public bool Equals(Address other)
	{
		return Value == other.Value;
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is Address other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return Value.GetHashCode();
	}

	/// <inheritdoc />
	public int CompareTo(Address other)
	{
		return Value.CompareTo(other.Value);
	}

	/// <inheritdoc />
	/// <exception cref="ArgumentException"><paramref name="obj" /> is not an <see cref="Address" />.</exception>
	public int CompareTo(object? obj)
	{
		if (obj is null)
		{
			return 1;
		}

		if (obj is Address other)
		{
			return CompareTo(other);
		}

		throw new ArgumentException("The object is not an Address.", nameof(obj));
	}

	/// <summary>
	///     Formats with the default format: uppercase hexadecimal, 8 digits when the value fits 32 bits, 16 otherwise, no
	///     prefix.
	/// </summary>
	/// <returns>The hexadecimal text.</returns>
	public override string ToString()
	{
		return Value.ToString(DefaultFormat(Value), CultureInfo.InvariantCulture);
	}

	/// <summary>
	///     Formats with a format string: <c>G</c> or empty for the default, <c>X</c>/<c>x</c> for minimal upper/lowercase
	///     digits, <c>Xn</c>/<c>xn</c> for at least <c>n</c> digits. The provider is ignored: addresses are
	///     culture-invariant.
	/// </summary>
	/// <param name="format">The format, or <see langword="null" /> for the default.</param>
	/// <param name="formatProvider">Ignored.</param>
	/// <returns>The hexadecimal text.</returns>
	/// <exception cref="FormatException">The format is not one of the above.</exception>
	public string ToString(string? format, IFormatProvider? formatProvider)
	{
		return IsDefaultFormat(format)
			? ToString()
			: Value.ToString(ValidateHexFormat(format!), CultureInfo.InvariantCulture);
	}

	/// <summary>Formats into a UTF-16 buffer; see <see cref="ToString(string?, IFormatProvider?)" /> for the formats.</summary>
	/// <param name="destination">The buffer.</param>
	/// <param name="charsWritten">Characters written, 0 when the buffer is too small.</param>
	/// <param name="format">The format, or empty for the default.</param>
	/// <param name="provider">Ignored.</param>
	/// <returns><see langword="false" /> when the buffer is too small.</returns>
	/// <exception cref="FormatException">The format is not supported.</exception>
	public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format,
		IFormatProvider? provider)
	{
		return Value.TryFormat(destination, out charsWritten, ResolveFormat(format, Value),
			CultureInfo.InvariantCulture);
	}

	/// <summary>
	///     Formats into a UTF-8 buffer, for pushing to Lua without allocating; see
	///     <see cref="ToString(string?, IFormatProvider?)" /> for the formats.
	/// </summary>
	/// <param name="utf8Destination">The buffer.</param>
	/// <param name="bytesWritten">Bytes written, 0 when the buffer is too small.</param>
	/// <param name="format">The format, or empty for the default.</param>
	/// <param name="provider">Ignored.</param>
	/// <returns><see langword="false" /> when the buffer is too small.</returns>
	/// <exception cref="FormatException">The format is not supported.</exception>
	public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format,
		IFormatProvider? provider)
	{
		return Value.TryFormat(utf8Destination, out bytesWritten, ResolveFormat(format, Value),
			CultureInfo.InvariantCulture);
	}

	private static string DefaultFormat(ulong value)
	{
		return value <= uint.MaxValue ? "X8" : "X16";
	}

	// Empty, "G" or "g" select the default format.
	private static bool IsDefaultFormat(ReadOnlySpan<char> format)
	{
		return format.IsEmpty || (format.Length == 1 && (format[0] | 0x20) == 'g');
	}

	// The default format resolved for the value, or the caller's hexadecimal format validated.
	private static ReadOnlySpan<char> ResolveFormat(ReadOnlySpan<char> format, ulong value)
	{
		return IsDefaultFormat(format) ? DefaultFormat(value) : ValidateHexFormat(format);
	}

	// "X" or "x", optionally followed by decimal digits: the formats this type passes through to ulong unchanged.
	private static ReadOnlySpan<char> ValidateHexFormat(ReadOnlySpan<char> format)
	{
		if (format.IsEmpty || (format[0] | 0x20) != 'x')
		{
			ThrowUnsupportedFormat(format);
		}

		for (int i = 1; i < format.Length; i++)
		{
			if (!char.IsAsciiDigit(format[i]))
			{
				ThrowUnsupportedFormat(format);
			}
		}

		return format;
	}

	private static string ValidateHexFormat(string format)
	{
		_ = ValidateHexFormat(format.AsSpan());
		return format;
	}

	private static ReadOnlySpan<byte> TrimAsciiWhitespace(ReadOnlySpan<byte> utf8)
	{
		int start = 0;
		int end = utf8.Length;
		while (start < end && IsAsciiWhitespace(utf8[start]))
		{
			start++;
		}

		while (end > start && IsAsciiWhitespace(utf8[end - 1]))
		{
			end--;
		}

		return utf8[start..end];
	}

	// Same rule as the UTF-8 form: ASCII whitespace only, so that both forms accept exactly the same text.
	private static ReadOnlySpan<char> TrimAsciiWhitespace(ReadOnlySpan<char> text)
	{
		int start = 0;
		int end = text.Length;
		while (start < end && text[start] <= 0x7F && IsAsciiWhitespace((byte) text[start]))
		{
			start++;
		}

		while (end > start && text[end - 1] <= 0x7F && IsAsciiWhitespace((byte) text[end - 1]))
		{
			end--;
		}

		return text[start..end];
	}

	private static bool IsAsciiWhitespace(byte b)
	{
		return b is (byte) ' ' or (byte) '\t' or (byte) '\n' or (byte) '\r' or (byte) '\f' or (byte) '\v';
	}

	private static int HexDigitValue(byte b)
	{
		if (b is >= (byte) '0' and <= (byte) '9')
		{
			return b - '0';
		}

		int lower = b | 0x20;
		if (lower is >= 'a' and <= 'f')
		{
			return lower - 'a' + 10;
		}

		return -1;
	}

	[DoesNotReturn]
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ThrowFormat(ReadOnlySpan<char> text)
	{
		throw new FormatException("'" + text.ToString() +
		                          "' is not a hexadecimal address (digits with an optional 0x prefix).");
	}

	[DoesNotReturn]
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ThrowUnsupportedFormat(ReadOnlySpan<char> format)
	{
		throw new FormatException("'" + format.ToString() + "' is not a supported Address format (G, X, x, Xn or xn).");
	}
}
