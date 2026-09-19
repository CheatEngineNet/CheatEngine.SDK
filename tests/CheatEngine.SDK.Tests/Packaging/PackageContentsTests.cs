using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Packaging;

/// <summary>
///     What is physically inside the packed <c>.nupkg</c>: the six embedded libs plus their XML docs under
///     <c>lib/net10.0</c>, and exactly the four shipping Roslyn components under <c>analyzers/dotnet/cs</c> - never
///     <c>CheatEngine.SDK.SourceGenerators.EngineApi</c>, which is repository-internal and does not ship.
/// </summary>
[Collection(PackagedUmbrellaSuite.Name)]
public sealed class PackageContentsTests(PackagedUmbrellaFixture fixture)
{
    private static readonly string[] ExpectedLibraries =
    [
        "CheatEngine.SDK.dll", "CheatEngine.SDK.Abi.dll", "CheatEngine.SDK.Annotations.dll",
        "CheatEngine.SDK.Engine.dll", "CheatEngine.SDK.Hosting.dll", "CheatEngine.SDK.Lua.dll",
        "CheatEngine.SDK.Lua.Interop.dll"
    ];

    private static readonly string[] ExpectedAnalyzers =
    [
        "CheatEngine.SDK.Analyzers.dll", "CheatEngine.SDK.Analyzers.CodeFixes.dll",
        "CheatEngine.SDK.SourceGenerators.EntryPoint.dll", "CheatEngine.SDK.SourceGenerators.LuaBindings.dll"
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
        // NuGet imports build/<PackageId>.props and buildTransitive/<PackageId>.props by package id alone: a props file
        // with any other name is silently never imported, so the expected names come from the id, not a second literal.
        Assert.Contains($"build/{UmbrellaPackage.Id}.props", fixture.PackageEntries, StringComparer.Ordinal);
        Assert.Contains($"buildTransitive/{UmbrellaPackage.Id}.props", fixture.PackageEntries, StringComparer.Ordinal);
    }

    [Fact]
    public void Package_carries_its_readme()
    {
        Assert.Contains("README.md", fixture.PackageEntries, StringComparer.Ordinal);
    }

    [Fact]
    public void Package_carries_the_win_x64_native_bridge()
    {
        Assert.Contains("runtimes/win-x64/native/cheatengine-sdk-lua-bridge.dll", fixture.PackageEntries,
            StringComparer.Ordinal);
    }
}
