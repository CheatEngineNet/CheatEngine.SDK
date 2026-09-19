using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CESDK.SourceGenerators.EntryPoint.Tests.Infrastructure;

/// <summary>
///     Class fixture of every test class that runs the generator: a direct <see cref="CSharpGeneratorDriver" /> harness
///     over the shared <see cref="RoslynEnvironment" />. It always tracks incremental steps, so that any run can be
///     inspected for caching.
/// </summary>
public sealed class RoslynFixture
{
    /// <summary>Assembly name of the plugin compilations created here.</summary>
    internal const string PluginAssemblyName = "TestPlugin";

    /// <summary>
    ///     Takes the process-wide environment. Nothing to await and nothing to restore: the references come from the
    ///     local .NET installation. A failure to find them fails the tests of the class with the resolver's message.
    /// </summary>
    public RoslynFixture()
    {
        Environment = RoslynEnvironment.Shared;
    }

    internal RoslynEnvironment Environment { get; }

    /// <summary>A plugin compilation with one syntax tree per source, named <c>Source0.cs</c>, <c>Source1.cs</c>...</summary>
    internal CSharpCompilation CreateCompilation(params string[] sources)
    {
        return CreateCompilation(Environment, RoslynEnvironment.ParseOptions, sources);
    }

    /// <summary>Same, against another environment (framework references) or another language version.</summary>
    internal static CSharpCompilation CreateCompilation(RoslynEnvironment environment, CSharpParseOptions parseOptions,
        params string[] sources)
    {
        var trees = new SyntaxTree[sources.Length];
        for (var i = 0; i < sources.Length; i++)
            trees[i] = CSharpSyntaxTree.ParseText(sources[i], parseOptions, $"Source{i}.cs");

        return CSharpCompilation.Create(
            PluginAssemblyName,
            trees,
            environment.PluginReferences,
            RoslynEnvironment.CompilationOptions);
    }

    /// <summary>
    ///     Creates a driver for the generator; <paramref name="options" /> defaults to "no build property set",
    ///     <paramref name="parseOptions" /> (the language version of the generated tree) to the strict C# 14 options.
    /// </summary>
    internal static GeneratorDriver CreateDriver(
        TestAnalyzerConfigOptionsProvider? options = null,
        CSharpParseOptions? parseOptions = null)
    {
        return CSharpGeneratorDriver.Create(
            [new EntryPointGenerator().AsSourceGenerator()],
            [],
            parseOptions ?? RoslynEnvironment.ParseOptions,
            options ?? TestAnalyzerConfigOptionsProvider.Empty,
            new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                true));
    }

    /// <summary>Runs the generator once over <paramref name="sources" />.</summary>
    internal GeneratorRun Run(params string[] sources)
    {
        return Run(CreateCompilation(sources));
    }

    /// <summary>Runs the generator once over <paramref name="compilation" />.</summary>
    internal static GeneratorRun Run(
        Compilation compilation,
        TestAnalyzerConfigOptionsProvider? options = null,
        CSharpParseOptions? parseOptions = null)
    {
        return GeneratorRun.Execute(CreateDriver(options, parseOptions), compilation);
    }

    internal static SyntaxTree Parse(string source, string path)
    {
        return CSharpSyntaxTree.ParseText(source, RoslynEnvironment.ParseOptions, path);
    }
}
