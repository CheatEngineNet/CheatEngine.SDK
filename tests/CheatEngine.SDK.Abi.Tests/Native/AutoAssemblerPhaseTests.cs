using CheatEngine.SDK.Abi.Native;
using CheatEngine.SDK.Abi.Tests.Support;

namespace CheatEngine.SDK.Abi.Tests.Native;

public sealed class AutoAssemblerPhaseTests
{
	[Theory]
	[InlineData(AutoAssemblerPhase.Initialize, 0)]
	[InlineData(AutoAssemblerPhase.Phase1, 1)]
	[InlineData(AutoAssemblerPhase.Phase2, 2)]
	[InlineData(AutoAssemblerPhase.Finalize, 3)]
	public void Member_has_the_upstream_numeric_value(AutoAssemblerPhase member, int expected)
	{
		Assert.Equal(expected, (int) member);
	}

	[Fact]
	public void Enum_has_exactly_the_four_upstream_members()
	{
		Assert.Equal(4, Enum.GetValues<AutoAssemblerPhase>().Length);
	}

	[Fact]
	public void Enum_is_four_bytes_wide_like_the_c_enumeration()
	{
		Assert.Equal(typeof(int), Enum.GetUnderlyingType(typeof(AutoAssemblerPhase)));
		Assert.Equal(4, Layout.SizeOf<AutoAssemblerPhase>());
	}
}
