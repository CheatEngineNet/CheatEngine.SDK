using System;
using System.Runtime.CompilerServices;

using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Lua.Marshalling;

/// <summary>
///     The integer conversion policy of <see cref="Int64Marshaller" />, <see cref="Int32Marshaller" /> and
///     <see cref="AddressMarshaller" />: no value reaches a 64-bit integer through a lossy <see cref="double" /> (audit
///     A07-02, A07-08, A12-08, A18-18, Q21).
/// </summary>
/// <remarks>
///     <para>
///         An integer subtype is exact. A float subtype is accepted only when it is finite, integral and of magnitude
///         strictly below 2^53: from 2^53 on, a double no longer tells neighbouring integers apart, so
///         <c>9007199254740993</c> written as a float would silently read as <c>9007199254740992</c>. A string is accepted
///         (by the integer marshallers only) when its bytes are a Lua 5.3 integer numeral (optional spaces, an optional
///         sign, decimal digits or <c>0x</c> and at most 16 significant hexadecimal digits) that fits a 64-bit integer:
///         a float numeral (<c>"3.0"</c>, <c>"1e3"</c>, <c>"0x1p4"</c>) or an out-of-range decimal is refused even where
///         Lua's own <c>lua_tointegerx</c> would round it through a float.
///     </para>
///     <para>
///         The hot path, an integer subtype, costs two C API calls (<c>lua_tointegerx</c>, <c>lua_isinteger</c>); floats
///         and strings take a cold path. Nothing is allocated and the stack slot is never converted in place.
///     </para>
/// </remarks>
internal static class LuaIntegerReader
{
	// 2^53: the first magnitude at which a double cannot represent every integer.
	private const long FloatExactLimit = 1L << 53;

	// Lua 5.3 l_str2int: the decimal overflow guard ('a >= maxby10 && (a > maxby10 || d > maxlastd + neg)').
	private const ulong MaxIntegerDividedBy10 = (ulong) long.MaxValue / 10;
	private const int MaxIntegerLastDigit = (int) ((ulong) long.MaxValue % 10);

	/// <summary>
	///     Reads the value at <paramref name="index" /> as a 64-bit integer under the policy above, without changing
	///     the stack.
	/// </summary>
	/// <param name="state">The state to read from.</param>
	/// <param name="index">An acceptable index.</param>
	/// <param name="acceptIntegerNumerals">Whether a string holding an integer numeral is accepted.</param>
	/// <param name="value">The integer, or 0 when refused.</param>
	/// <returns><see langword="true" /> when <paramref name="value" /> holds the exact integer.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool TryRead(LuaState state, int index, bool acceptIntegerNumerals, out long value)
	{
		if (!state.TryReadInteger(index, out value))
		{
			return false;
		}

		return state.IsInteger(index) || AcceptConverted(state, index, acceptIntegerNumerals, ref value);
	}

	/// <summary>
	///     Whether <paramref name="text" /> is exactly a Lua 5.3 integer numeral that fits a 64-bit integer: optional
	///     whitespace, an optional sign, then decimal digits (no overflow) or <c>0x</c>/<c>0X</c> and 1 to 16 significant
	///     hexadecimal digits, then optional whitespace. Anything else (a fraction, an exponent, an embedded NUL) is not.
	/// </summary>
	public static bool IsIntegerNumeral(ReadOnlySpan<byte> text)
	{
		int i = SkipSpaces(text, 0);
		bool negative = false;
		if (i < text.Length && (text[i] == (byte) '-' || text[i] == (byte) '+'))
		{
			negative = text[i] == (byte) '-';
			i++;
		}

		bool valid = i + 1 < text.Length && text[i] == (byte) '0' && (text[i + 1] | 0x20) == (byte) 'x'
			? TryScanHexadecimal(text, ref i)
			: TryScanDecimal(text, negative, ref i);
		return valid && SkipSpaces(text, i) == text.Length;
	}

	// A value lua_tointegerx converted from a float or a string: exact only under the policy.
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool AcceptConverted(LuaState state, int index, bool acceptIntegerNumerals, ref long value)
	{
		bool accepted = state.TypeOf(index) switch
		{
			// lua_tointegerx already refused a non-integral, infinite or NaN float and one outside the long range.
			LuaType.Number => value is > -FloatExactLimit and < FloatExactLimit,
			LuaType.String => acceptIntegerNumerals && state.TryReadUtf8(index, out ReadOnlySpan<byte> text) &&
							  IsIntegerNumeral(text),
			_ => false
		};

		if (!accepted)
		{
			value = 0;
		}

		return accepted;
	}

	private static bool TryScanHexadecimal(ReadOnlySpan<byte> text, ref int i)
	{
		i += 2;
		int start = i;
		int significant = 0;
		while (i < text.Length && IsHexadecimalDigit(text[i]))
		{
			if (significant > 0 || text[i] != (byte) '0')
			{
				significant++;
			}

			i++;
		}

		// Lua wraps a longer hexadecimal numeral modulo 2^64: more than 16 significant digits lose bits.
		return i > start && significant <= 16;
	}

	private static bool TryScanDecimal(ReadOnlySpan<byte> text, bool negative, ref int i)
	{
		int start = i;
		ulong accumulated = 0;
		int lastDigitLimit = MaxIntegerLastDigit + (negative ? 1 : 0);
		while (i < text.Length && text[i] >= (byte) '0' && text[i] <= (byte) '9')
		{
			int digit = text[i] - (byte) '0';
			if (accumulated >= MaxIntegerDividedBy10 &&
				(accumulated > MaxIntegerDividedBy10 || digit > lastDigitLimit))
			{
				return false;
			}

			accumulated = (accumulated * 10) + (ulong) digit;
			i++;
		}

		return i > start;
	}

	// Lua's lisspace: space, \t, \n, \v, \f, \r.
	private static int SkipSpaces(ReadOnlySpan<byte> text, int i)
	{
		while (i < text.Length && (text[i] == (byte) ' ' || (text[i] >= (byte) '\t' && text[i] <= (byte) '\r')))
		{
			i++;
		}

		return i;
	}

	private static bool IsHexadecimalDigit(byte character)
	{
		return (character >= (byte) '0' && character <= (byte) '9') || ((character | 0x20) >= (byte) 'a' &&
																		 (character | 0x20) <= (byte) 'f');
	}
}
