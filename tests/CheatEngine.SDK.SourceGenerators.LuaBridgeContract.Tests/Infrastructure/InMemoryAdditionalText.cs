using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace CheatEngine.SDK.SourceGenerators.LuaBridgeContract.Tests.Infrastructure;

/// <summary>One compiler-provided additional text held entirely in test memory.</summary>
internal sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
{
    private readonly SourceText _text = SourceText.From(text, Encoding.UTF8);

    /// <inheritdoc />
    public override string Path { get; } = path;

    /// <inheritdoc />
    public override SourceText GetText(CancellationToken cancellationToken = default)
    {
        return _text;
    }
}
