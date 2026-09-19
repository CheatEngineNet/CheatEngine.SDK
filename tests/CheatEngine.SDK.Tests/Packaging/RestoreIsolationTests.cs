using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Packaging;

/// <summary>
///     Regression coverage for a NuGet caching gap: a consumer restore that used the machine-wide global-packages
///     folder (the default when <c>dotnet restore</c> is given no <c>--packages</c>) would silently reuse whatever
///     extraction of <c>cheatengine.sdk/&lt;version&gt;</c> already sat there from an earlier run - this fixture's own previous
///     run, a developer's manual restore, or another parallel build - instead of the content of the <c>.nupkg</c> this
///     fixture run just packed, because every pack in one session gets the same MinVer-derived
///     version and NuGet treats a given package id+version as immutable once extracted.
///     <see cref="PackagedUmbrellaFixture" />
///     closes that gap by pointing every consumer restore at a packages directory scoped to its own temp root
///     (<c>dotnet restore --packages</c>); this test is the check that would fail if that isolation were ever dropped.
/// </summary>
[Collection(PackagedUmbrellaSuite.Name)]
public sealed class RestoreIsolationTests(PackagedUmbrellaFixture fixture)
{
    [Fact]
    public void Fixture_exposes_a_packages_directory_under_its_own_temp_root()
    {
        Assert.True(
            Directory.Exists(fixture.PackagesDirectory),
            $"Expected an isolated packages directory at '{fixture.PackagesDirectory}'.");
        Assert.Contains("cheatengine-sdk-umbrella-tests-", fixture.PackagesDirectory, StringComparison.Ordinal);
    }

    [Fact]
    public void Consumer_restore_extracts_the_packed_version_into_the_isolated_packages_directory()
    {
        // Proves the restore actually used --packages (not merely that the directory exists): NuGet only creates
        // '<packagesDirectory>/cheatengine.sdk/<version>' (the package id, lower-cased) as a side effect of
        // extracting the package there. If a future change dropped '--packages "<packagesDirectory>"' from
        // ThrowawayConsumer.RestoreAsync, this directory would stay empty while restore quietly fell back to the
        // machine-wide global-packages folder instead.
        var extractedPackageDirectory =
            Path.Combine(fixture.PackagesDirectory, UmbrellaPackage.ExtractionFolderName, fixture.PackageVersion);
        Assert.True(
            Directory.Exists(extractedPackageDirectory),
            $"Expected the consumer restore to extract '{UmbrellaPackage.ExtractionFolderName}/{fixture.PackageVersion}' " +
            $"into the fixture's isolated packages directory ('{extractedPackageDirectory}'), not the machine-wide " +
            "global-packages folder.");
    }
}
