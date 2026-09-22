using System.Collections.Immutable;

using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Infrastructure;

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

	/// <summary>Diagnostics reported by the generator itself (this generator must never report any).</summary>
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

	/// <summary>Text of the generated file named <paramref name="hintName" />; fails when there is none.</summary>
	public string GeneratedText(string hintName)
	{
		foreach (GeneratedSourceResult source in GeneratedSources)
		{
			if (string.Equals(source.HintName, hintName, StringComparison.Ordinal))
			{
				return source.SourceText.ToString();
			}
		}

		Assert.Fail($"No generated file is named '{hintName}'. Generated: {string.Join(", ", HintNames)}.");
		return string.Empty;
	}

	/// <summary>Asserts "silent": no file, no generator diagnostic, no exception swallowed by the driver.</summary>
	public void AssertNoOutput()
	{
		Assert.Null(Result.Exception);
		Assert.Empty(GeneratorDiagnostics);
		Assert.Empty(GeneratedSources);
	}

	/// <summary>
	///     Asserts that the updated compilation (user code + generated code, against the real SDK assemblies) has no
	///     error and no warning, and that the generator reported nothing.
	/// </summary>
	public void AssertCompilesClean()
	{
		Assert.Null(Result.Exception);
		Assert.Empty(GeneratorDiagnostics);

		Diagnostic[] problems =
		[
			.. OutputCompilation
				.GetDiagnostics(TestContext.Current.CancellationToken)
				.Where(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning &&
				                            !IsMissingDocumentationInTestInput(diagnostic))
		];

		Assert.True(problems.Length == 0,
			"Unexpected compiler diagnostics:\n" + string.Join('\n', problems.AsEnumerable()));
	}

	// The test classes are public and undocumented on purpose (short sources). CS1591 is ignored for them, and only
	// for them: in a generated file it still fails the assertion.
	private static bool IsMissingDocumentationInTestInput(Diagnostic diagnostic)
	{
		return string.Equals(diagnostic.Id, "CS1591", StringComparison.Ordinal)
		       && diagnostic.Location.SourceTree is { FilePath: string path }
		       && !path.EndsWith(".g.cs", StringComparison.Ordinal);
	}
}
