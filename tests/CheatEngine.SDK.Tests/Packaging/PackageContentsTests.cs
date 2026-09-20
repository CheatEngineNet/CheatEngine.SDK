using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Packaging;

/// <summary>
///     What is physically inside the packed <c>.nupkg</c>: the six embedded libs plus their XML docs under
///     <c>lib/net10.0</c>, the four active shipping Roslyn components and their shared loader dependency under
///     <c>analyzers/dotnet/cs</c> - never <c>CheatEngine.SDK.SourceGenerators.EngineApi</c>, which is
///     repository-internal and does not ship.
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
        "CheatEngine.SDK.SourceGenerators.EntryPoint.dll", "CheatEngine.SDK.SourceGenerators.LuaBindings.dll",
        "CheatEngine.SDK.SourceGenerators.Shared.dll"
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
    public void Analyzers_directory_holds_the_active_components_and_their_shared_dependency()
    {
        string[] underAnalyzers =
        [
            .. fixture.PackageEntries.Where(static e => e.StartsWith("analyzers/dotnet/cs/", StringComparison.Ordinal))
        ];
        Assert.Equivalent(ExpectedAnalyzers.Select(static name => $"analyzers/dotnet/cs/{name}"), underAnalyzers);
    }

    [Fact]
    public void Package_carries_direct_consumer_build_assets_only()
    {
        // NuGet imports build/<PackageId>.* by package id alone. Omission of buildTransitive is intentional: an indirect
        // dependency must not activate the generator, alter compiler properties, or copy deployment files.
        Assert.Contains($"build/{UmbrellaPackage.Id}.props", fixture.PackageEntries, StringComparer.Ordinal);
        Assert.Contains($"build/{UmbrellaPackage.Id}.targets", fixture.PackageEntries, StringComparer.Ordinal);
        Assert.Contains("build/native/cheatengine-sdk-lua-bridge.dll", fixture.PackageEntries, StringComparer.Ordinal);
        Assert.DoesNotContain(fixture.PackageEntries,
            static entry => entry.StartsWith("buildTransitive/", StringComparison.Ordinal));
        Assert.DoesNotContain("runtimes/win-x64/native/cheatengine-sdk-lua-bridge.dll", fixture.PackageEntries,
            StringComparer.Ordinal);
    }

    [Fact]
    public void Package_carries_its_readme()
    {
        Assert.Contains("README.md", fixture.PackageEntries, StringComparer.Ordinal);
    }
}
