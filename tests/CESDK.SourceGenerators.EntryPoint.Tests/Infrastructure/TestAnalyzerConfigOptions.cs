using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CESDK.SourceGenerators.EntryPoint.Tests.Infrastructure;

/// <summary>Dictionary-backed <see cref="AnalyzerConfigOptions" /> (keys compare like real editorconfig keys).</summary>
internal sealed class TestAnalyzerConfigOptions(ImmutableDictionary<string, string> values) : AnalyzerConfigOptions
{
    public static readonly TestAnalyzerConfigOptions Empty =
        new(ImmutableDictionary.Create<string, string>(KeyComparer));

    /// <inheritdoc />
    public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
    {
        return values.TryGetValue(key, out value);
    }
}
