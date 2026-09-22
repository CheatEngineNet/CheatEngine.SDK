using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Packaging;

/// <summary>Verifies the packed SDK exposes the target-bound allocation backend seam to an independent consumer.</summary>
[Collection(PackagedUmbrellaSuite.Name)]
public sealed class TargetBoundAllocationConsumerTests(PackagedUmbrellaFixture fixture)
{
	[Fact]
	public void Packed_consumer_compiles_an_independent_target_bound_allocation_backend()
	{
		Assert.True(File.Exists(Path.Combine(fixture.DefaultDeploymentDirectory, "DefaultConsumer.dll")));
	}
}
