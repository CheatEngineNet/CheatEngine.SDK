using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CheatEngine.SDK.Analyzers.Tests.Infrastructure;

/// <summary>
///     The compiler-visible setting contributed by the direct <c>CheatEngine.SDK</c> package build asset. Unit tests
///     that execute Roslyn directly use it instead of relying on the generator's absent-property behavior.
/// </summary>
internal sealed class DirectPackageAnalyzerConfigOptions : AnalyzerConfigOptionsProvider
{
	/// <summary>The direct-package configuration: bootstrap generation is explicitly enabled.</summary>
	public static readonly DirectPackageAnalyzerConfigOptions Enabled = new();

	private static readonly AnalyzerConfigOptions Empty = new TestOptions(ImmutableDictionary<string, string>.Empty);

	private DirectPackageAnalyzerConfigOptions()
	{
		GlobalOptions = new TestOptions(
			ImmutableDictionary.Create<string, string>(AnalyzerConfigOptions.KeyComparer)
				.Add("build_property.CheatEngineSdkGenerateEntryPoint", "true"));
	}

	/// <inheritdoc />
	public override AnalyzerConfigOptions GlobalOptions
	{
		get;
	}

	/// <inheritdoc />
	public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
	{
		return Empty;
	}

	/// <inheritdoc />
	public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
	{
		return Empty;
	}

	private sealed class TestOptions(ImmutableDictionary<string, string> values) : AnalyzerConfigOptions
	{
		public override bool TryGetValue(string key, out string value)
		{
			return values.TryGetValue(key, out value!);
		}
	}
}
