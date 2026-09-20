using System.Collections.Immutable;
using CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.Generator;

/// <summary>
///     The generator's marker and base-class checks are assembly-symbol checks. A type with the same fully-qualified
///     metadata name in another referenced assembly must never become a plugin contract accidentally.
/// </summary>
public sealed class ContractIdentityTests(RoslynFixture roslyn) : IClassFixture<RoslynFixture>
{
    private const string LifecycleOverrides = "protected override void OnEnable() { } protected override void OnDisable() { }";

    [Fact]
    public void Generator_sdk_contract_symbols_from_the_expected_assemblies_emit_the_bootstrap()
    {
        var run = roslyn.Run(PluginSources.Nominal);

        Assert.Equal(ExpectedBootstrap.Text("global::Demo.DemoPlugin", "\"Demo Plugin\"u8"), run.SingleGeneratedText);
        run.AssertCompilesClean();
    }

    [Fact]
    public void Generator_file_local_entry_point_lookalike_does_not_block_the_bootstrap()
    {
        var run = roslyn.Run(
            PluginSources.Nominal,
            """
            namespace CESDK
            {
                file static class CESDK
                {
                }
            }
            """);

        Assert.Equal(ExpectedBootstrap.Text("global::Demo.DemoPlugin", "\"Demo Plugin\"u8"), run.SingleGeneratedText);
        run.AssertCompilesClean();
    }

    [Fact]
    public void Generator_referenced_entry_point_lookalike_does_not_block_the_bootstrap()
    {
        var foreignEntryPoint = CreateReference(
            roslyn.Environment,
            "Foreign.EntryPoint",
            """
            namespace CESDK
            {
                public static class CESDK
                {
                }
            }
            """);
        var compilation = CreateCompilation(
            roslyn.Environment,
            roslyn.Environment.PluginReferences.Add(foreignEntryPoint),
            PluginSources.Nominal);

        var run = RoslynFixture.Run(compilation);

        Assert.Equal(ExpectedBootstrap.Text("global::Demo.DemoPlugin", "\"Demo Plugin\"u8"), run.SingleGeneratedText);
        run.AssertCompilesClean();
    }

    [Fact]
    public void Generator_same_named_plugin_attribute_from_a_foreign_assembly_emits_nothing()
    {
        var foreignAttribute = CreateReference(
            roslyn.Environment,
            "Foreign.Plugin.Annotations",
            """
            namespace CheatEngine.SDK.Annotations.Plugin
            {
                [global::System.AttributeUsage(global::System.AttributeTargets.Class)]
                public sealed class CheatEnginePluginAttribute(string name) : global::System.Attribute
                {
                }
            }
            """);
        var compilation = CreateCompilation(
            roslyn.Environment,
            roslyn.Environment.PluginReferences.Add(foreignAttribute.WithAliases(["foreign"])),
            """
            extern alias foreign;
            using CheatEngine.SDK.Hosting.Plugin;

            [foreign::CheatEngine.SDK.Annotations.Plugin.CheatEnginePlugin("Foreign")]
            public sealed class P : CheatEnginePlugin
            {
                protected override void OnEnable() { }
                protected override void OnDisable() { }
            }
            """);

        var run = RoslynFixture.Run(compilation);

        run.AssertNoOutput();
        AssertNoSourceErrors(run);
    }

    [Fact]
    public void Generator_same_named_plugin_base_from_a_foreign_assembly_emits_nothing()
    {
        var foreignBase = CreateReference(
            roslyn.Environment,
            "Foreign.Plugin.Hosting",
            """
            namespace CheatEngine.SDK.Hosting.Plugin
            {
                public abstract class CheatEnginePlugin
                {
                    protected internal abstract void OnEnable();
                    protected internal abstract void OnDisable();
                }
            }
            """);
        var compilation = CreateCompilation(
            roslyn.Environment,
            roslyn.Environment.PluginReferences.Add(foreignBase.WithAliases(["foreign"])),
            $$"""
              extern alias foreign;
              using CheatEngine.SDK.Annotations.Plugin;

              [CheatEnginePlugin("Foreign")]
              public sealed class P : foreign::CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin
              {
                  {{LifecycleOverrides}}
              }
              """);

        var run = RoslynFixture.Run(compilation);

        run.AssertNoOutput();
        AssertNoSourceErrors(run);
    }

    private static CSharpCompilation CreateCompilation(
        RoslynEnvironment environment,
        ImmutableArray<MetadataReference> references,
        string source)
    {
        return CSharpCompilation.Create(
            RoslynFixture.PluginAssemblyName,
            [CSharpSyntaxTree.ParseText(source, RoslynEnvironment.ParseOptions, "Plugin.cs")],
            references,
            RoslynEnvironment.CompilationOptions);
    }

    private static PortableExecutableReference CreateReference(
        RoslynEnvironment environment,
        string assemblyName,
        string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, RoslynEnvironment.ParseOptions, assemblyName + ".cs")],
            environment.FrameworkReferences,
            RoslynEnvironment.CompilationOptions);
        using MemoryStream image = new();
        var result = compilation.Emit(image, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Success, "The foreign contract did not compile:\n" + string.Join('\n', result.Diagnostics));

        return MetadataReference.CreateFromImage([.. image.ToArray()], filePath: assemblyName + ".dll");
    }

    private static void AssertNoSourceErrors(GeneratorRun run)
    {
        Assert.DoesNotContain(
            run.OutputCompilation.GetDiagnostics(TestContext.Current.CancellationToken),
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
                                 && (diagnostic.Location.SourceTree is null
                                     || !diagnostic.Location.SourceTree.FilePath.EndsWith(".g.cs", StringComparison.Ordinal)));
    }
}
