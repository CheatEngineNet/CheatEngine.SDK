using System.Diagnostics.CodeAnalysis;
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
        var L = LuaTest.View(state);

        Int32Marshaller.Push(L, value);

        Assert.True(L.IsInteger(-1));
        Assert.True(Int32Marshaller.TryRead(L, -1, out var read));
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
        var L = LuaTest.View(state);

        Int64Marshaller.Push(L, value);

        Assert.False(Int32Marshaller.TryRead(L, -1, out var read));
        Assert.Equal(0, read);
        Assert.True(Int64Marshaller.TryRead(L, -1, out var wide));
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
        var L = LuaTest.View(state);

        Int64Marshaller.Push(L, value);

        Assert.True(Int64Marshaller.TryRead(L, -1, out var read));
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
        var L = LuaTest.View(state);
        var address = (nuint)value;

        AddressMarshaller.Push(L, address);

        Assert.True(L.IsInteger(-1));
        Assert.True(AddressMarshaller.TryRead(L, -1, out var read));
        Assert.Equal(address, read);

        // The same bits seen from Lua: an address above long.MaxValue is a negative lua_Integer.
        Assert.True(L.TryReadInteger(-1, out var bits));
        Assert.Equal(unchecked((long)value), bits);
    }

    [Fact]
    public void Address_pushed_from_lua_as_a_negative_integer_reads_as_the_high_address()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = LuaTest.View(state);
        LuaTest.Run(L, "return -16"u8, 1);

        Assert.True(AddressMarshaller.TryRead(L, -1, out var read));
        Assert.Equal(unchecked((nuint)0xFFFF_FFFF_FFFF_FFF0UL), read);
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
        var L = LuaTest.View(state);
        StringMarshaller.Push(L, text);

        Assert.False(AddressMarshaller.TryRead(L, -1, out var read));
        Assert.Equal((nuint)0, read);
        Assert.Equal(LuaType.String, L.TypeOf(-1));
    }

    [Fact]
    public void Address_accepts_a_float_with_an_integral_value_like_lua_does()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = LuaTest.View(state);
        L.PushNumber(4096.0);
        L.PushNumber(4096.5);

        Assert.True(AddressMarshaller.TryRead(L, 1, out var integral));
        Assert.Equal((nuint)4096, integral);
        Assert.False(AddressMarshaller.TryRead(L, 2, out _));
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
        var L = LuaTest.View(state);

        DoubleMarshaller.Push(L, value);

        Assert.False(L.IsInteger(-1));
        Assert.True(DoubleMarshaller.TryRead(L, -1, out var read));
        Assert.Equal(value, read);
    }

    [Fact]
    public void Double_nan_round_trips_as_nan()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = LuaTest.View(state);

        DoubleMarshaller.Push(L, double.NaN);

        Assert.True(DoubleMarshaller.TryRead(L, -1, out var read));
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
        var L = LuaTest.View(state);

        SingleMarshaller.Push(L, value);

        Assert.True(SingleMarshaller.TryRead(L, -1, out var read));
        Assert.Equal(value, read);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Boolean_round_trips(bool value)
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = LuaTest.View(state);

        BooleanMarshaller.Push(L, value);

        Assert.Equal(LuaType.Boolean, L.TypeOf(-1));
        Assert.True(BooleanMarshaller.TryRead(L, -1, out var read));
        Assert.Equal(value, read);
    }

    [Fact]
    public void Boolean_is_strict_nil_and_numbers_are_not_booleans()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = LuaTest.View(state);
        L.PushNil();
        L.PushInteger(1);

        Assert.False(BooleanMarshaller.TryRead(L, 1, out var fromNil));
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
        var L = LuaTest.View(state);
        L.PushInteger(3);
        L.PushNumber(3.0);
        L.PushNumber(2.5);
        L.PushString("42"u8);
        L.PushString("2.5"u8);
        L.PushBoolean(true);

        // 3: an integer; both marshallers accept it.
        Assert.True(L.IsInteger(1));
        Assert.True(Int64Marshaller.TryRead(L, 1, out var i1));
        Assert.Equal(3, i1);
        Assert.True(DoubleMarshaller.TryRead(L, 1, out var d1));
        Assert.Equal(3.0, d1);

        // 3.0: a float with an integral value; Lua's conversion accepts it as an integer, IsInteger tells the difference.
        Assert.False(L.IsInteger(2));
        Assert.True(Int64Marshaller.TryRead(L, 2, out var i2));
        Assert.Equal(3, i2);

        // 2.5: a float that has no integer representation.
        Assert.False(Int64Marshaller.TryRead(L, 3, out _));
        Assert.False(Int32Marshaller.TryRead(L, 3, out _));
        Assert.True(DoubleMarshaller.TryRead(L, 3, out var d3));
        Assert.Equal(2.5, d3);

        // Strings follow Lua's own coercion; the string marshallers stay strict the other way round (see string tests).
        Assert.True(Int64Marshaller.TryRead(L, 4, out var i4));
        Assert.Equal(42, i4);
        Assert.False(Int64Marshaller.TryRead(L, 5, out _));
        Assert.True(DoubleMarshaller.TryRead(L, 5, out var d5));
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

    [Fact]
    public void Absent_values_read_as_failures_not_as_defaults()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = LuaTest.View(state);

        Assert.False(Int32Marshaller.TryRead(L, 1, out _));
        Assert.False(Int64Marshaller.TryRead(L, 1, out _));
        Assert.False(DoubleMarshaller.TryRead(L, 1, out _));
        Assert.False(AddressMarshaller.TryRead(L, 1, out _));
        Assert.False(BooleanMarshaller.TryRead(L, 1, out _));
        Assert.False(Utf8Marshaller.TryRead(L, 1, out var utf8));
        Assert.True(utf8.IsEmpty);
        Assert.False(StringMarshaller.TryRead(L, 1, out var text));
        Assert.Null(text);
    }

    [Fact]
    public void Generic_code_over_a_marshaller_is_the_same_call()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = LuaTest.View(state);

        PushThrough<int, Int32Marshaller>(L, 1234);
        PushThrough<nuint, AddressMarshaller>(L, unchecked((nuint)0xFFFF_FFFF_0000_0001UL));
        // The string marshaller's T is the type a declaration names, 'string': this instantiation must compile
        // without a nullability warning, and a failed read is 'maybe null when false'.
        PushThrough<string, StringMarshaller>(L, "text");
        PushThrough<ReadOnlySpan<byte>, Utf8Marshaller>(L, "bytes"u8);

        Assert.True(ReadThrough<int, Int32Marshaller>(L, 1, out var i));
        Assert.Equal(1234, i);
        Assert.True(ReadThrough<nuint, AddressMarshaller>(L, 2, out var a));
        Assert.Equal(unchecked((nuint)0xFFFF_FFFF_0000_0001UL), a);
        Assert.True(ReadThrough<string, StringMarshaller>(L, 3, out var s));
        Assert.Equal("text", s);
        Assert.False(ReadThrough<string, StringMarshaller>(L, 1, out var notAString));
        Assert.Null(notAString);
        Assert.True(ReadThrough<ReadOnlySpan<byte>, Utf8Marshaller>(L, 4, out var bytes));
        Assert.True(bytes.SequenceEqual("bytes"u8));
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
