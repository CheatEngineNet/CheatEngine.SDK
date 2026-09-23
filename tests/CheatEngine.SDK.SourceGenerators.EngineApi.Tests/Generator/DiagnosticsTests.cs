using System.Globalization;

using CheatEngine.SDK.SourceGenerators.EngineApi.Tests.Infrastructure;

using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.EngineApi.Tests.Generator;

/// <summary>Regression tests for diagnostics that point at malformed or conflicting additional spec files.</summary>
public sealed class DiagnosticsTests(RoslynFixture roslyn) : IClassFixture<RoslynFixture>
{
	/// <summary>Malformed input reports its additional-file line and does not hide a separately valid wrapper.</summary>
	[Fact]
	public void An_invalid_entry_reports_its_additional_file_line_and_column_while_a_valid_sibling_is_emitted()
	{
		const string Text =
			"namespace: Demo\ntype: T\n" + SpecSources.Ce77 +
			"\nglobal: readInteger\nmethod: Bad\nform: try\nresult: value:int32\nnil: none\ndoc: bad.\nextra: value\n\nglobal: readQword\nmethod: Good\nform: try\nresult: value:int64\nnil: none\ndoc: good.\n";
		const string Path = "Specs/diagnostics.cheatengine-sdk-api.txt";

		GeneratorRun run = roslyn.Run(Path, Text);

		Assert.Null(run.Result.Exception);
		Assert.Single(run.GeneratedSources);
		Diagnostic diagnostic = Assert.Single(run.GeneratorDiagnostics);
		Assert.Equal("CESDK3001", diagnostic.Id);
		Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
		Assert.Equal(LocationKind.ExternalFile, diagnostic.Location.Kind);
		FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
		Assert.Equal(Path, span.Path);
		Assert.Equal(15, span.StartLinePosition.Line);
		Assert.Equal(0, span.StartLinePosition.Character);
		Assert.Contains("Unknown entry key 'extra'", diagnostic.GetMessage(CultureInfo.InvariantCulture),
			StringComparison.Ordinal);
		Assert.Contains("Good", run.SingleGeneratedText, StringComparison.Ordinal);
	}

	/// <summary>Every ce77 contract field is validated as an additional-file diagnostic at its value, not as a C# error.</summary>
	[Fact]
	public void An_invalid_ce77_nil_contract_reports_the_exact_additional_file_value_location()
	{
		const string Path = "Specs/invalid-contract.cheatengine-sdk-api.txt";
		const string Text =
			"namespace: Demo\ntype: Contract\ncontract: ce77\nprovenance: ExactInstalledFile: CE fixture\nminimum-ce: 7.7.0.10621\narchitecture: x64\nthread: unknown\nownership: none\n\nglobal: readInteger\nmethod: Read\nform: try\nresult: value:int32\nnil: ambiguous\ndoc: Reads an integer.\n";

		GeneratorRun run = roslyn.Run(Path, Text);

		run.AssertNoGeneratedSource();
		Diagnostic diagnostic = Assert.Single(run.GeneratorDiagnostics);
		Assert.Equal("CESDK3001", diagnostic.Id);
		FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
		Assert.Equal(Path, span.Path);
		Assert.Equal(13, span.StartLinePosition.Line);
		Assert.Equal(5, span.StartLinePosition.Character);
		Assert.Contains("not a valid nil contract", diagnostic.GetMessage(CultureInfo.InvariantCulture),
			StringComparison.Ordinal);
	}

