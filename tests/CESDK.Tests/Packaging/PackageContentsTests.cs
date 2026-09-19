using CESDK.Tests.Infrastructure;

namespace CESDK.Tests.Packaging;

/// <summary>
///     What is physically inside the packed <c>.nupkg</c>: the six embedded libs plus their XML docs under
///     <c>lib/net10.0</c>, and exactly the four shipping Roslyn components under <c>analyzers/dotnet/cs</c> - never
///     <c>CESDK.SourceGenerators.EngineApi</c>, which is repository-internal and does not ship.
/// </summary>
[Collection(PackagedUmbrellaSuite.Name)]
public sealed class PackageContentsTests(PackagedUmbrellaFixture fixture)
{
    private static readonly string[] ExpectedLibraries =
    [
        "CESDK.dll", "CESDK.Abi.dll", "CESDK.Annotations.dll", "CESDK.Engine.dll", "CESDK.Hosting.dll", "CESDK.Lua.dll",
        "CESDK.Lua.Interop.dll"
    ];

    private static readonly string[] ExpectedAnalyzers =
    [
        "CESDK.Analyzers.dll", "CESDK.Analyzers.CodeFixes.dll", "CESDK.SourceGenerators.EntryPoint.dll",
        "CESDK.SourceGenerators.LuaBindings.dll"
    ];

    public static TheoryData<string> LibraryNames => [.. ExpectedLibraries];

    public static TheoryData<string> AnalyzerNames => [.. ExpectedAnalyzers];

    [Theory]
    [MemberData(nameof(LibraryNames))]
    public void Every_embedded_library_is_under_lib_net10_0_with_its_xml_docs(string libraryName)
    {
        Assert.Contains($"lib/net10.0/{libraryName}", fixture.PackageEntries, StringComparer.Ordinal);
        Assert.Contains($"lib/net10.0/{Path.GetFileNameWithoutExtension(libraryName)}.xml", fixture.PackageEntries,
            StringComparer.Ordinal);
    }

    [Theory]
    [MemberData(nameof(AnalyzerNames))]
    public void Every_shipping_component_is_under_analyzers_dotnet_cs(string analyzerName)
    {
        Assert.Contains($"analyzers/dotnet/cs/{analyzerName}", fixture.PackageEntries, StringComparer.Ordinal);
    }

    [Fact]
    public void EngineApi_generator_is_not_packed()
    {
        Assert.DoesNotContain(fixture.PackageEntries,
            entry => entry.Contains("EngineApi", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Analyzers_directory_holds_exactly_the_four_shipping_components()
    {
        string[] underAnalyzers =
        [
            .. fixture.PackageEntries.Where(static e => e.StartsWith("analyzers/dotnet/cs/", StringComparison.Ordinal))
        ];
        Assert.Equivalent(ExpectedAnalyzers.Select(static name => $"analyzers/dotnet/cs/{name}"), underAnalyzers);
    }

    [Fact]
    public void Package_carries_its_own_build_props_at_both_locations()
    {
        Assert.Contains("build/CESDK.props", fixture.PackageEntries, StringComparer.Ordinal);
        Assert.Contains("buildTransitive/CESDK.props", fixture.PackageEntries, StringComparer.Ordinal);
    }

    [Fact]
    public void Package_carries_its_readme()
    {
        Assert.Contains("README.md", fixture.PackageEntries, StringComparer.Ordinal);
    }

    [Fact]
    public void Package_carries_the_win_x64_native_bridge()
    {
        Assert.Contains("runtimes/win-x64/native/cesdk-lua-bridge.dll", fixture.PackageEntries,
            StringComparer.Ordinal);
    }
}
