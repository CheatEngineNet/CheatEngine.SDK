using System.Collections.Immutable;
using CheatEngine.SDK.Annotations.Plugin;
using CheatEngine.SDK.Hosting.Plugin;
using CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.Generator;

/// <summary>
///     The "does the whole chain actually fit together" proof. Every other test in this project compiles the generated
///     bootstrap against <c>ContractStubs</c> (a hand-written mirror of the plugin contract, kept so the generator's
///     own test suite depends on nothing but that contract); this one compiles it against the REAL
///     <c>CheatEngine.SDK.Annotations</c> and <c>CheatEngine.SDK.Hosting</c> assemblies instead, so a real drift between
///     the contract and
///     its stub mirror would show up here even if <c>ContractStubs</c> had not been updated to match.
/// </summary>
public sealed class RealAssemblyCompilationTests
{
    private const string PluginSource = """
                                        using CheatEngine.SDK.Annotations.Plugin;
                                        using CheatEngine.SDK.Hosting.Plugin;

                                        namespace Demo;

                                        [CheatEnginePlugin("Real Assembly Demo")]
                                        public sealed class DemoPlugin : CheatEnginePlugin
                                        {
                                            protected override void OnEnable() { }

                                            protected override void OnDisable() { }
                                        }
                                        """;

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp14, DocumentationMode.Diagnose);

    // Same strictness as RoslynEnvironment.CompilationOptions: unsafe OFF (the entry point must compile without
    // it), nullable on, every warning wave.
    private static readonly CSharpCompilationOptions CompilationOptions = new(
        OutputKind.DynamicallyLinkedLibrary,
        nullableContextOptions: NullableContextOptions.Enable,
        allowUnsafe: false,
        warningLevel: 9999);

    [Fact]
    public void
        Generated_bootstrap_compiles_clean_against_the_real_CheatEngine_SDK_Hosting_and_CheatEngine_SDK_Annotations()
    {
        ImmutableArray<MetadataReference> references =
        [
            .. LocalFrameworkReferences.Load(),
            MetadataReference.CreateFromFile(typeof(CheatEnginePluginAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(CheatEnginePlugin).Assembly.Location)
        ];

        var compilation = CSharpCompilation.Create(
            "RealAssemblyDemoPlugin",
            [
                CSharpSyntaxTree.ParseText(PluginSource, ParseOptions, "Plugin.cs",
                    cancellationToken: TestContext.Current.CancellationToken)
            ],
            references,
            CompilationOptions);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new EntryPointGenerator().AsSourceGenerator()],
            [],
            ParseOptions,
            TestAnalyzerConfigOptionsProvider.WithBuildProperty("CheatEngineSdkGenerateEntryPoint", "true"),
            new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics,
            TestContext.Current.CancellationToken);

        Assert.Empty(generatorDiagnostics);
        Assert.Single(driver.GetRunResult().Results.Single().GeneratedSources);

        Diagnostic[] problems =
        [
            .. outputCompilation
                .GetDiagnostics(TestContext.Current.CancellationToken)
                .Where(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning &&
                                            !IsMissingDocumentationInTestInput(diagnostic))
        ];

        Assert.True(
            problems.Length == 0,
            "Unexpected compiler diagnostics against the real CheatEngine.SDK.Hosting/CheatEngine.SDK.Annotations:\n" +
            string.Join('\n', problems.AsEnumerable()));
    }

    // Same carve-out as GeneratorRun.AssertCompilesClean: the test plugin is public and undocumented on purpose
    // (a short source), which is CS1591 outside the generated file only.
    private static bool IsMissingDocumentationInTestInput(Diagnostic diagnostic)
    {
        return string.Equals(diagnostic.Id, "CS1591", StringComparison.Ordinal)
               && diagnostic.Location.SourceTree is { FilePath: string path }
               && !path.EndsWith(".g.cs", StringComparison.Ordinal);
    }
}
