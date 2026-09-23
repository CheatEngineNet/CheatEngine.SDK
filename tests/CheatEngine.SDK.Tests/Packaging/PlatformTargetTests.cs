using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Packaging;

/// <summary>
///     The direct package target accepts a managed plugin that can load in Cheat Engine's x64 process, and rejects
///     every explicit architecture that cannot load the packaged Windows x64 bridge. It does not infer an
///     architecture for the consumer and an indirect package reference never imports this target.
/// </summary>
[Collection(PackagedUmbrellaSuite.Name)]
[Trait("Category", UmbrellaPackage.PackagingCategory)]
public sealed class PlatformTargetTests(PackagedUmbrellaFixture fixture)
{
	public static TheoryData<string, string> SupportedPlatformTargets =>
	[
		("Unset", ""),
		("AnyCPU", "AnyCPU"),
		("x64", "x64")
	];

	public static TheoryData<string, string> UnsupportedPlatformTargets =>
	[
		("x86", "x86"),
		("ARM", "ARM"),
		("ARM64", "ARM64"),
		("Itanium", "Itanium"),
		("Unsupported", "Unsupported")
	];

	[Theory]
	[MemberData(nameof(SupportedPlatformTargets))]
	public void Direct_supported_platform_target_is_accepted_by_the_packaged_target(string platformTarget,
		string expectedEffectiveValue)
	{
		Assert.Equal(expectedEffectiveValue, fixture.PlatformTargetConsumerEffectiveValues[platformTarget]);
		Assert.True(fixture.PlatformTargetConsumerBuildSucceeded[platformTarget],
			$"A direct CheatEngine.SDK package consumer with PlatformTarget={platformTarget} failed unexpectedly:{Environment.NewLine}" +
			fixture.PlatformTargetConsumerBuildOutput[platformTarget]);
	}

	[Theory]
	[MemberData(nameof(UnsupportedPlatformTargets))]
	public void Direct_unsupported_platform_target_is_rejected_by_the_packaged_target(string platformTarget,
		string expectedEffectiveValue)
	{
		Assert.Equal(expectedEffectiveValue, fixture.PlatformTargetConsumerEffectiveValues[platformTarget]);
		Assert.False(fixture.PlatformTargetConsumerBuildSucceeded[platformTarget],
			$"A direct CheatEngine.SDK package consumer with PlatformTarget={platformTarget} unexpectedly built successfully.");
		Assert.Contains("CESDK9101", fixture.PlatformTargetConsumerBuildOutput[platformTarget],
			StringComparison.Ordinal);
	}
}