	/// <summary>A duplicate target type blocks only its owning specs and reports each participating additional file.</summary>
	[Fact]
	public void Conflicting_specs_report_each_participant_and_emit_neither_while_an_independent_type_is_emitted()
	{
		const string First =
			"namespace: Demo\ntype: Duplicate\n" + SpecSources.Ce77 +
			"\nglobal: readInteger\nmethod: First\nform: try\nresult: value:int32\nnil: none\ndoc: first.\n";
		const string Second =
			"namespace: Demo\ntype: Duplicate\n" + SpecSources.Ce77 +
			"\nglobal: readQword\nmethod: Second\nform: try\nresult: value:int64\nnil: none\ndoc: second.\n";

		GeneratorRun run = roslyn.Run(
			("Specs/first.cheatengine-sdk-api.txt", First),
			("Specs/second.cheatengine-sdk-api.txt", Second),
			("Specs/independent.cheatengine-sdk-api.txt", SpecSources.BeepOnly));

		Assert.Null(run.Result.Exception);
		Assert.Single(run.GeneratedSources);
		Assert.Contains("Other", run.SingleGeneratedText, StringComparison.Ordinal);
		Assert.Equal(2, run.GeneratorDiagnostics.Length);
		foreach (Diagnostic diagnostic in run.GeneratorDiagnostics)
		{
			Assert.Equal("CESDK3002", diagnostic.Id);
			Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
			Assert.Contains("Duplicate", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
		}

		AssertConflictLocation(run, "Generated type", "Specs/first.cheatengine-sdk-api.txt", 1, 6);
		AssertConflictLocation(run, "Generated type", "Specs/second.cheatengine-sdk-api.txt", 1, 6);
	}

	/// <summary>Duplicate wrapper and cache identities receive specific conflict diagnostics before code generation.</summary>
	[Fact]
	public void Conflicting_member_and_cache_identities_are_diagnosed_on_both_spec_files()
	{
		const string First =
			"namespace: Demo\ntype: Duplicate\n" + SpecSources.Ce77 +
			"\n  global: readInteger\n  method: Same\n  form: try\n  result: value:int32\n  nil: none\n  doc: first.\n";
		const string Second =
			"namespace: Demo\ntype: Duplicate\n" + SpecSources.Ce77 +
			"\n  global: readInteger\n  method: Same\n  form: try\n  result: value:int32\n  nil: none\n  doc: second.\n";

		GeneratorRun run = roslyn.Run(
			("Specs/first.cheatengine-sdk-api.txt", First),
			("Specs/second.cheatengine-sdk-api.txt", Second));

		run.AssertNoGeneratedSource();
		Assert.Contains(run.GeneratorDiagnostics,
			static diagnostic => string.Equals(diagnostic.Id, "CESDK3002", StringComparison.Ordinal)
								 && diagnostic.GetMessage(CultureInfo.InvariantCulture)
									 .Contains("member", StringComparison.Ordinal));
		Assert.Contains(run.GeneratorDiagnostics,
			static diagnostic => string.Equals(diagnostic.Id, "CESDK3002", StringComparison.Ordinal)
								 && diagnostic.GetMessage(CultureInfo.InvariantCulture)
									 .Contains("cache field", StringComparison.Ordinal));
		AssertConflictLocation(run, "Generated member", "Specs/first.cheatengine-sdk-api.txt", 10, 10);
		AssertConflictLocation(run, "Generated member", "Specs/second.cheatengine-sdk-api.txt", 10, 10);
		AssertConflictLocation(run, "Generated cache field", "Specs/first.cheatengine-sdk-api.txt", 9, 10);
		AssertConflictLocation(run, "Generated cache field", "Specs/second.cheatengine-sdk-api.txt", 9, 10);
	}

	/// <summary>Repeated file names from different directories always receive separate deterministic source hint names.</summary>
	[Fact]
	public void Three_same_named_spec_files_receive_unique_case_insensitive_hint_names()
	{
		const string First =
			"namespace: Demo\ntype: First\n" + SpecSources.Ce77 +
			"\nglobal: first\nmethod: LoadFirst\nform: throwing\nnil: none\ndoc: first.\n";
		const string Second =
			"namespace: Demo\ntype: Second\n" + SpecSources.Ce77 +
			"\nglobal: second\nmethod: LoadSecond\nform: throwing\nnil: none\ndoc: second.\n";
		const string Third =
			"namespace: Demo\ntype: Third\n" + SpecSources.Ce77 +
			"\nglobal: third\nmethod: LoadThird\nform: throwing\nnil: none\ndoc: third.\n";

		GeneratorRun run = roslyn.Run(
			("One/shared.cheatengine-sdk-api.txt", First),
			("Two/shared.cheatengine-sdk-api.txt", Second),
			("Three/shared.cheatengine-sdk-api.txt", Third));

		run.AssertCompilesClean();
		Assert.Equal(3, run.HintNames.Length);
		HashSet<string> distinct = new(run.HintNames, StringComparer.OrdinalIgnoreCase);
		Assert.Equal(3, distinct.Count);
	}

	[Fact]
	public void A_spec_with_entries_and_no_contract_reports_CESDK3003_on_its_header()
	{
		const string Path = "Specs/legacy.cheatengine-sdk-api.txt";
		const string Text =
			"# legacy\nnamespace: Demo\ntype: Legacy\n\nglobal: readInteger\nmethod: TryReadInt32\nform: try\nresult: value:int32\ndoc: d.\n";

		GeneratorRun run = roslyn.Run(Path, Text);

		run.AssertNoGeneratedSource();
		AssertLocated(Assert.Single(run.GeneratorDiagnostics), "CESDK3003", Path, 1, 0, "'contract: ce77'");
	}

	[Fact]
	public void An_argument_after_an_optional_one_reports_CESDK3004_at_the_argument()
	{
		const string Path = "Specs/optional.cheatengine-sdk-api.txt";
		string text = SpecSources.Ce77Header("Demo", "Optional") +
					  "global: g\nmethod: G\nform: throwing\nopt: a:int32\narg: b:int64\nnil: none\ndoc: d.\n";

		GeneratorRun run = roslyn.Run(Path, text);

		run.AssertNoGeneratedSource();
		AssertLocated(Assert.Single(run.GeneratorDiagnostics), "CESDK3004", Path, 13, 5, "follows an 'opt' argument");
	}

	[Fact]
	public void A_required_result_after_an_optional_one_reports_CESDK3005_at_the_result()
	{
		const string Path = "Specs/results.cheatengine-sdk-api.txt";
		string text = SpecSources.Ce77Header("Demo", "Results") +
					  "global: g\nmethod: G\nform: outcome\nopt-result: a:int32\nresult: b:int64\nnil: none\ndoc: d.\n";

		GeneratorRun run = roslyn.Run(Path, text);

		run.AssertNoGeneratedSource();
		AssertLocated(Assert.Single(run.GeneratorDiagnostics), "CESDK3005", Path, 13, 8, "after an 'opt-result'");
	}

	private static void AssertLocated(Diagnostic diagnostic, string id, string path, int line, int character,
		string messageFragment)
	{
		Assert.Equal(id, diagnostic.Id);
		Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
		Assert.Equal("https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/analyzers/docs/" + id + ".md",
			diagnostic.Descriptor.HelpLinkUri);
		FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
		Assert.Equal(path, span.Path);
		Assert.Equal(line, span.StartLinePosition.Line);
		Assert.Equal(character, span.StartLinePosition.Character);
		Assert.Contains(messageFragment, diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
	}

	private static void AssertConflictLocation(GeneratorRun run, string messageFragment, string path, int line,
		int character)
	{
		foreach (Diagnostic diagnostic in run.GeneratorDiagnostics)
		{
			if (!diagnostic.GetMessage(CultureInfo.InvariantCulture)
					.Contains(messageFragment, StringComparison.Ordinal))
			{
				continue;
			}

			FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
			if (!string.Equals(span.Path, path, StringComparison.Ordinal))
			{
				continue;
			}

			Assert.Equal(line, span.StartLinePosition.Line);
			Assert.Equal(character, span.StartLinePosition.Character);
			return;
		}

		Assert.Fail("Expected a CESDK3002 diagnostic containing '" + messageFragment + "' for '" + path + "'.");
	}
}
