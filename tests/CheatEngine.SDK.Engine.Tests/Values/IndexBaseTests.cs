using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.SDK.Engine.Tests.Values;

/// <summary>The index-base conversions without any Lua.</summary>
public sealed class IndexBaseTests
{
	[Fact]
	public void The_two_bases_are_zero_and_one()
	{
		Assert.Equal(0, IndexBase.FirstObjectIndex);
		Assert.Equal(1L, IndexBase.FirstLuaKey);
	}

	[Theory]
	[InlineData(0, 1L)]
	[InlineData(1, 2L)]
	[InlineData(41, 42L)]
	[InlineData(int.MaxValue, int.MaxValue + 1L)]
	public void ToLuaKey_adds_one_without_overflowing(int zeroBased, long expectedKey)
	{
		Assert.Equal(expectedKey, IndexBase.ToLuaKey(zeroBased));
		Assert.Equal(zeroBased, IndexBase.FromLuaKey(expectedKey));
		Assert.True(IndexBase.TryFromLuaKey(expectedKey, out int back));
		Assert.Equal(zeroBased, back);
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(int.MinValue)]
	public void ToLuaKey_rejects_a_negative_index(int zeroBased)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => IndexBase.ToLuaKey(zeroBased));
	}

	[Theory]
	[InlineData(0L)]
	[InlineData(-1L)]
	[InlineData(long.MinValue)]
	[InlineData(int.MaxValue + 2L)]
	[InlineData(long.MaxValue)]
	public void FromLuaKey_rejects_keys_outside_the_sequence_range(long key)
	{
		Assert.False(IndexBase.TryFromLuaKey(key, out int zeroBased));
		Assert.Equal(0, zeroBased);
		Assert.Throws<ArgumentOutOfRangeException>(() => IndexBase.FromLuaKey(key));
	}
}
