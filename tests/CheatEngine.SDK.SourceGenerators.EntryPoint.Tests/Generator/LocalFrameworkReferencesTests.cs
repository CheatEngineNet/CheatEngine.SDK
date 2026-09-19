using CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.Infrastructure;
using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.Generator;

/// <summary>
///     The test compilations take <c>Microsoft.NETCore.App</c> from the local .NET installation, never from a NuGet
///     restore (no network in tests). Both sources must give the same verdict on the generated code, so that a machine
///     with only a runtime (fallback) and a machine with an SDK (targeting pack) run the same suite.
/// </summary>
public sealed class LocalFrameworkReferencesTests
{
    [Fact]
    public void Load_finds_the_framework_without_a_package_restore()
    {
        var references = LocalFrameworkReferences.Load();

        Assert.NotEmpty(references);
        Assert.All(references,
            static reference => Assert.True(File.Exists(reference.Display), $"Not a local file: {reference.Display}"));
        Assert.Contains(references, static reference => IsNamed(reference, "System.Runtime.dll"));
    }

    [Fact]
    public void FromRunningRuntime_holds_managed_framework_assemblies_only()
    {
        var references = LocalFrameworkReferences.FromRunningRuntime();

        Assert.Contains(references, static reference => IsNamed(reference, "System.Private.CoreLib.dll"));
        Assert.DoesNotContain(references, static reference => IsNamed(reference, "xunit.v3.core.dll"));
        Assert.DoesNotContain(references, static reference => IsNamed(reference, "Microsoft.CodeAnalysis.dll"));
    }

    [Fact]
    public void Generator_output_compiles_clean_against_the_running_runtime_fallback()
    {
        var environment = RoslynEnvironment.Create(LocalFrameworkReferences.FromRunningRuntime());
        var compilation =
            RoslynFixture.CreateCompilation(environment, RoslynEnvironment.ParseOptions, PluginSources.Nominal);

        var run = RoslynFixture.Run(compilation);

        Assert.Equal(ExpectedBootstrap.Text("global::Demo.DemoPlugin", "\"Demo Plugin\"u8"), run.SingleGeneratedText);
        run.AssertCompilesClean();

        using var bootstrap = LoadedBootstrap.Load(environment, run.OutputCompilation);
        Assert.Equal(1, bootstrap.Initialize(IntPtr.Zero, 0));
    }

    [Fact]
    public void Generator_output_compiles_clean_against_the_targeting_pack_when_one_is_installed()
    {
        var pack = LocalFrameworkReferences.FromTargetingPack();
        Assert.SkipWhen(pack.IsEmpty,
            "No Microsoft.NETCore.App.Ref 10.0.x targeting pack next to the running runtime (runtime-only installation).");

        var environment = RoslynEnvironment.Create(pack);
        var compilation =
            RoslynFixture.CreateCompilation(environment, RoslynEnvironment.ParseOptions, PluginSources.Nominal);

        var run = RoslynFixture.Run(compilation);

        Assert.All(pack,
            static reference =>
                Assert.Contains("Microsoft.NETCore.App.Ref", reference.Display, StringComparison.Ordinal));
        run.AssertCompilesClean();
    }

    private static bool IsNamed(MetadataReference reference, string fileName)
    {
        return string.Equals(Path.GetFileName(reference.Display), fileName, StringComparison.OrdinalIgnoreCase);
    }
}
