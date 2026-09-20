using System.Text;
using CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.Infrastructure;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;

namespace CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.Generator;

/// <summary>
///     The same generator under the standard Roslyn test harness (
///     <c>Microsoft.CodeAnalysis.CSharp.SourceGenerators.Testing</c>
///     with the framework-agnostic <see cref="DefaultVerifier" />). Two things only this harness exercises: the generator
///     is instantiated by type, like the compiler does, and the MSBuild switch arrives through a real global analyzer
///     config file instead of an in-memory options object.
/// </summary>
public sealed class DefaultVerifierTests
{
    // This harness compiles with documentation diagnostics on: public members need XML comments (CS1591), which
    // also proves that the generated file does not trip that rule.
    private const string DocumentedPlugin = """
                                            using CheatEngine.SDK.Annotations.Plugin;
                                            using CheatEngine.SDK.Hosting.Plugin;

                                            namespace Demo;

                                            /// <summary>Test plugin.</summary>
                                            [CheatEnginePlugin("Demo Plugin")]
                                            public sealed class DemoPlugin : CheatEnginePlugin
                                            {
                                                /// <inheritdoc/>
                                                protected override void OnEnable() { }

                                                /// <inheritdoc/>
                                                protected override void OnDisable() { }
                                            }
                                            """;

    [Fact]
    public async Task Verifier_single_valid_plugin_with_direct_package_setting_matches_expected_source_and_compiles()
    {
        var test = CreateTest();
        test.TestState.GeneratedSources.Add((
            typeof(EntryPointGenerator),
            ExpectedBootstrap.HintName,
            SourceText.From(ExpectedBootstrap.Text("global::Demo.DemoPlugin", "\"Demo Plugin\"u8"), Encoding.UTF8)));

        await test.RunAsync(TestContext.Current.CancellationToken);
        Assert.Single(test.TestState.GeneratedSources);
    }

    [Fact]
    public async Task Verifier_build_property_false_in_global_config_emits_nothing()
    {
        var test = CreateTest(false);
        test.TestState.AnalyzerConfigFiles.Add((
            "/.globalconfig",
            "is_global = true\nbuild_property.CheatEngineSdkGenerateEntryPoint = false\n"));

        // No entry in GeneratedSources: the verifier fails if the generator adds any file.
        await test.RunAsync(TestContext.Current.CancellationToken);
        Assert.Empty(test.TestState.GeneratedSources);
    }

    [Fact]
    public async Task Verifier_build_property_true_in_global_config_emits_the_bootstrap()
    {
        var test = CreateTest(false);
        test.TestState.AnalyzerConfigFiles.Add((
            "/.globalconfig",
            "is_global = true\nbuild_property.CheatEngineSdkGenerateEntryPoint = true\n"));
        test.TestState.GeneratedSources.Add((
            typeof(EntryPointGenerator),
            ExpectedBootstrap.HintName,
            SourceText.From(ExpectedBootstrap.Text("global::Demo.DemoPlugin", "\"Demo Plugin\"u8"), Encoding.UTF8)));

        await test.RunAsync(TestContext.Current.CancellationToken);
        Assert.Single(test.TestState.GeneratedSources);
    }

    private static CSharpSourceGeneratorTest<EntryPointGenerator, DefaultVerifier> CreateTest(
        bool applyDirectPackageSetting = true)
    {
        var environment = RoslynEnvironment.Shared;

        CSharpSourceGeneratorTest<EntryPointGenerator, DefaultVerifier> test = new()
        {
            // A framework moniker WITHOUT a reference-assembly package: the library then resolves nothing through
            // its NuGet client (ReferenceAssemblies.Net.Net100 would download Microsoft.NETCore.App.Ref on a cold
            // cache; tests must not need the network). The framework comes from the local installation instead.
            ReferenceAssemblies = new ReferenceAssemblies("net10.0"),

            // Warnings of the whole compilation (generated file included) fail the test, not only errors.
            CompilerDiagnostics = CompilerDiagnostics.Warnings
        };
        if (applyDirectPackageSetting)
            test.TestState.AnalyzerConfigFiles.Add((
                "/.globalconfig",
                "is_global = true\nbuild_property.CheatEngineSdkGenerateEntryPoint = true\n"));
        test.TestState.Sources.Add(DocumentedPlugin);
        test.TestState.AdditionalReferences.AddRange(environment.FrameworkReferences);
        test.TestState.AdditionalReferences.Add(environment.AnnotationsReference);
        test.TestState.AdditionalReferences.Add(environment.HostingReference);
        return test;
    }
}
