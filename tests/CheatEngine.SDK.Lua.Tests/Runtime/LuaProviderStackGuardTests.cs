using System.Globalization;

using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.SDK.Lua.Tests.Runtime;

/// <summary>Validates invalid stack observations without constructing an invalid native state.</summary>
public sealed class LuaProviderStackGuardTests
{
	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(int.MaxValue)]
	public void Provider_top_accepts_empty_and_nonempty_stacks(int top)
	{
		Assert.Null(Record.Exception(() => LuaRuntime.ValidateProviderStackTop(top)));
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(int.MinValue)]
	public void Provider_top_rejects_negative_observations(int top)
	{
		InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
			LuaRuntime.ValidateProviderStackTop(top));
		Assert.Equal("The host returned a Lua state with a negative stack top (" +
			top.ToString(CultureInfo.InvariantCulture) + ").", error.Message);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(int.MaxValue)]
	public void Stamp_check_accepts_unchanged_stack_top(int top)
	{
		Assert.Null(Record.Exception(() => LuaRuntime.ValidateVerifiedStackTop(top, top)));
	}

	[Theory]
	[InlineData(0, 1)]
	[InlineData(1, 0)]
	[InlineData(1, -1)]
	[InlineData(int.MaxValue, 0)]
	public void Stamp_check_rejects_growth_shrinkage_and_negative_observations(int before, int after)
	{
		InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
			LuaRuntime.ValidateVerifiedStackTop(before, after));
		Assert.Equal("The Lua stack changed during the host-state check (before " +
			before.ToString(CultureInfo.InvariantCulture) + ", after " +
			after.ToString(CultureInfo.InvariantCulture) + ").", error.Message);
	}

	[Fact]
	public void Rejection_diagnostics_use_invariant_numbers()
	{
		CultureInfo original = CultureInfo.CurrentCulture;
		CultureInfo custom = (CultureInfo) CultureInfo.InvariantCulture.Clone();
		custom.NumberFormat.NegativeSign = "~";
		try
		{
			CultureInfo.CurrentCulture = custom;
			InvalidOperationException provider = Assert.Throws<InvalidOperationException>(() =>
				LuaRuntime.ValidateProviderStackTop(-1));
			Assert.Equal("The host returned a Lua state with a negative stack top (-1).", provider.Message);
			InvalidOperationException stamp = Assert.Throws<InvalidOperationException>(() =>
				LuaRuntime.ValidateVerifiedStackTop(1, -1));
			Assert.Equal("The Lua stack changed during the host-state check (before 1, after -1).", stamp.Message);
		}
		finally
		{
			CultureInfo.CurrentCulture = original;
		}
	}
}
