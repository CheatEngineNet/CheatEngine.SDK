using System.Globalization;
using System.Numerics;
using System.Text;

using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Lua.Tests.Support;
using CheatEngine.SDK.Tests.Shared.NativeLua;

using FsCheck;
using FsCheck.Fluent;

namespace CheatEngine.SDK.Lua.Tests.Marshalling;

/// <summary>Generated-input checks for the lossless Lua integer conversion contract.</summary>
[Trait("Category", "NativeLua")]
public sealed class LuaIntegerFuzzTests
{
	private const long FloatExactLimit = 1L << 53;
	private static readonly Config s_configuration = Config.QuickThrowOnFailure.WithMaxTest(1000).WithEndSize(128);

	[Fact]
	public void Generated_integer_numerals_match_an_independent_arithmetic_oracle()
	{
		LuaTest.RequireNativeLua();
		Prop.ForAll<byte[]>(values =>
		{
			string numeral = BuildNumeral(Bound(values, 128));
			byte[] utf8 = Encoding.UTF8.GetBytes(numeral);
			(bool expectedAccepted, long expectedValue) = TryParseIntegerNumeral(utf8);

			using NativeLuaState state = new(false);
			LuaState L = LuaTest.View(state);
			L.PushString(utf8);

			Assert.Equal(expectedAccepted, Int64Marshaller.TryRead(L, 1, out long actualValue));
			Assert.Equal(expectedAccepted ? expectedValue : 0L, actualValue);
			Assert.Equal(LuaType.String, L.TypeOf(1));
			Assert.Equal(1, L.Top);
			Assert.True(L.TryReadUtf8(1, out ReadOnlySpan<byte> stored));
			Assert.True(stored.SequenceEqual(utf8));
			return true;
		}).Check(s_configuration);
	}

	[Fact]
	public void Generated_integer_and_float_values_keep_exactness_and_never_change_the_slot()
	{
		LuaTest.RequireNativeLua();
		Prop.ForAll<long>(value =>
		{
			long exact = value % (FloatExactLimit - 1);
			double fractional = (value % 1_000_000) + 0.5;

			using NativeLuaState state = new(false);
			LuaState L = LuaTest.View(state);
			L.PushInteger(value);
			L.PushNumber(exact);
			L.PushNumber(fractional);
			L.PushNumber(FloatExactLimit);
			L.PushNumber(-FloatExactLimit);

			Assert.True(Int64Marshaller.TryRead(L, 1, out long integerRead));
			Assert.Equal(value, integerRead);
			Assert.True(Int64Marshaller.TryRead(L, 2, out long exactRead));
			Assert.Equal(exact, exactRead);
			Assert.False(Int64Marshaller.TryRead(L, 3, out long fractionalRead));
			Assert.Equal(0, fractionalRead);
			Assert.False(Int64Marshaller.TryRead(L, 4, out long positiveBoundaryRead));
			Assert.Equal(0, positiveBoundaryRead);
			Assert.False(Int64Marshaller.TryRead(L, 5, out long negativeBoundaryRead));
			Assert.Equal(0, negativeBoundaryRead);
			Assert.True(L.IsInteger(1));
			Assert.False(L.IsInteger(2));
			Assert.False(L.IsInteger(3));
			Assert.False(L.IsInteger(4));
			Assert.False(L.IsInteger(5));
			Assert.Equal(5, L.Top);
			return true;
		}).Check(s_configuration);
	}

	[Theory]
	[InlineData("0xFFFFFFFFFFFFFFFF", -1L)]
	[InlineData("-0xFFFFFFFFFFFFFFFF", 1L)]
	[InlineData("0x000000000000000000001", 1L)]
	[InlineData("-9223372036854775808", long.MinValue)]
	[InlineData("9223372036854775807", long.MaxValue)]
	public void Integer_numeral_boundaries_preserve_lua_signed_wrap_policy(string numeral, long expected)
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		L.PushString(numeral.AsSpan());

