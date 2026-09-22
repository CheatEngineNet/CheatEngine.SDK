using CheatEngine.SDK.SourceGenerators.LuaBridgeContract.Tests.Infrastructure;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CheatEngine.SDK.SourceGenerators.LuaBridgeContract.Tests.Generator;

/// <summary>Golden and semantic tests for the generated internal bridge operation contract.</summary>
public sealed class CatalogEmissionTests
{
	[Fact]
	public void Catalog_operations_emit_a_numeric_sorted_enum_and_required_bitmap()
	{
		GeneratorRun run =
			RoslynFixture.Run("eng/lua-bridge/protected-operations.json", CatalogSources.ReverseOpcodeOrder);

		run.AssertCompilesClean();
		string generated = run.SingleGeneratedText;
		Assert.Contains("internal enum LuaProtectedOperation", generated, StringComparison.Ordinal);
		Assert.Contains("PushBytes = 0", generated, StringComparison.Ordinal);
		Assert.Contains("PushHostObject = 10", generated, StringComparison.Ordinal);
		Assert.True(generated.IndexOf("PushBytes = 0", StringComparison.Ordinal)
		            < generated.IndexOf("PushHostObject = 10", StringComparison.Ordinal));
		Assert.Contains("internal const int Count = 2;", generated, StringComparison.Ordinal);
		Assert.Contains("internal const ulong RequiredBitmap = 0x0000000000000401UL;", generated,
			StringComparison.Ordinal);
		Assert.Contains("opcode < 64", generated, StringComparison.Ordinal);
	}

	[Fact]
	public void Production_catalog_emits_all_C11_operations_and_the_exact_required_bitmap()
	{
		GeneratorRun run = RoslynFixture.Run("eng/lua-bridge/protected-operations.json", ProductionCatalog.Read());

		run.AssertCompilesClean();
		string generated = run.SingleGeneratedText;
		string[] expectedMembers =
		[
			"PushBytes = 0",
			"CreateTable = 1",
			"NewUserdata = 2",
			"PushClosure = 3",
			"RawSet = 4",
			"RawSetIndex = 5",
			"RawSetPointer = 6",
			"CreateReference = 7",
			"PushReference = 8",
			"ReleaseReference = 9",
			"PushHostObject = 10",
			"PushByteTable = 11"
		];
		for (int i = 0; i < expectedMembers.Length; i++)
		{
			Assert.Contains(expectedMembers[i], generated, StringComparison.Ordinal);
		}

		Assert.Contains("internal const int Count = 12;", generated, StringComparison.Ordinal);
		Assert.Contains("internal const ulong RequiredBitmap = 0x0000000000000FFFUL;", generated,
			StringComparison.Ordinal);
	}

	[Fact]
	public void Catalog_an_identical_rerun_is_deterministic()
	{
		InMemoryAdditionalText additionalText = new("eng/lua-bridge/protected-operations.json",
			CatalogSources.ReverseOpcodeOrder);
		CSharpCompilation compilation = RoslynFixture.CreateCompilation();
		GeneratorRun first = GeneratorRun.Execute(RoslynFixture.CreateDriver(additionalText), compilation);
		GeneratorRun second = GeneratorRun.Execute(first.Driver, compilation);

		Assert.Equal(first.SingleGeneratedText, second.SingleGeneratedText, StringComparer.Ordinal);
		Assert.Empty(second.GeneratorDiagnostics);
		Assert.Null(second.Result.Exception);
	}

	[Fact]
	public void Replacing_a_valid_catalogue_updates_members_and_the_required_bitmap_without_stale_source()
	{
		InMemoryAdditionalText original = new("eng/lua-bridge/protected-operations.json",
			CatalogSources.ReverseOpcodeOrder);
		CSharpCompilation compilation = RoslynFixture.CreateCompilation();
		GeneratorRun first = GeneratorRun.Execute(RoslynFixture.CreateDriver(original), compilation);
		InMemoryAdditionalText replacement = new(
			original.Path,
			CatalogSources.ReverseOpcodeOrder
				.Replace("0x0000000000000401", "0x0000000000000011", StringComparison.Ordinal)
				.Replace("\"opcode\": 10", "\"opcode\": 4", StringComparison.Ordinal));

		GeneratorRun second =
			GeneratorRun.Execute(first.Driver.ReplaceAdditionalText(original, replacement), compilation);

		second.AssertCompilesClean();
		string generated = second.SingleGeneratedText;
		Assert.Contains("PushHostObject = 4", generated, StringComparison.Ordinal);
		Assert.Contains("internal const ulong RequiredBitmap = 0x0000000000000011UL;", generated,
			StringComparison.Ordinal);
		Assert.DoesNotContain("PushHostObject = 10", generated, StringComparison.Ordinal);
		Assert.DoesNotContain("0x0000000000000401UL", generated, StringComparison.Ordinal);
	}

	[Fact]
	public void Replacing_a_valid_catalogue_with_an_invalid_one_removes_generated_source()
	{
		InMemoryAdditionalText original = new("eng/lua-bridge/protected-operations.json",
			CatalogSources.ReverseOpcodeOrder);
		CSharpCompilation compilation = RoslynFixture.CreateCompilation();
		GeneratorRun first = GeneratorRun.Execute(RoslynFixture.CreateDriver(original), compilation);
		InMemoryAdditionalText replacement = new(
			original.Path,
			CatalogSources.ReverseOpcodeOrder.Replace("0x0000000000000401", "0x0000000000000001",
				StringComparison.Ordinal));

		GeneratorRun second =
			GeneratorRun.Execute(first.Driver.ReplaceAdditionalText(original, replacement), compilation);

		Assert.Empty(second.GeneratedSources);
		Diagnostic diagnostic = Assert.Single(second.GeneratorDiagnostics);
		Assert.Equal("CESDK4001", diagnostic.Id);
		Assert.Null(second.Result.Exception);
	}

	[Fact]
	public void An_unrelated_additional_file_is_silent()
	{
		GeneratorRun run = RoslynFixture.Run("eng/lua-bridge/notes.json", CatalogSources.ReverseOpcodeOrder);

		Assert.Empty(run.GeneratedSources);
		Assert.Empty(run.GeneratorDiagnostics);
		Assert.Null(run.Result.Exception);
	}
}
