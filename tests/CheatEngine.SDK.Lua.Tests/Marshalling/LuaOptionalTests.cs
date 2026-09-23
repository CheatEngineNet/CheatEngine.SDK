using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.Tests.Support;

namespace CheatEngine.SDK.Lua.Tests.Marshalling;

/// <summary>
///     The explicit optional model (audit A19-07, AX06-22, A06-08): omitted, <c>nil</c> and a value are three distinct
///     states, <see langword="default" /> is omitted, and nothing is inferred from a null reference. Managed only.
/// </summary>
public sealed class LuaOptionalTests
{
	[Fact]
	public void Default_value_is_omitted_and_not_nil()
	{
		LuaOptional<int> value = default;

		Assert.True(value.IsOmitted);
		Assert.False(value.IsNil);
		Assert.False(value.HasValue);
		Assert.Equal(LuaOptional.Omitted<int>(), value);
		Assert.NotEqual(LuaOptional.Nil<int>(), value);
		Assert.Equal("<omitted>", value.ToString());
	}

	[Fact]
	public void Nil_is_distinct_from_omitted_and_from_every_value()
	{
		LuaOptional<int> nil = LuaOptional.Nil<int>();

		Assert.True(nil.IsNil);
		Assert.False(nil.IsOmitted);
		Assert.False(nil.HasValue);
		Assert.NotEqual(LuaOptional.Omitted<int>(), nil);
		Assert.NotEqual(LuaOptional.Of(0), nil);
		Assert.NotEqual(LuaOptional.Of(default(int)), nil);
		Assert.Equal("nil", nil.ToString());

		LuaOptional<string> nilText = LuaOptional.Nil<string>();
		Assert.NotEqual(LuaOptional.Of(string.Empty), nilText);
		Assert.False(nilText.TryGetValue(out string? text));
		Assert.Null(text);
	}

	[Fact]
	public void Of_keeps_the_value_and_rejects_a_null_reference()
	{
		LuaOptional<long> wide = LuaOptional.Of(long.MinValue);
		Assert.True(wide.HasValue);
		Assert.Equal(long.MinValue, wide.Value);
		Assert.True(wide.TryGetValue(out long read));
		Assert.Equal(long.MinValue, read);

		LuaOptional<double> number = LuaOptional.Of(2.5);
		Assert.Equal("2.5", number.ToString());

		LuaOptional<string> text = LuaOptional.Of("abc");
		Assert.Equal("abc", text.Value);
		Assert.Equal("abc", text.ToString());

		string? missing = null;
		ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => LuaOptional.Of(missing!));
		Assert.Equal("value", exception.ParamName);
	}

	[Fact]
	public void Value_throws_unless_a_value_is_present()
	{
		InvalidOperationException omitted =
			Assert.Throws<InvalidOperationException>(() => LuaOptional.Omitted<int>().Value);
		InvalidOperationException nil = Assert.Throws<InvalidOperationException>(() => LuaOptional.Nil<int>().Value);

		Assert.Contains("omitted", omitted.Message, StringComparison.Ordinal);
		Assert.Contains("nil", nil.Message, StringComparison.Ordinal);
		Assert.False(LuaOptional.Omitted<int>().TryGetValue(out int none));
		Assert.Equal(0, none);
	}

	[Fact]
	public void Equality_distinguishes_the_three_states()
	{
		LuaOptional<int>[] states =
			[LuaOptional.Omitted<int>(), LuaOptional.Nil<int>(), LuaOptional.Of(0), LuaOptional.Of(1)];

		for (int i = 0; i < states.Length; i++)
		{
			for (int j = 0; j < states.Length; j++)
			{
				Assert.Equal(i == j, states[i] == states[j]);
				Assert.Equal(i != j, states[i] != states[j]);
				Assert.Equal(i == j, states[i].Equals((object) states[j]));
			}
		}

		Assert.Equal(LuaOptional.Of(7), LuaOptional.Of(7));
		Assert.Equal(LuaOptional.Of(7).GetHashCode(), LuaOptional.Of(7).GetHashCode());
		Assert.Equal(LuaOptional.Nil<string>().GetHashCode(), LuaOptional.Nil<string>().GetHashCode());
		Assert.False(LuaOptional.Of(7).Equals(7));
		Assert.Equal(LuaOptional.Of("a"), LuaOptional.Of(new string('a', 1)));
	}

	[Fact]
	public void Factories_do_not_allocate_for_value_types()
	{
		long sink = 0;

		AllocationGate.AssertZero(() =>
		{
			LuaOptional<long> value = LuaOptional.Of(sink + 1);
			LuaOptional<long> nil = LuaOptional.Nil<long>();
			LuaOptional<long> omitted = LuaOptional.Omitted<long>();
			if (value == nil || nil == omitted || !value.TryGetValue(out long read))
			{
				throw new InvalidOperationException("states merged");
			}

			sink += read + (nil.IsNil ? 1 : 0) + (omitted.IsOmitted ? 1 : 0) + value.GetHashCode();
		});

		Assert.NotEqual(0, sink);
	}
}
