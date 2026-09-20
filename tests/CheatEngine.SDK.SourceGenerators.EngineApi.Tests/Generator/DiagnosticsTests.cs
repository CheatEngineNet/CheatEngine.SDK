using CheatEngine.SDK.SourceGenerators.EngineApi.Tests.Infrastructure;
using System.Globalization;
using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.EngineApi.Tests.Generator;

/// <summary>Regression tests for diagnostics that point at malformed or conflicting additional spec files.</summary>
public sealed class DiagnosticsTests(RoslynFixture roslyn) : IClassFixture<RoslynFixture>
{
    /// <summary>Malformed input reports its additional-file line and does not hide a separately valid wrapper.</summary>
    [Fact]
    public void An_invalid_entry_reports_its_additional_file_line_and_column_while_a_valid_sibling_is_emitted()
    {
        const string Text = "namespace: Demo\ntype: T\n\nglobal: readInteger\nmethod: Bad\nform: try\nresult: value:int32\ndoc: bad.\nextra: value\n\nglobal: readQword\nmethod: Good\nform: try\nresult: value:int64\ndoc: good.\n";
        const string Path = "Specs/diagnostics.cheatengine-sdk-api.txt";

        var run = roslyn.Run(Path, Text);

        Assert.Null(run.Result.Exception);
        Assert.Single(run.GeneratedSources);
        var diagnostic = Assert.Single(run.GeneratorDiagnostics);
        Assert.Equal("CESDK3001", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(LocationKind.ExternalFile, diagnostic.Location.Kind);
        var span = diagnostic.Location.GetLineSpan();
        Assert.Equal(Path, span.Path);
        Assert.Equal(8, span.StartLinePosition.Line);
        Assert.Equal(0, span.StartLinePosition.Character);
        Assert.Contains("Unknown entry key 'extra'", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Contains("Good", run.SingleGeneratedText, StringComparison.Ordinal);
    }

    /// <summary>Every ce77 contract field is validated as an additional-file diagnostic at its value, not as a C# error.</summary>
    [Fact]
    public void An_invalid_ce77_nil_contract_reports_the_exact_additional_file_value_location()
    {
        const string Path = "Specs/invalid-contract.cheatengine-sdk-api.txt";
        const string Text = "namespace: Demo\ntype: Contract\ncontract: ce77\nprovenance: ExactInstalledFile: CE fixture\nminimum-ce: 7.7.0.10621\narchitecture: x64\nthread: unknown\nownership: none\n\nglobal: readInteger\nmethod: Read\nform: try\nresult: value:int32\nnil: ambiguous\ndoc: Reads an integer.\n";

        var run = roslyn.Run(Path, Text);

        run.AssertNoGeneratedSource();
        var diagnostic = Assert.Single(run.GeneratorDiagnostics);
        Assert.Equal("CESDK3001", diagnostic.Id);
        var span = diagnostic.Location.GetLineSpan();
        Assert.Equal(Path, span.Path);
        Assert.Equal(13, span.StartLinePosition.Line);
        Assert.Equal(5, span.StartLinePosition.Character);
        Assert.Contains("not a valid nil contract", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    /// <summary>A duplicate target type blocks only its owning specs and reports each participating additional file.</summary>
    [Fact]
    public void Conflicting_specs_report_each_participant_and_emit_neither_while_an_independent_type_is_emitted()
    {
        const string First = "namespace: Demo\ntype: Duplicate\n\nglobal: readInteger\nmethod: First\nform: try\nresult: value:int32\ndoc: first.\n";
        const string Second = "namespace: Demo\ntype: Duplicate\n\nglobal: readQword\nmethod: Second\nform: try\nresult: value:int64\ndoc: second.\n";

        var run = roslyn.Run(
            ("Specs/first.cheatengine-sdk-api.txt", First),
            ("Specs/second.cheatengine-sdk-api.txt", Second),
            ("Specs/independent.cheatengine-sdk-api.txt", SpecSources.BeepOnly));

        Assert.Null(run.Result.Exception);
        Assert.Single(run.GeneratedSources);
        Assert.Contains("Other", run.SingleGeneratedText, StringComparison.Ordinal);
        Assert.Equal(2, run.GeneratorDiagnostics.Length);
        foreach (var diagnostic in run.GeneratorDiagnostics)
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
        const string First = "namespace: Demo\ntype: Duplicate\n\n  global: readInteger\n  method: Same\n  form: try\n  result: value:int32\n  doc: first.\n";
        const string Second = "namespace: Demo\ntype: Duplicate\n\n  global: readInteger\n  method: Same\n  form: try\n  result: value:int32\n  doc: second.\n";

        var run = roslyn.Run(
            ("Specs/first.cheatengine-sdk-api.txt", First),
            ("Specs/second.cheatengine-sdk-api.txt", Second));

        run.AssertNoGeneratedSource();
        Assert.Contains(run.GeneratorDiagnostics,
            static diagnostic => string.Equals(diagnostic.Id, "CESDK3002", StringComparison.Ordinal)
                                 && diagnostic.GetMessage(CultureInfo.InvariantCulture).Contains("member", StringComparison.Ordinal));
        Assert.Contains(run.GeneratorDiagnostics,
            static diagnostic => string.Equals(diagnostic.Id, "CESDK3002", StringComparison.Ordinal)
                                 && diagnostic.GetMessage(CultureInfo.InvariantCulture).Contains("cache field", StringComparison.Ordinal));
        AssertConflictLocation(run, "Generated member", "Specs/first.cheatengine-sdk-api.txt", 4, 10);
        AssertConflictLocation(run, "Generated member", "Specs/second.cheatengine-sdk-api.txt", 4, 10);
        AssertConflictLocation(run, "Generated cache field", "Specs/first.cheatengine-sdk-api.txt", 3, 10);
        AssertConflictLocation(run, "Generated cache field", "Specs/second.cheatengine-sdk-api.txt", 3, 10);
    }

    /// <summary>Repeated file names from different directories always receive separate deterministic source hint names.</summary>
    [Fact]
    public void Three_same_named_spec_files_receive_unique_case_insensitive_hint_names()
    {
        const string First = "namespace: Demo\ntype: First\n\nglobal: first\nmethod: LoadFirst\nform: throwing\ndoc: first.\n";
        const string Second = "namespace: Demo\ntype: Second\n\nglobal: second\nmethod: LoadSecond\nform: throwing\ndoc: second.\n";
        const string Third = "namespace: Demo\ntype: Third\n\nglobal: third\nmethod: LoadThird\nform: throwing\ndoc: third.\n";

        var run = roslyn.Run(
            ("One/shared.cheatengine-sdk-api.txt", First),
            ("Two/shared.cheatengine-sdk-api.txt", Second),
            ("Three/shared.cheatengine-sdk-api.txt", Third));

        run.AssertCompilesClean();
        Assert.Equal(3, run.HintNames.Length);
        HashSet<string> distinct = new(run.HintNames, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(3, distinct.Count);
    }

    private static void AssertConflictLocation(GeneratorRun run, string messageFragment, string path, int line,
        int character)
    {
        foreach (var diagnostic in run.GeneratorDiagnostics)
        {
            if (!diagnostic.GetMessage(CultureInfo.InvariantCulture).Contains(messageFragment, StringComparison.Ordinal))
                continue;

            var span = diagnostic.Location.GetLineSpan();
            if (!string.Equals(span.Path, path, StringComparison.Ordinal)) continue;

            Assert.Equal(line, span.StartLinePosition.Line);
            Assert.Equal(character, span.StartLinePosition.Character);
            return;
        }

        Assert.Fail("Expected a CESDK3002 diagnostic containing '" + messageFragment + "' for '" + path + "'.");
    }
}
