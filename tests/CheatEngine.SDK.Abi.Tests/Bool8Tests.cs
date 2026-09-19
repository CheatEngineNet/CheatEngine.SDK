using CheatEngine.SDK.Abi.Tests.Support;

namespace CheatEngine.SDK.Abi.Tests;

public sealed class Bool8Tests
{
    [Fact]
    public void Size_is_one_byte_like_a_pascal_boolean()
    {
        Assert.Equal(1, Layout.SizeOf<Bool8>());
    }

    [Fact]
    public void True_and_False_have_the_canonical_raw_values()
    {
        Assert.Equal(1, Bool8.True.RawValue);
        Assert.Equal(0, Bool8.False.RawValue);
        Assert.Equal(0, default(Bool8).RawValue);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(0x80, true)]
    [InlineData(0xFF, true)]
    public void Truthiness_of_a_raw_value_is_non_zero(byte raw, bool expected)
    {
        Bool8 value = new(raw);

        Assert.Equal(expected, value.IsTrue);
        Assert.Equal(expected, value.ToBoolean());
        Assert.Equal(expected, (bool)value);
        Assert.Equal(expected, value ? true : false);
        Assert.Equal(!expected, !value);
        Assert.Equal(raw, value.RawValue);
    }

    [Fact]
    public void Conversion_from_bool_writes_one_or_zero()
    {
        Bool8 fromTrue = true;
        Bool8 fromFalse = false;

        Assert.Equal(1, fromTrue.RawValue);
        Assert.Equal(0, fromFalse.RawValue);
        Assert.Equal(1, Bool8.FromBoolean(true).RawValue);
        Assert.Equal(0, Bool8.FromBoolean(false).RawValue);
    }

    [Fact]
    public void Equality_compares_truthiness_not_raw_bits()
    {
        Bool8 one = new(1);
        Bool8 allBitsSet = new(0xFF);

        Assert.True(one == allBitsSet);
        Assert.False(one != allBitsSet);
        Assert.True(one.Equals(allBitsSet));
        Assert.True(one.Equals((object)allBitsSet));
        Assert.Equal(one.GetHashCode(), allBitsSet.GetHashCode());
        Assert.True(one != Bool8.False);
        Assert.False(one.Equals((byte)1));
    }

    [Fact]
    public void ToString_returns_the_truthiness()
    {
        Assert.Equal("True", new Bool8(0xFF).ToString());
        Assert.Equal("False", Bool8.False.ToString());
    }
}
