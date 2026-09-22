using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace CheatEngine.SDK.SourceGenerators.EngineApi.Tests.Infrastructure;

/// <summary>
///     A hand-built <see cref="AdditionalText" /> over an in-memory spec file, for feeding
///     <see cref="EngineApiGenerator" /> without touching disk.
/// </summary>
internal sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
{
	private readonly SourceText _text = SourceText.From(text, Encoding.UTF8);

	/// <inheritdoc />
	public override string Path
	{
		get;
	} = path;

	/// <inheritdoc />
	public override SourceText GetText(CancellationToken cancellationToken = default)
	{
		return _text;
	}
}
