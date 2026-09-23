using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace CheatEngine.SDK.Analyzers.Tests.Infrastructure;

/// <summary>
///     Entry points for code-fix tests. The diagnostics to fix are marked up in the sources; diagnostics that must
///     remain after the fix are marked up in the fixed sources. When a provider offers several actions for one
///     diagnostic id, <c>equivalenceKey</c> names the one under test. Passing identical sources asserts that no fix is
///     offered.
/// </summary>
internal static class CodeFixVerifier<TAnalyzer, TCodeFix>
	where TAnalyzer : DiagnosticAnalyzer, new()
	where TCodeFix : CodeFixProvider, new()
{
	/// <summary>Verifies a fix inside a single file.</summary>
	public static Task VerifyAsync(string source, string fixedSource, string? equivalenceKey = null,
		int? fixAllIterations = null)
	{
		return VerifyAsync([("Test0.cs", source)], [("Test0.cs", fixedSource)], equivalenceKey, fixAllIterations);
	}

	/// <summary>Verifies a fix over several files; the fixed state lists every file, changed or not.</summary>
	/// <param name="sources">The files before the fix, with the diagnostics marked up.</param>
	/// <param name="fixedSources">The files after the fix, with the remaining diagnostics marked up.</param>
	/// <param name="equivalenceKey">The action to apply when the provider offers several.</param>
	/// <param name="fixAllIterations">
	///     Number of Fix All passes needed to reach the fixed state when the edits of one pass overlap; the library
	///     expects a single pass otherwise.
	/// </param>
	public static Task VerifyAsync(
		(string FileName, string Source)[] sources,
		(string FileName, string Source)[] fixedSources,
		string? equivalenceKey = null,
		int? fixAllIterations = null)
	{
		CheatEngineSdkCodeFixTest<TAnalyzer, TCodeFix> test = new()
		{
			CodeActionEquivalenceKey = equivalenceKey,
			NumberOfFixAllIterations = fixAllIterations
		};

		// The default drops fixable ids from the markup of the fixed state, assuming a fix always removes them all.
		// CESDK0001 stands for several problems, fixed one at a time: what remains must be stated and checked.
		test.FixedState.MarkupHandling = MarkupMode.Allow;

		foreach ((string fileName, string source) in sources)
		{
			test.TestState.Sources.Add((fileName, TestText.Normalize(source)));
		}

		foreach ((string fileName, string source) in fixedSources)
		{
			test.FixedState.Sources.Add((fileName, TestText.Normalize(source)));
		}

		return test.RunAsync(TestContext.Current.CancellationToken);
	}
}
