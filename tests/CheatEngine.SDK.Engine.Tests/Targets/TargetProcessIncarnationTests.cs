using CheatEngine.SDK.Engine.Targets;

namespace CheatEngine.SDK.Engine.Tests.Targets;

/// <summary>Value-contract tests for copied target process incarnations.</summary>
public sealed class TargetProcessIncarnationTests
{
	[Theory]
	[InlineData(0, 1, "processId")]
	[InlineData(-1, 1, "processId")]
	[InlineData(1, 0, "startedAtUtcTicks")]
	[InlineData(1, -1, "startedAtUtcTicks")]
	public void Constructor_with_an_invalid_identity_component_rejects_the_component(int processId,
		long startedAtUtcTicks,
		string parameterName)
	{
		ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
			new TargetProcessIncarnation(processId, startedAtUtcTicks));

		Assert.Equal(parameterName, exception.ParamName);
	}

	[Fact]
	public void Value_identity_uses_both_the_PID_and_observed_creation_time()
	{
		TargetProcessIncarnation original = new(4101, 1001);
		TargetProcessIncarnation sameIncarnation = new(4101, 1001);
		TargetProcessIncarnation differentTarget = new(4102, 1001);
		TargetProcessIncarnation reusedPid = new(4101, 2002);

		Assert.True(original.Equals(sameIncarnation));
		Assert.True(original.Equals((object) sameIncarnation));
		Assert.Equal(original.GetHashCode(), sameIncarnation.GetHashCode());
		Assert.True(original == sameIncarnation);
		Assert.False(original != sameIncarnation);

		Assert.False(original.Equals(differentTarget));
		Assert.False(original.Equals(reusedPid));
		Assert.False(original.Equals(null));
		Assert.True(original != differentTarget);
		Assert.True(original != reusedPid);
	}
}
