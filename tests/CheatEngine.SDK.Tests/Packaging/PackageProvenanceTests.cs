using System.Security.Cryptography;

using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Packaging;

/// <summary>
///     Which file every other packaging fact is about. In the CI Release leg the fixture must consume the exact
///     <c>.nupkg</c> named by <see cref="UmbrellaPackage.PrebuiltPackageVariable" /> (the file uploaded as
///     <c>nuget-package</c>, attested and published) and never pack; only a local run outside CI may pack the working tree.
///     The identity of the consumed file is written to the test output, so it lands in the TRX next to the results.
/// </summary>
[Collection(PackagedUmbrellaSuite.Name)]
[Trait("Category", UmbrellaPackage.PackagingCategory)]
public sealed class PackageProvenanceTests(PackagedUmbrellaFixture fixture)
{
	[Fact]
	public void Fixture_consumes_the_ci_supplied_package_or_self_packs_only_outside_ci()
	{
		string? supplied = Environment.GetEnvironmentVariable(UmbrellaPackage.PrebuiltPackageVariable);
		string? continuousIntegration = Environment.GetEnvironmentVariable("CI");
		TestContext.Current.TestOutputHelper?.WriteLine(
			$"Consumed {Path.GetFileName(fixture.PackagePath)} sha256={fixture.PackageSha256} origin={fixture.PackageOrigin}");

		if (!string.IsNullOrWhiteSpace(supplied))
		{
			Assert.Equal(UmbrellaPackageOrigin.Prebuilt, fixture.PackageOrigin);
			Assert.Equal(supplied, fixture.SuppliedPackagePath);
			Assert.Equal(Path.GetFileName(supplied), Path.GetFileName(fixture.PackagePath));
			Assert.True(fixture.SelfPackAttempts == 0,
				$"The fixture ran 'dotnet pack' {fixture.SelfPackAttempts} time(s) although CI supplied '{supplied}'.");
		}
		else
		{
			Assert.Equal(UmbrellaPackageOrigin.SelfPacked, fixture.PackageOrigin);
			Assert.False(string.Equals(continuousIntegration?.Trim(), "true", StringComparison.OrdinalIgnoreCase),
				"A CI run reached the packaging facts without a supplied package; the fixture must have refused it.");
			Assert.Equal("", fixture.SuppliedPackagePath);
			Assert.InRange(fixture.SelfPackAttempts, 1, 5);
		}
	}

	[Fact]
	public void Feed_copy_is_byte_identical_to_the_supplied_package()
	{
		byte[] feedCopy = File.ReadAllBytes(fixture.PackagePath);
		Assert.Matches("^[0-9a-f]{64}$", fixture.PackageSha256);
		Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(feedCopy)), fixture.PackageSha256);
		Assert.Equal(Convert.ToBase64String(SHA512.HashData(feedCopy)), fixture.PackageSha512Base64);
		Assert.Equal(88, fixture.PackageSha512Base64.Length);

		if (fixture.PackageOrigin == UmbrellaPackageOrigin.Prebuilt)
		{
			byte[] supplied = File.ReadAllBytes(fixture.SuppliedPackagePath);
			Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(supplied)), fixture.PackageSha256);
			Assert.Equal(supplied.Length, feedCopy.Length);
		}
		else
		{
			List<string> umbrellas = [];
			foreach (string file in Directory.GetFiles(Path.GetDirectoryName(fixture.PackagePath)!, "*.nupkg"))
			{
				if (UmbrellaPackageSource.IsUmbrellaPackageFileName(Path.GetFileName(file)))
				{
					umbrellas.Add(file);
				}
			}

			Assert.Equal(fixture.PackagePath, Assert.Single(umbrellas));
		}
	}
}
