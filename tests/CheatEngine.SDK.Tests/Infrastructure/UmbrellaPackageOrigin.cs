namespace CheatEngine.SDK.Tests.Infrastructure;

/// <summary>Where the package under test of <see cref="PackagedUmbrellaFixture" /> comes from.</summary>
public enum UmbrellaPackageOrigin
{
	/// <summary>
	///     The fixture packed <c>src/CheatEngine.SDK</c> itself (local runs). The facts then describe the working tree,
	///     not a file that CI uploaded, attested or published.
	/// </summary>
	SelfPacked,

	/// <summary>
	///     The fixture copied the file named by <see cref="UmbrellaPackage.PrebuiltPackageVariable" /> and never packed: in
	///     the CI Release leg, the exact <c>.nupkg</c> that is uploaded as <c>nuget-package</c>, attested and published.
	/// </summary>
	Prebuilt
}
