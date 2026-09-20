using CheatEngine.SDK.Abi.Tests.Support;

namespace CheatEngine.SDK.Abi.Tests;

public sealed class Bool32Tests
{
    [Fact]
    public void Size_is_four_bytes_like_win32_BOOL()
    {
        Assert.Equal(4, Layout.SizeOf<Bool32>());
    }

    [Fact]
    public void True_and_False_have_the_canonical_raw_values()
    {
        Assert.Equal(1, Bool32.True.RawValue);
        Assert.Equal(0, Bool32.False.RawValue);
        Assert.Equal(0, default(Bool32).RawValue);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(-1, true)]
    [InlineData(2, true)]
    [InlineData(0x100, true)]
    [InlineData(int.MinValue, true)]
    public void Truthiness_of_a_raw_value_is_non_zero(int raw, bool expected)
    {
        Bool32 value = new(raw);

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
        Bool32 fromTrue = true;
        Bool32 fromFalse = false;

        Assert.Equal(1, fromTrue.RawValue);
        Assert.Equal(0, fromFalse.RawValue);
        Assert.Equal(1, Bool32.FromBoolean(true).RawValue);
        Assert.Equal(0, Bool32.FromBoolean(false).RawValue);
    }

    [Fact]
    public void Equality_compares_truthiness_not_raw_bits()
    {
        Bool32 one = new(1);
        Bool32 allBitsSet = new(-1);

        Assert.True(one == allBitsSet);
        Assert.False(one != allBitsSet);
        Assert.True(one.Equals(allBitsSet));
        Assert.True(one.Equals((object)allBitsSet));
        Assert.Equal(one.GetHashCode(), allBitsSet.GetHashCode());
        Assert.True(one != Bool32.False);
        Assert.False(one.Equals(1));
    }

    [Fact]
    public void Comparison_with_canonical_values_uses_truthiness()
    {
        Bool32 allBitsSet = new(-1);

        Assert.True(allBitsSet == Bool32.True);
        Assert.False(Bool32.False == Bool32.True);
    }

    [Fact]
    public void ToString_returns_the_truthiness()
    {
        Assert.Equal("True", new Bool32(-1).ToString());
        Assert.Equal("False", Bool32.False.ToString());
    }
}
