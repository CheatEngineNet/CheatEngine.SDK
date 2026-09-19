using CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.Infrastructure;
using Microsoft.CodeAnalysis.CSharp;

namespace CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.Generator;

/// <summary>
///     The generated file needs C# 11 (<c>file</c> types, <c>u8</c> literals, implementing static abstract interface
///     members) and nothing newer: a consumer that pins <c>LangVersion</c> anywhere from 11 to the <c>net10.0</c> default
///     (14) gets a file that compiles clean and runs. The failing side (C# 10) is in <see cref="KnownLimitationTests" />.
/// </summary>
public sealed class LanguageVersionTests(RoslynFixture roslyn) : IClassFixture<RoslynFixture>
{
    [Theory]
    [InlineData(LanguageVersion.CSharp11)]
    [InlineData(LanguageVersion.CSharp12)]
    [InlineData(LanguageVersion.CSharp13)]
    [InlineData(LanguageVersion.CSharp14)]
    public void Generator_output_compiles_clean_and_runs_from_csharp_11_up(LanguageVersion version)
    {
        var parseOptions = RoslynEnvironment.ParseOptions.WithLanguageVersion(version);
        var compilation = RoslynFixture.CreateCompilation(roslyn.Environment, parseOptions, PluginSources.Nominal);

        // The driver parses the generated tree with the consumer's language version, like the compiler does.
        var run = RoslynFixture.Run(compilation, parseOptions: parseOptions);

        Assert.Equal(ExpectedBootstrap.Text("global::Demo.DemoPlugin", "\"Demo Plugin\"u8"), run.SingleGeneratedText);
        run.AssertCompilesClean();

        using var bootstrap = LoadedBootstrap.Load(roslyn.Environment, run.OutputCompilation);
        Assert.Equal(1, bootstrap.Initialize(IntPtr.Zero, 0));
    }
}
