using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CheatEngine.SDK.SourceGenerators.EngineApi.Tests.Infrastructure;

/// <summary>
///     Class fixture of every test class that runs the generator: a direct <see cref="CSharpGeneratorDriver" /> harness
///     over the shared <see cref="RoslynEnvironment" />. It always tracks incremental steps, so that any run can be
///     inspected for caching.
/// </summary>
/// <remarks>
///     Unlike <c>CheatEngine.SDK.SourceGenerators.LuaBindings</c>'s fixture, the compilation carries no attributed C#
///     source: this
///     generator reads only <c>AdditionalTextsProvider</c>, so the compilation exists solely to host the generated
///     trees and their compile-clean check.
/// </remarks>
public sealed class RoslynFixture
{
    /// <summary>Assembly name of the test compilations.</summary>
    internal const string PluginAssemblyName = "TestEngineApi";

    /// <summary>
    ///     Takes the process-wide environment; a failure to find the framework references fails the class with the
    ///     resolver's message.
    /// </summary>
    public RoslynFixture()
    {
        Environment = RoslynEnvironment.Shared;
    }

    internal RoslynEnvironment Environment { get; }

    /// <summary>An otherwise-empty compilation (no attributed source is needed by this generator).</summary>
    internal CSharpCompilation CreateCompilation()
    {
        return CSharpCompilation.Create(PluginAssemblyName, [], Environment.PluginReferences,
            RoslynEnvironment.CompilationOptions);
    }

    /// <summary>Creates a driver for the generator, with step tracking on, over <paramref name="additionalTexts" />.</summary>
    internal static GeneratorDriver CreateDriver(params AdditionalText[] additionalTexts)
    {
        return CSharpGeneratorDriver.Create(
            [new EngineApiGenerator().AsSourceGenerator()],
            additionalTexts,
            RoslynEnvironment.ParseOptions,
            null,
            new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                true));
    }

    /// <summary>Runs the generator once over one spec file at <paramref name="path" /> with content <paramref name="text" />.</summary>
    internal GeneratorRun Run(string path, string text)
    {
        return Run((path, text));
    }

    /// <summary>Runs the generator once over several spec files.</summary>
    internal GeneratorRun Run(params (string Path, string Text)[] specs)
    {
        AdditionalText[] texts =
            [.. specs.Select(static spec => (AdditionalText)new InMemoryAdditionalText(spec.Path, spec.Text))];
        return GeneratorRun.Execute(CreateDriver(texts), CreateCompilation());
    }
}
