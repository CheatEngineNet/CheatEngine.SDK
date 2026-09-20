using CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.Generator;

/// <summary>
///     Inputs for which the generator still emits although the emitted <c>new T()</c> cannot compile. They are the
///     remaining exceptions to "whatever is emitted compiles": staying silent would need a CESDK0001 case in
///     <c>CheatEngine.SDK.Analyzers</c> to explain the silence. These tests pin that behaviour; the cases that have an analyzer
///     rule (required members without <c>[SetsRequiredMembers]</c>, <c>[Obsolete(error: true)]</c>) are covered in
///     <see cref="NoOutputTests" /> instead.
/// </summary>
public sealed class KnownLimitationTests(RoslynFixture roslyn) : IClassFixture<RoslynFixture>
{
    private const string Usings = "using CheatEngine.SDK.Annotations.Plugin; using CheatEngine.SDK.Hosting.Plugin;\n";

    private const string Members = "protected override void OnEnable() { } protected override void OnDisable() { }";

    [Fact]
    public void Generator_sdk_referenced_only_through_an_extern_alias_fails_in_generated_code_with_CS0400()
    {
        // The generated file names the contract as global::CheatEngine.SDK.Hosting.*, which an aliased reference does not
        // feed. No namespace CheatEngine exists in the global alias of this plugin, hence CS0400.
        var references = roslyn.Environment.FrameworkReferences
            .Add(roslyn.Environment.AnnotationsReference.WithAliases(["sdk"]))
            .Add(roslyn.Environment.HostingReference.WithAliases(["sdk"]));
        var compilation = CSharpCompilation.Create(
            RoslynFixture.PluginAssemblyName,
            [
                RoslynFixture.Parse(
                    $"extern alias sdk; namespace N {{ [sdk::CheatEngine.SDK.Annotations.Plugin.CheatEnginePlugin(\"x\")] public sealed class P : sdk::CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin {{ {Members} }} }}",
                    "Source0.cs")
            ],
            references,
            RoslynEnvironment.CompilationOptions);

        var run = RoslynFixture.Run(compilation);

        AssertOnlyErrorIsInGeneratedFile(run, "CS0400");
    }

    [Fact]
    public void Generator_consumer_below_csharp_11_fails_in_generated_code_with_language_version_errors()
    {
        // C# 11 is the floor: 'file' types, u8 literals and static abstract interface members (see LanguageVersionTests
        // for the passing side). The compiler's own messages name the version to use.
        var csharp10 = RoslynEnvironment.ParseOptions.WithLanguageVersion(LanguageVersion.CSharp10);
        var compilation = RoslynFixture.CreateCompilation(roslyn.Environment, csharp10, PluginSources.Nominal);

        var run = RoslynFixture.Run(compilation, parseOptions: csharp10);

        AssertOnlyErrorIsInGeneratedFile(run, "CS8936", "CS8706");
    }

    // One generated file, no generator diagnostic, and every compiler error is one of the expected IDs, located in
    // the generated file (the author's own code stays clean), each expected ID occurring at least once.
    private static void AssertOnlyErrorIsInGeneratedFile(GeneratorRun run, params string[] expectedIds)
    {
        Assert.Null(run.Result.Exception);
        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Single(run.GeneratedSources);

        Diagnostic[] errors =
        [
            .. run.OutputCompilation
                .GetDiagnostics(TestContext.Current.CancellationToken)
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
        ];

        Assert.NotEmpty(errors);
        Assert.All(errors, error =>
        {
            Assert.Contains(error.Id, expectedIds, StringComparer.Ordinal);
            Assert.EndsWith(ExpectedBootstrap.HintName, error.Location.SourceTree?.FilePath ?? string.Empty,
                StringComparison.Ordinal);
        });
        Assert.All(expectedIds,
            id => Assert.Contains(errors, error => string.Equals(error.Id, id, StringComparison.Ordinal)));
    }
}
