using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CESDK.SourceGenerators.EntryPoint.Tests.Infrastructure;

/// <summary>
///     In-memory stand-in for the options the compiler builds from the generated <c>.editorconfig</c>: only global
///     options (the <c>build_property.*</c> keys) carry values.
/// </summary>
internal sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
{
    public static readonly TestAnalyzerConfigOptionsProvider Empty = new(TestAnalyzerConfigOptions.Empty);

    private TestAnalyzerConfigOptionsProvider(TestAnalyzerConfigOptions globalOptions)
    {
        GlobalOptions = globalOptions;
    }

    /// <inheritdoc />
    public override AnalyzerConfigOptions GlobalOptions { get; }

    /// <summary>Global options with the single key <c>build_property.&lt;name&gt;</c>.</summary>
    public static TestAnalyzerConfigOptionsProvider WithBuildProperty(string name, string value)
    {
        return new TestAnalyzerConfigOptionsProvider(new TestAnalyzerConfigOptions(
            ImmutableDictionary.Create<string, string>(AnalyzerConfigOptions.KeyComparer)
                .Add("build_property." + name, value)));
    }

    /// <inheritdoc />
    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
    {
        return TestAnalyzerConfigOptions.Empty;
    }

    /// <inheritdoc />
    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
    {
        return TestAnalyzerConfigOptions.Empty;
    }
}
