using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Packaging;

/// <summary>
///     The packed umbrella must not carry a <c>CheatEngine.SDK.*</c> nuspec dependency for any of the six embedded libs.
///     <c>PrivateAssets="all"</c> on every embedded <c>ProjectReference</c> (<c>src/CheatEngine.SDK/CheatEngine.SDK.csproj</c>) is what
///     keeps them out; this is the check that would fail if that metadata were ever dropped by accident.
/// </summary>
[Collection(PackagedUmbrellaSuite.Name)]
public sealed class NuspecDependencyTests(PackagedUmbrellaFixture fixture)
{
    [Fact]
    public void Packed_nuspec_declares_no_CheatEngine_SDK_dependency()
    {
        Assert.DoesNotContain(fixture.NuspecDependencyIds,
            id => id.StartsWith(UmbrellaPackage.Id, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Packed_nuspec_declares_no_dependency_at_all()
    {
        // Stronger than the check above: the six libs are the only thing that could ever appear here (the SDK
        // components take no runtime NuGet dependency of their own, see eng/RoslynComponent.props), so the nuspec has
        // an empty dependency group, not merely one without a CheatEngine.SDK.* entry.
        Assert.Empty(fixture.NuspecDependencyIds);
    }
}
