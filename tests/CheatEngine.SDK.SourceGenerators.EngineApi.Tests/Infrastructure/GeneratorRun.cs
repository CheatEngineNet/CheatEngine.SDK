using System.Collections.Immutable;

using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.EngineApi.Tests.Infrastructure;

/// <summary>One execution of the generator: the driver (to run again), the result and the updated compilation.</summary>
internal sealed class GeneratorRun
{
	private GeneratorRun(GeneratorDriver driver, Compilation outputCompilation, ImmutableArray<Diagnostic> diagnostics)
	{
		Driver = driver;
		OutputCompilation = outputCompilation;
		GeneratorDiagnostics = diagnostics;
		Result = driver.GetRunResult().Results.Single();
	}

	/// <summary>The driver after the run; feed it to <see cref="Execute" /> again to test incrementality.</summary>
	public GeneratorDriver Driver
	{
		get;
	}

	/// <summary>Input compilation plus the generated trees.</summary>
	public Compilation OutputCompilation
	{
		get;
	}

	/// <summary>Diagnostics reported by the generator itself for malformed or conflicting curated specifications.</summary>
	public ImmutableArray<Diagnostic> GeneratorDiagnostics
	{
		get;
	}

	public GeneratorRunResult Result
	{
		get;
	}

	public ImmutableArray<GeneratedSourceResult> GeneratedSources => Result.GeneratedSources;

	/// <summary>Text of the only generated file; fails when there is none or more than one.</summary>
	public string SingleGeneratedText => Assert.Single(GeneratedSources).SourceText.ToString();

	/// <summary>Hint names of the generated files, in generation order.</summary>
	public string[] HintNames => [.. GeneratedSources.Select(static source => source.HintName)];

	public static GeneratorRun Execute(GeneratorDriver driver, Compilation compilation)
	{
		GeneratorDriver updated = driver.RunGeneratorsAndUpdateCompilation(
			compilation,
			out Compilation outputCompilation,
			out ImmutableArray<Diagnostic> diagnostics,
			TestContext.Current.CancellationToken);

		return new GeneratorRun(updated, outputCompilation, diagnostics);
	}

	/// <summary>
	///     Text of the generated file whose hint name contains <paramref name="hintNameFragment" />; fails when there is
	///     none or more than one.
	/// </summary>
	public string GeneratedTextContaining(string hintNameFragment)
	{
		GeneratedSourceResult[] matches =
			[.. GeneratedSources.Where(source => source.HintName.Contains(hintNameFragment, StringComparison.Ordinal))];
		Assert.True(matches.Length == 1,
			$"Expected exactly one generated file whose hint name contains '{hintNameFragment}'. Generated: {string.Join(", ", HintNames)}.");
		return matches[0].SourceText.ToString();
	}

	/// <summary>
	///     Text of the generated file whose CONTENT contains <paramref name="codeFragment" /> (a method or type name
	///     unique to one spec file); fails when there is none or more than one. More robust than a hint-name lookup for
	///     tests that only care which file's text changed, not the hashed hint name.
	/// </summary>
	public string GeneratedTextByContent(string codeFragment)
	{
		GeneratedSourceResult[] matches =
		[
			.. GeneratedSources.Where(source =>
				source.SourceText.ToString().Contains(codeFragment, StringComparison.Ordinal))
		];
		Assert.True(matches.Length == 1,
			$"Expected exactly one generated file containing '{codeFragment}'. Generated: {string.Join(", ", HintNames)}.");
		return matches[0].SourceText.ToString();
	}

	/// <summary>Asserts that a valid or ignored input stays fully silent: no file, diagnostic, or generator exception.</summary>
	public void AssertNoOutput()
	{
		Assert.Null(Result.Exception);
		Assert.Empty(GeneratorDiagnostics);
		Assert.Empty(GeneratedSources);
	}

	/// <summary>
	///     Asserts that an invalid or conflicted spec emitted no source, while leaving diagnostic assertions to the
	///     caller.
	/// </summary>
	public void AssertNoGeneratedSource()
	{
		Assert.Null(Result.Exception);
		Assert.Empty(GeneratedSources);
	}

	/// <summary>
	///     Asserts that the updated compilation (generated code, against the real SDK assemblies) has no error and no
	///     warning, and that the generator reported nothing.
	/// </summary>
	public void AssertCompilesClean()
	{
		Assert.Null(Result.Exception);
		Assert.Empty(GeneratorDiagnostics);

		Diagnostic[] problems =
		[
			.. OutputCompilation.GetDiagnostics(TestContext.Current.CancellationToken)
				.Where(static d => d.Severity >= DiagnosticSeverity.Warning)
		];
		Assert.True(problems.Length == 0,
			"Unexpected compiler diagnostics:\n" + string.Join('\n', problems.AsEnumerable()));
	}
}