		Assert.True(Int64Marshaller.TryRead(L, 1, out long actual));
		Assert.Equal(expected, actual);
		Assert.Equal(LuaType.String, L.TypeOf(1));
		Assert.Equal(1, L.Top);
	}

	private static byte[] Bound(byte[] values, int maximumLength)
	{
		return values.Length <= maximumLength ? values : values[..maximumLength];
	}

	private static string BuildNumeral(byte[] values)
	{
		byte selector = At(values, 0);
		string spaces = BuildSpaces(values, 1);
		return (selector % 8) switch
		{
			0 => spaces + unchecked((long) ReadUInt64(values, 8)).ToString(CultureInfo.InvariantCulture) + spaces,
			1 => BuildHexadecimal(values, spaces),
			2 => spaces + "9223372036854775808" + spaces,
			3 => spaces + "0x10000000000000000" + spaces,
			4 => spaces + "12.5" + spaces,
			5 => spaces + "1e3" + spaces,
			6 => spaces + "42\0" + Convert.ToHexString(Bound(values, 8)),
			_ => "\u00A0" + spaces + "17" + spaces
		};
	}

	private static string BuildHexadecimal(byte[] values, string spaces)
	{
		const string Hex = "0123456789abcdef";
		int digits = (At(values, 2) % 16) + 1;
		int leadingZeros = At(values, 3) % 33;
		StringBuilder text = new(spaces);
		if ((At(values, 4) & 1) != 0)
		{
			text.Append('-');
		}

		text.Append((At(values, 5) & 1) == 0 ? "0x" : "0X");
		text.Append('0', leadingZeros);
		for (int i = 0; i < digits; i++)
		{
			text.Append(Hex[At(values, i + 6) & 0x0F]);
		}

		return text.Append(spaces).ToString();
	}

	private static string BuildSpaces(byte[] values, int offset)
	{
		ReadOnlySpan<char> whitespace = [' ', '\t', '\n', '\v', '\f', '\r'];
		StringBuilder text = new();
		for (int i = 0; i < At(values, offset) % 5; i++)
		{
			text.Append(whitespace[At(values, offset + i + 1) % whitespace.Length]);
		}

		return text.ToString();
	}

	private static (bool Accepted, long Value) TryParseIntegerNumeral(ReadOnlySpan<byte> utf8)
	{
		foreach (byte utf8Byte in utf8)
		{
			if (utf8Byte > 0x7F)
			{
				return (false, 0);
			}
		}

		string text = Encoding.ASCII.GetString(utf8).Trim(' ', '\t', '\n', '\v', '\f', '\r');
		if (text.Length == 0)
		{
			return (false, 0);
		}

		bool negative = text[0] == '-';
		int signLength = text[0] is '-' or '+' ? 1 : 0;
		string unsigned = text[signLength..];
		if (unsigned.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			string digits = unsigned[2..];
			if (!IsDigits(digits, hexadecimal: true))
			{
				return (false, 0);
			}

			string significant = digits.TrimStart('0');
			if (significant.Length > 16 || !ulong.TryParse(digits, NumberStyles.AllowHexSpecifier,
					CultureInfo.InvariantCulture, out ulong bits))
			{
				return (false, 0);
			}

			return (true, negative ? unchecked((long) (0UL - bits)) : unchecked((long) bits));
		}

		if (!IsDigits(unsigned, hexadecimal: false) ||
			!BigInteger.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out BigInteger value) ||
			value < long.MinValue || value > long.MaxValue)
		{
			return (false, 0);
		}

		return (true, (long) value);
	}

	private static bool IsDigits(string text, bool hexadecimal)
	{
		foreach (char character in text)
		{
			if (hexadecimal ? !Uri.IsHexDigit(character) : character is < '0' or > '9')
			{
				return false;
			}
		}

		return text.Length > 0;
	}

	private static byte At(byte[] values, int index) => values.Length == 0 ? (byte) 0 : values[index % values.Length];

	private static ulong ReadUInt64(byte[] values, int offset)
	{
		ulong result = 0;
		for (int i = 0; i < sizeof(ulong); i++)
		{
			result |= (ulong) At(values, offset + i) << (i * 8);
		}

		return result;
	}
}
