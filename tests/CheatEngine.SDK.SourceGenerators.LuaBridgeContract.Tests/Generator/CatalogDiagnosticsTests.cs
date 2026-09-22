using System.Globalization;

using CheatEngine.SDK.SourceGenerators.LuaBridgeContract.Tests.Infrastructure;

using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.LuaBridgeContract.Tests.Generator;

/// <summary>Invalid additional-file content fails locally and never falls back to a stale generated contract.</summary>
public sealed class CatalogDiagnosticsTests
{
	[Fact]
	public void An_incorrect_bitmap_reports_the_external_file_value_and_emits_nothing()
	{
		string text = CatalogSources.ReverseOpcodeOrder.Replace("0x0000000000000401", "0x0000000000000001",
			StringComparison.Ordinal);
		const string Path = "eng/lua-bridge/protected-operations.json";
		GeneratorRun run = RoslynFixture.Run(Path, text);

		Assert.Empty(run.GeneratedSources);
		Diagnostic diagnostic = Assert.Single(run.GeneratorDiagnostics);
		Assert.Equal("CESDK4001", diagnostic.Id);
		Assert.Equal(LocationKind.ExternalFile, diagnostic.Location.Kind);
		FileLinePositionSpan lineSpan = diagnostic.Location.GetLineSpan();
		Assert.Equal(Path, lineSpan.Path);
		Assert.Equal(7, lineSpan.StartLinePosition.Line);
		Assert.Contains("operationBitmap", diagnostic.GetMessage(CultureInfo.InvariantCulture),
			StringComparison.Ordinal);
	}

	[Fact]
	public void Duplicate_opcode_reports_both_external_file_entries_and_emits_nothing()
	{
		string text = CatalogSources.ReverseOpcodeOrder
			.Replace("\"opcode\": 10", "\"opcode\": 0", StringComparison.Ordinal)
			.Replace("0x0000000000000401", "0x0000000000000001", StringComparison.Ordinal);
		GeneratorRun run = RoslynFixture.Run("eng/lua-bridge/protected-operations.json", text);

		Assert.Empty(run.GeneratedSources);
		Assert.Equal(2, run.GeneratorDiagnostics.Length);
		for (int i = 0; i < run.GeneratorDiagnostics.Length; i++)
		{
			Assert.Equal("CESDK4001", run.GeneratorDiagnostics[i].Id);
			Assert.Equal(LocationKind.ExternalFile, run.GeneratorDiagnostics[i].Location.Kind);
			Assert.Contains("opcode", run.GeneratorDiagnostics[i].GetMessage(CultureInfo.InvariantCulture),
				StringComparison.Ordinal);
		}
	}

	[Fact]
	public void Two_catalogue_additional_files_report_each_file_and_emit_nothing()
	{
		GeneratorRun run = RoslynFixture.Run(
			("one/protected-operations.json", CatalogSources.ReverseOpcodeOrder),
			("two/protected-operations.json", CatalogSources.ReverseOpcodeOrder));

		Assert.Empty(run.GeneratedSources);
		Assert.Equal(2, run.GeneratorDiagnostics.Length);
		for (int i = 0; i < run.GeneratorDiagnostics.Length; i++)
		{
			Assert.Equal("CESDK4002", run.GeneratorDiagnostics[i].Id);
		}
	}

	[Fact]
	public void A_valid_and_malformed_catalogue_both_receive_the_ambiguity_diagnostic()
	{
		const string ValidPath = "one/protected-operations.json";
		const string InvalidPath = "two/protected-operations.json";
		GeneratorRun run = RoslynFixture.Run(
			(ValidPath, CatalogSources.ReverseOpcodeOrder),
			(InvalidPath, "{ \"schemaVersion\":"));

		Assert.Empty(run.GeneratedSources);
		Assert.Equal(3, run.GeneratorDiagnostics.Length);
		Assert.Contains(run.GeneratorDiagnostics,
			static diagnostic => string.Equals(diagnostic.Id, "CESDK4001", StringComparison.Ordinal)
			                     && string.Equals(diagnostic.Location.GetLineSpan().Path, InvalidPath,
				                     StringComparison.Ordinal));
		Assert.Contains(run.GeneratorDiagnostics,
			static diagnostic => string.Equals(diagnostic.Id, "CESDK4002", StringComparison.Ordinal)
			                     && string.Equals(diagnostic.Location.GetLineSpan().Path, ValidPath,
				                     StringComparison.Ordinal));
		Assert.Contains(run.GeneratorDiagnostics,
			static diagnostic => string.Equals(diagnostic.Id, "CESDK4002", StringComparison.Ordinal)
			                     && string.Equals(diagnostic.Location.GetLineSpan().Path, InvalidPath,
				                     StringComparison.Ordinal));
	}

	[Fact]
	public void Malformed_json_reports_the_additional_file_and_emits_nothing()
	{
		const string Path = "eng/lua-bridge/protected-operations.json";
		GeneratorRun run = RoslynFixture.Run(Path, "{ \"schemaVersion\":");

		Assert.Empty(run.GeneratedSources);
		Diagnostic diagnostic = Assert.Single(run.GeneratorDiagnostics);
		Assert.Equal("CESDK4001", diagnostic.Id);
		Assert.Equal(LocationKind.ExternalFile, diagnostic.Location.Kind);
		Assert.Equal(Path, diagnostic.Location.GetLineSpan().Path);
	}

	[Fact]
	public void A_duplicate_json_property_reports_the_second_property_and_emits_nothing()
	{
		const string Path = "eng/lua-bridge/protected-operations.json";
		const string Text = "{\n  \"schemaVersion\": 1,\n  \"schemaVersion\": 1\n}";
		GeneratorRun run = RoslynFixture.Run(Path, Text);

		Assert.Empty(run.GeneratedSources);
		Diagnostic diagnostic = Assert.Single(run.GeneratorDiagnostics);
		Assert.Equal("CESDK4001", diagnostic.Id);
		Assert.Equal(LocationKind.ExternalFile, diagnostic.Location.Kind);
		FileLinePositionSpan lineSpan = diagnostic.Location.GetLineSpan();
		Assert.Equal(Path, lineSpan.Path);
		Assert.Equal(2, lineSpan.StartLinePosition.Line);
		Assert.Equal(2, lineSpan.StartLinePosition.Character);
		Assert.Contains("duplicated", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
	}
}
