using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Lua.Tests.Support;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Lua.Tests.Marshalling;

/// <summary>Every marshaller pushes a value that reads back identical, and refuses values of another kind.</summary>
[Trait("Category", "NativeLua")]
public sealed class MarshallerRoundTripTests
{
	[Theory]
	[InlineData(0)]
	[InlineData(42)]
	[InlineData(-1)]
	[InlineData(int.MinValue)]
	[InlineData(int.MaxValue)]
	public void Int32_round_trips(int value)
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);

		Int32Marshaller.Push(L, value);

		Assert.True(L.IsInteger(-1));
		Assert.True(Int32Marshaller.TryRead(L, -1, out int read));
		Assert.Equal(value, read);
		Assert.Equal(1, L.Top);
	}

	[Theory]
	[InlineData(int.MaxValue + 1L)]
	[InlineData(int.MinValue - 1L)]
	[InlineData(long.MaxValue)]
	public void Int32_refuses_values_that_do_not_fit_instead_of_truncating(long value)
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);

		Int64Marshaller.Push(L, value);

		Assert.False(Int32Marshaller.TryRead(L, -1, out int read));
		Assert.Equal(0, read);
		Assert.True(Int64Marshaller.TryRead(L, -1, out long wide));
		Assert.Equal(value, wide);
	}

	[Theory]
	[InlineData(0L)]
	[InlineData(long.MinValue)]
	[InlineData(long.MaxValue)]
	[InlineData(0x7FFF_FFFF_FFFF_FFF0L)]
	public void Int64_round_trips(long value)
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);

		Int64Marshaller.Push(L, value);

		Assert.True(Int64Marshaller.TryRead(L, -1, out long read));
		Assert.Equal(value, read);
	}

	[Theory]
	[InlineData(0UL)]
	[InlineData(0x1000UL)]
	[InlineData(0x7FFF_FFFF_FFFF_FFFFUL)]
	[InlineData(0x8000_0000_0000_0000UL)]
	[InlineData(0xFFFF_FFFF_FFFF_FFF0UL)]
	[InlineData(ulong.MaxValue)]
	public void Address_round_trips_including_values_above_long_MaxValue(ulong value)
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		UIntPtr address = (nuint) value;

		AddressMarshaller.Push(L, address);

		Assert.True(L.IsInteger(-1));
		Assert.True(AddressMarshaller.TryRead(L, -1, out UIntPtr read));
		Assert.Equal(address, read);

		// The same bits seen from Lua: an address above long.MaxValue is a negative lua_Integer.
		Assert.True(L.TryReadInteger(-1, out long bits));
		Assert.Equal(unchecked((long) value), bits);
	}

	[Fact]
	public void Address_pushed_from_lua_as_a_negative_integer_reads_as_the_high_address()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		LuaTest.Run(L, "return -16"u8, 1);

		Assert.True(AddressMarshaller.TryRead(L, -1, out UIntPtr read));
		Assert.Equal(unchecked((nuint) 0xFFFF_FFFF_FFFF_FFF0UL), read);
	}

	[Theory]
	[InlineData("00400000")] // Cheat Engine's hexadecimal text: Lua's coercion would read it as 400000 decimal.
	[InlineData("16")]
	[InlineData("0x10")]
	[InlineData("7FF6A0001000")]
	public void Address_refuses_strings_even_when_lua_could_convert_them(string text)
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		StringMarshaller.Push(L, text);

		Assert.False(AddressMarshaller.TryRead(L, -1, out UIntPtr read));
		Assert.Equal((nuint) 0, read);
		Assert.Equal(LuaType.String, L.TypeOf(-1));
	}

	[Fact]
	public void Address_accepts_a_float_with_an_integral_value_like_lua_does()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		L.PushNumber(4096.0);
		L.PushNumber(4096.5);
		L.PushNumber(9007199254740991.0);

		Assert.True(AddressMarshaller.TryRead(L, 1, out UIntPtr integral));
		Assert.Equal((nuint) 4096, integral);
		Assert.False(AddressMarshaller.TryRead(L, 2, out _));
		Assert.True(AddressMarshaller.TryRead(L, 3, out UIntPtr largest));
		Assert.Equal(unchecked((nuint) 9007199254740991UL), largest);
	}

	[Theory]
	[InlineData(0.0)]
	[InlineData(2.5)]
	[InlineData(-1e300)]
	[InlineData(double.MaxValue)]
	[InlineData(double.PositiveInfinity)]
	public void Double_round_trips(double value)
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);

		DoubleMarshaller.Push(L, value);

		Assert.False(L.IsInteger(-1));
		Assert.True(DoubleMarshaller.TryRead(L, -1, out double read));
		Assert.Equal(value, read);
	}

	[Fact]
	public void Double_nan_round_trips_as_nan()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);

		DoubleMarshaller.Push(L, double.NaN);

		Assert.True(DoubleMarshaller.TryRead(L, -1, out double read));
		Assert.True(double.IsNaN(read));
	}

	[Theory]
	[InlineData(0.0f)]
	[InlineData(1.5f)]
	[InlineData(-3.25f)]
	[InlineData(float.MaxValue)]
	public void Single_round_trips(float value)
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);

		SingleMarshaller.Push(L, value);

		Assert.True(SingleMarshaller.TryRead(L, -1, out float read));
		Assert.Equal(value, read);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void Boolean_round_trips(bool value)
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);

		BooleanMarshaller.Push(L, value);

		Assert.Equal(LuaType.Boolean, L.TypeOf(-1));
		Assert.True(BooleanMarshaller.TryRead(L, -1, out bool read));
		Assert.Equal(value, read);
	}

	[Fact]
	public void Boolean_is_strict_nil_and_numbers_are_not_booleans()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		L.PushNil();
		L.PushInteger(1);

		Assert.False(BooleanMarshaller.TryRead(L, 1, out bool fromNil));
		Assert.False(fromNil);
		Assert.False(BooleanMarshaller.TryRead(L, 2, out _));
		Assert.False(BooleanMarshaller.TryRead(L, 3, out _));
		Assert.True(L.ToBoolean(2));
	}

	[Fact]
	public void Integer_and_float_are_discriminated()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		L.PushInteger(3);
		L.PushNumber(3.0);
		L.PushNumber(2.5);
		L.PushString("42"u8);
		L.PushString("2.5"u8);
		L.PushBoolean(true);

		// 3: an integer; both marshallers accept it.
		Assert.True(L.IsInteger(1));
		Assert.True(Int64Marshaller.TryRead(L, 1, out long i1));
		Assert.Equal(3, i1);
		Assert.True(DoubleMarshaller.TryRead(L, 1, out double d1));
		Assert.Equal(3.0, d1);

		// 3.0: an integral float below 2^53 is accepted as an integer; IsInteger tells the difference.
		Assert.False(L.IsInteger(2));
		Assert.True(Int64Marshaller.TryRead(L, 2, out long i2));
		Assert.Equal(3, i2);

		// 2.5: a float that has no integer representation.
		Assert.False(Int64Marshaller.TryRead(L, 3, out _));
		Assert.False(Int32Marshaller.TryRead(L, 3, out _));
		Assert.True(DoubleMarshaller.TryRead(L, 3, out double d3));
		Assert.Equal(2.5, d3);

		// An integer numeral string reads, a float numeral does not; the string marshallers stay strict the other way
		// round (see string tests).
		Assert.True(Int64Marshaller.TryRead(L, 4, out long i4));
		Assert.Equal(42, i4);
		Assert.False(Int64Marshaller.TryRead(L, 5, out _));
		Assert.True(DoubleMarshaller.TryRead(L, 5, out double d5));
		Assert.Equal(2.5, d5);

		// A boolean is never a number.
		Assert.False(Int64Marshaller.TryRead(L, 6, out _));
		Assert.False(DoubleMarshaller.TryRead(L, 6, out _));
		Assert.False(AddressMarshaller.TryRead(L, 6, out _));

		// Nothing was converted in place.
		Assert.Equal(LuaType.String, L.TypeOf(4));
		Assert.Equal(LuaType.Number, L.TypeOf(2));
		Assert.Equal(6, L.Top);
	}

	[Theory]
	[Trait("Qualification", "Q21")]
	[InlineData(long.MinValue)]
	[InlineData(long.MaxValue)]
	[InlineData((1L << 53) + 1)]
	[InlineData(-(1L << 53) - 1)]
	[InlineData(long.MaxValue - 1)]
	public void Int64_keeps_every_bit_of_integer_subtype_values(long value)
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		Int64Marshaller.Push(L, value);
		LuaTest.Run(L, Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"return 0x{value:X16}")), 1);

		Assert.True(Int64Marshaller.TryRead(L, 1, out long pushed));
		Assert.Equal(value, pushed);
		Assert.True(L.IsInteger(2));
		Assert.True(Int64Marshaller.TryRead(L, 2, out long fromSource));
		Assert.Equal(value, fromSource);
	}

	[Theory]
	[Trait("Qualification", "Q21")]
	[InlineData("return 2^53")]
	[InlineData("return -2^53")]
	[InlineData("return 2^53 + 2")]
	[InlineData("return 1e18")]
	[InlineData("return -1e18")]
	[InlineData("return 2^63")]
	[InlineData("return 1e19")]
	[InlineData("return 9007199254740993.0")]
	public void Int64_refuses_a_float_at_or_above_2_pow_53(string source)
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		LuaTest.Run(L, Encoding.UTF8.GetBytes(source), 1);

		Assert.False(L.IsInteger(1));
		Assert.False(Int64Marshaller.TryRead(L, 1, out long read));
		Assert.Equal(0, read);
		Assert.False(Int32Marshaller.TryRead(L, 1, out _));
		Assert.Equal(LuaType.Number, L.TypeOf(1));
		Assert.Equal(1, L.Top);
	}

	[Theory]
	[Trait("Qualification", "Q21")]
	[InlineData(0.0, 0L)]
	[InlineData(-1.0, -1L)]
	[InlineData(3.0, 3L)]
	[InlineData(-4096.0, -4096L)]
	[InlineData(9007199254740991.0, 9007199254740991L)]
	[InlineData(-9007199254740991.0, -9007199254740991L)]
	public void Int64_accepts_an_integral_float_below_2_pow_53(double value, long expected)
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		L.PushNumber(value);

		Assert.False(L.IsInteger(1));
		Assert.True(Int64Marshaller.TryRead(L, 1, out long read));
		Assert.Equal(expected, read);
		Assert.False(L.IsInteger(1));
	}

	[Theory]
	[Trait("Qualification", "Q21")]
	[InlineData("42", true, 42L)]
	[InlineData("  -17\t\n", true, -17L)]
	[InlineData("+8", true, 8L)]
	[InlineData("0x10", true, 16L)]
	[InlineData(" 0XfF ", true, 255L)]
	[InlineData("-0x10", true, -16L)]
	[InlineData("0xFFFFFFFFFFFFFFFF", true, -1L)]
	[InlineData("0x000000000000000000001", true, 1L)]
	[InlineData("9007199254740993", true, 9007199254740993L)]
	[InlineData("9223372036854775807", true, long.MaxValue)]
	[InlineData("-9223372036854775808", true, long.MinValue)]
	[InlineData("9223372036854775808", false, 0L)]
	[InlineData("-9223372036854775809", false, 0L)]
	[InlineData("0x10000000000000000", false, 0L)]
	[InlineData("3.0", false, 0L)]
	[InlineData("1e3", false, 0L)]
	[InlineData("0x1p4", false, 0L)]
	[InlineData("0x", false, 0L)]
	[InlineData("", false, 0L)]
	[InlineData(" ", false, 0L)]
	[InlineData("-", false, 0L)]
	[InlineData("12abc", false, 0L)]
	[InlineData("inf", false, 0L)]
	public void Int64_accepts_integer_numeral_strings_and_refuses_float_numeral_strings(string text, bool accepted,
		long expected)
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		L.PushString(text.AsSpan());

		Assert.Equal(accepted, Int64Marshaller.TryRead(L, 1, out long read));
		Assert.Equal(expected, read);
		Assert.Equal(LuaType.String, L.TypeOf(1));
		Assert.Equal(text, LuaTest.ReadString(L, 1));
	}

	[Fact]
	[Trait("Qualification", "Q21")]
	public void Int64_refuses_a_numeral_with_an_embedded_nul()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		L.PushString("42\u00007"u8);
		L.PushString("42\u0000"u8);

		Assert.False(Int64Marshaller.TryRead(L, 1, out long first));
		Assert.Equal(0, first);
		Assert.False(Int64Marshaller.TryRead(L, 2, out _));
		Assert.False(Int32Marshaller.TryRead(L, 2, out _));
	}

	[Theory]
	[Trait("Qualification", "Q21")]
	[InlineData("return 2147483647", true, int.MaxValue)]
	[InlineData("return -2147483648", true, int.MinValue)]
	[InlineData("return 2147483648", false, 0)]
	[InlineData("return 4294967295", false, 0)]
	[InlineData("return 7.0", true, 7)]
	[InlineData("return 2^31", false, 0)]
	[InlineData("return 2^53", false, 0)]
	[InlineData("return 1.5", false, 0)]
	[InlineData("return '123'", true, 123)]
	[InlineData("return ' -0x10 '", true, -16)]
	[InlineData("return '2147483648'", false, 0)]
	[InlineData("return '12.0'", false, 0)]
	public void Int32_follows_the_int64_conversion_policy_and_its_range(string source, bool accepted, int expected)
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		LuaTest.Run(L, Encoding.UTF8.GetBytes(source), 1);

		Assert.Equal(accepted, Int32Marshaller.TryRead(L, 1, out int read));
		Assert.Equal(expected, read);
		Assert.Equal(1, L.Top);
	}

	[Theory]
	[Trait("Qualification", "Q21")]
	[InlineData("return 2^53")]
	[InlineData("return -2^53")]
	[InlineData("return 2^53 + 2")]
	[InlineData("return 0x7FF6A0001000 * 2^10")]
	[InlineData("return 2^63")]
	[InlineData("return 2^64")]
	[InlineData("return 1e19")]
	public void Address_refuses_a_float_at_or_above_2_pow_53(string source)
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		LuaTest.Run(L, Encoding.UTF8.GetBytes(source), 1);

		Assert.False(AddressMarshaller.TryRead(L, 1, out UIntPtr read));
		Assert.Equal((nuint) 0, read);
		Assert.Equal(LuaType.Number, L.TypeOf(1));
		Assert.False(L.IsInteger(1));
	}

	[Theory]
	[Trait("Qualification", "Q21")]
	[InlineData("return 0x100000000", 0x1_0000_0000UL)]
	[InlineData("return 0x100000000 * 3 + 5", 0x3_0000_0005UL)]
	[InlineData("return 0x7FF6A0001000", 0x7FF6_A000_1000UL)]
	[InlineData("return 0x0020000000000001", 0x0020_0000_0000_0001UL)]
	[InlineData("return 0x7FFFFFFFFFFFFFFF", 0x7FFF_FFFF_FFFF_FFFFUL)]
	[InlineData("return 0x8000000000000000", 0x8000_0000_0000_0000UL)]
	[InlineData("return 0xFFFFFFFFFFFFF000", 0xFFFF_FFFF_FFFF_F000UL)]
	[InlineData("return -4096", 0xFFFF_FFFF_FFFF_F000UL)]
	public void Address_keeps_values_above_4_gib_and_above_long_MaxValue(string source, ulong expected)
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		LuaTest.Run(L, Encoding.UTF8.GetBytes(source), 1);

		Assert.True(L.IsInteger(1));
		Assert.True(AddressMarshaller.TryRead(L, 1, out UIntPtr read));
		Assert.Equal((nuint) expected, read);

		// Pushed back, the address is the same lua_Integer bits Lua produced.
		AddressMarshaller.Push(L, read);
		Assert.True(L.RawEquals(1, 2));
	}

	[Fact]
	public void Integer_conversion_of_floats_and_numerals_allocates_nothing()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		L.PushNumber(4096.0);
		L.PushString(" 0x10 "u8);
		L.PushNumber(9007199254740992.0);
		L.PushString("1e3"u8);
		long sink = 0;

		AllocationGate.AssertZero(() => sink += ReadConvertedValues(L));

		Assert.NotEqual(0, sink);
		Assert.Equal(4096 + 16, ReadConvertedValues(L));
		Assert.Equal(4, L.Top);
	}

	[Fact]
	public void Absent_values_read_as_failures_not_as_defaults()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);

		Assert.False(Int32Marshaller.TryRead(L, 1, out _));
		Assert.False(Int64Marshaller.TryRead(L, 1, out _));
		Assert.False(DoubleMarshaller.TryRead(L, 1, out _));
		Assert.False(AddressMarshaller.TryRead(L, 1, out _));
		Assert.False(BooleanMarshaller.TryRead(L, 1, out _));
		Assert.False(Utf8Marshaller.TryRead(L, 1, out ReadOnlySpan<byte> utf8));
		Assert.True(utf8.IsEmpty);
		Assert.False(StringMarshaller.TryRead(L, 1, out string? text));
		Assert.Null(text);
	}

	[Fact]
	public void Generic_code_over_a_marshaller_is_the_same_call()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);

		PushThrough<int, Int32Marshaller>(L, 1234);
		PushThrough<nuint, AddressMarshaller>(L, unchecked((nuint) 0xFFFF_FFFF_0000_0001UL));
		// The string marshaller's T is the type a declaration names, 'string': this instantiation must compile
		// without a nullability warning, and a failed read is 'maybe null when false'.
		PushThrough<string, StringMarshaller>(L, "text");
		PushThrough<ReadOnlySpan<byte>, Utf8Marshaller>(L, "bytes"u8);

		Assert.True(ReadThrough<int, Int32Marshaller>(L, 1, out int i));
		Assert.Equal(1234, i);
		Assert.True(ReadThrough<nuint, AddressMarshaller>(L, 2, out UIntPtr a));
		Assert.Equal(unchecked((nuint) 0xFFFF_FFFF_0000_0001UL), a);
		Assert.True(ReadThrough<string, StringMarshaller>(L, 3, out string? s));
		Assert.Equal("text", s);
		Assert.False(ReadThrough<string, StringMarshaller>(L, 1, out string? notAString));
		Assert.Null(notAString);
		Assert.True(ReadThrough<ReadOnlySpan<byte>, Utf8Marshaller>(L, 4, out ReadOnlySpan<byte> bytes));
		Assert.True(bytes.SequenceEqual("bytes"u8));
	}

	private static long ReadConvertedValues(LuaState L)
	{
		long total = 0;
		if (Int64Marshaller.TryRead(L, 1, out long integral))
		{
			total += integral;
		}

		if (Int32Marshaller.TryRead(L, 2, out int numeral))
		{
			total += numeral;
		}

		// Both refused: a float at 2^53 and a float numeral.
		if (AddressMarshaller.TryRead(L, 3, out UIntPtr refused) || Int64Marshaller.TryRead(L, 4, out _))
		{
			total += (long) refused;
		}

		return total;
	}

	private static void PushThrough<T, TMarshaller>(LuaState L, T value)
		where T : allows ref struct
		where TMarshaller : ILuaMarshaller<T>
	{
		TMarshaller.Push(L, value);
	}

	private static bool ReadThrough<T, TMarshaller>(LuaState L, int index, [MaybeNullWhen(false)] out T value)
		where T : allows ref struct
		where TMarshaller : ILuaMarshaller<T>
	{
		return TMarshaller.TryRead(L, index, out value);
	}
}
