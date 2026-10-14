using System.Globalization;
using System.Text;
using System.Text.Json;

using CheatEngine.SDK.SourceGenerators.LuaBridgeContract.Catalog;

using FsCheck;
using FsCheck.Fluent;

using Microsoft.CodeAnalysis.Text;

namespace CheatEngine.SDK.SourceGenerators.LuaBridgeContract.Tests.Catalog;

/// <summary>Generated catalogue inputs with independent model and source-location oracles.</summary>
public sealed class CatalogParserFuzzTests
{
	private const string FilePath = "fuzz/protected-operations.json";
	private static readonly Config s_configuration = Config.QuickThrowOnFailure.WithMaxTest(1000).WithEndSize(128);
	private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

	[Fact]
	public void Arbitrary_utf16_catalogues_report_exact_source_locations_without_recovery_exceptions()
	{
		Prop.ForAll<ushort[]>(characters =>
		{
			string text = new(Array.ConvertAll(characters, static value => (char) value));
			foreach (string input in (string[]) [text, "\r" + text, "{\r\"schemaVersion\":" + text,
				"{\r\n\"schemaVersion\":" + text])
			{
				AssertDiagnostics(input, Parse(input));
			}

			return true;
		}).Check(s_configuration);
	}

	[Fact]
	public void Generated_catalogues_preserve_sorted_operations_and_exact_bitmaps()
	{
		Prop.ForAll<byte[]>(values =>
		{
			int[] opcodes = Opcodes(values);
			ulong bitmap = Bitmap(opcodes);
			CatalogParseResult result = Parse(BuildCatalog(opcodes, bitmap));
			Assert.Empty(result.Diagnostics);
			CatalogModel catalog = Assert.IsType<CatalogModel>(result.Catalog);
			Assert.Equal(FilePath, catalog.SourcePath);
			AssertOperations(opcodes, catalog);
			Assert.Equal(bitmap, catalog.RequiredBitmap);
			return true;
		}).Check(s_configuration);
	}

	[Fact]
	public void Generated_duplicates_and_inconsistent_bitmaps_refuse_the_entire_catalogue()
	{
		Prop.ForAll<byte[]>(values =>
		{
			int[] opcodes = Opcodes(values);
			ulong bitmap = Bitmap(opcodes);
			string duplicateId = BuildCatalog(opcodes, bitmap, Operation(opcodes[0]));
			string duplicateOpcode = BuildCatalog(opcodes, bitmap, Operation(opcodes[0], "Alias"));
			foreach (string input in (string[]) [duplicateId, duplicateOpcode, BuildCatalog(opcodes, bitmap ^ 1UL)])
			{
				CatalogParseResult result = Parse(input);
				Assert.Null(result.Catalog);
				Assert.NotEmpty(result.Diagnostics);
				AssertDiagnostics(input, result);
			}

			Assert.Equal(2, Parse(duplicateId).Diagnostics.Length);
			Assert.Equal(2, Parse(duplicateOpcode).Diagnostics.Length);
			return true;
		}).Check(s_configuration);
	}

	[Fact]
	public void Generated_escaped_identifiers_and_line_endings_preserve_the_catalogue()
	{
		Prop.ForAll<byte[]>(values =>
		{
			int[] opcodes = Opcodes(values);
			ulong bitmap = Bitmap(opcodes);
			string text = BuildCatalog(opcodes, bitmap);
			foreach (string input in (string[]) [text.Replace("Operation", "\\u004Fperation", StringComparison.Ordinal),
				text.Replace("\n", "\r\n", StringComparison.Ordinal), text.Replace('\n', '\r')])
			{
				CatalogParseResult result = Parse(input);
				Assert.Empty(result.Diagnostics);
				CatalogModel catalog = Assert.IsType<CatalogModel>(result.Catalog);
				AssertOperations(opcodes, catalog);
				Assert.Equal(bitmap, catalog.RequiredBitmap);
			}

			return true;
		}).Check(s_configuration);
	}

	[Theory]
	[InlineData("\n")]
	[InlineData("\r\n")]
	[InlineData("\r")]
	public void Truncated_json_reports_the_source_text_line_position(string newline)
	{
		string text = "{" + newline + "\"schemaVersion\":";
		CatalogParseResult result = Parse(text);
		Assert.Null(result.Catalog);
		CatalogDiagnostic diagnostic = Assert.Single(result.Diagnostics);
		Assert.Equal(text.Length, diagnostic.Span.Start);
		Assert.Equal(new LinePosition(1, 16), diagnostic.LineSpan.Start);
		AssertDiagnostics(text, result);
	}

	[Theory]
	[InlineData("\u0085")]
	[InlineData("\u2028")]
	[InlineData("\u2029")]
	public void Unicode_line_separators_in_strings_preserve_diagnostic_spans(string separator)
	{
		string text = "{\"schemaVersion\":\"before" + separator + "after\"}";
		CatalogParseResult result = Parse(text);
		Assert.Null(result.Catalog);
		Assert.NotEmpty(result.Diagnostics);
		AssertDiagnostics(text, result);
	}

	[Theory]
	[InlineData(63, true)]
	[InlineData(64, true)]
	[InlineData(65, false)]
	[InlineData(1024, false)]
	public void Json_container_depth_is_bounded_before_recursion(int depth, bool accepted)
	{
		foreach (bool objects in (bool[]) [false, true])
		{
			string text = WithNestedMetadata(depth, objects);
			CatalogParseResult result = Parse(text);
			if (accepted)
			{
				Assert.Empty(result.Diagnostics);
				CatalogModel catalog = Assert.IsType<CatalogModel>(result.Catalog);
				Assert.Equal(0x8000000000000001UL, catalog.RequiredBitmap);
				AssertOperations([0, 63], catalog);
			}
			else
			{
				Assert.Null(result.Catalog);
				CatalogDiagnostic diagnostic = Assert.Single(result.Diagnostics);
				Assert.Contains("maximum depth of 64", diagnostic.Message, StringComparison.Ordinal);
				AssertDiagnostics(text, result);
			}
		}
	}

	[Fact]
	public void Generated_nested_json_respects_the_depth_limit_and_restores_sibling_depth()
	{
		Prop.ForAll<byte>(value =>
		{
			int depth = 2 + value;
			foreach (bool objects in (bool[]) [false, true])
			{
				string text = WithNestedMetadata(depth, objects);
				CatalogParseResult result = Parse(text);
				Assert.Equal(depth <= 64, result.Catalog is not null);
				if (depth <= 64)
				{
					Assert.Empty(result.Diagnostics);
					Assert.Equal(2, result.Catalog!.Operations.Length);
				}
				else
				{
					CatalogDiagnostic diagnostic = Assert.Single(result.Diagnostics);
					Assert.Contains("maximum depth of 64", diagnostic.Message, StringComparison.Ordinal);
				}

				AssertDiagnostics(text, result);
			}

			return true;
		}).Check(s_configuration);
	}

	private static string WithNestedMetadata(int totalDepth, bool objects)
	{
		string opener = objects ? "{\"next\":" : "[";
		string closer = objects ? "}" : "]";
		StringBuilder builder = new();
		for (int i = 1; i < totalDepth; i++)
		{
			builder.Append(opener);
		}

		builder.Append('0');
		for (int i = 1; i < totalDepth; i++)
		{
			builder.Append(closer);
		}

		string nested = builder.ToString();
		// Parse the equally deep sibling too: failure to unwind depth cannot silently reject it.
		string text = BuildCatalog([0, 63], 0x8000000000000001UL);
		return text[..^1] + ",\"metadata\":" + nested + ",\"sibling\":" + nested + "}";
	}

	private static CatalogParseResult Parse(string text) => ProtectedOperationCatalogParser.Parse(new CatalogInput(FilePath, text));

	private static void AssertDiagnostics(string text, CatalogParseResult result)
	{
		Assert.Equal(FilePath, result.SourcePath);
		SourceText source = SourceText.From(text);
		foreach (CatalogDiagnostic diagnostic in result.Diagnostics)
		{
			Assert.Equal(FilePath, diagnostic.Path);
			Assert.InRange(diagnostic.Span.Start, 0, text.Length);
			Assert.InRange(diagnostic.Span.End, diagnostic.Span.Start, text.Length);
			Assert.Equal(source.Lines.GetLinePositionSpan(diagnostic.Span), diagnostic.LineSpan);
			Assert.DoesNotContain("recovered an unexpected", diagnostic.Message, StringComparison.Ordinal);
		}

		if (result.Diagnostics.Length > 0)
		{
			Assert.Null(result.Catalog);
		}
	}

	private static void AssertOperations(int[] opcodes, CatalogModel catalog)
	{
		int[] sorted = (int[]) opcodes.Clone();
		Array.Sort(sorted);
		Assert.Equal(sorted.Length, catalog.Operations.Length);
		for (int i = 0; i < sorted.Length; i++)
		{
			Assert.Equal(sorted[i], catalog.Operations[i].Opcode);
			Assert.Equal(Id(sorted[i]), catalog.Operations[i].Id, StringComparer.Ordinal);
		}
	}

	private static int[] Opcodes(byte[] values)
	{
		HashSet<int> seen = [];
		List<int> opcodes = [];
		foreach (byte value in values)
		{
			int opcode = value % 64;
			if (seen.Add(opcode))
			{
				opcodes.Add(opcode);
			}
		}

		foreach (int boundary in (int[]) [0, 63])
		{
			if (seen.Add(boundary))
			{
				opcodes.Add(boundary);
			}
		}

		return opcodes.ToArray();
	}

	private static ulong Bitmap(int[] opcodes)
	{
		ulong bitmap = 0;
		foreach (int opcode in opcodes)
		{
			bitmap |= 1UL << opcode;
		}

		return bitmap;
	}

	private static string Id(int opcode) => "Operation" + opcode.ToString("D2", CultureInfo.InvariantCulture);

	private static object Operation(int opcode, string? id = null)
	{
		string name = id ?? Id(opcode);
		return new
		{
			id = name,
			opcode,
			managed = new
			{
				constant = name + "Operation",
				wrapper = name
			},
			@protected = true,
			requiresNativeProtection = true
		};
	}

	private static string BuildCatalog(int[] opcodes, ulong bitmap, object? additionalOperation = null)
	{
		List<object> operations = [];
		foreach (int opcode in opcodes)
		{
			operations.Add(Operation(opcode));
		}

		if (additionalOperation is not null)
		{
			operations.Add(additionalOperation);
		}

		return JsonSerializer.Serialize(new
		{
			schemaVersion = 1,
			catalogId = "cheatengine-sdk-lua-protected-operations",
			bridgeContract = new
			{
				abiMajor = 1,
				minimumAbiMinor = 1,
				operationBitmapWidth = 64,
				operationBitmap = "0x" + bitmap.ToString("X16", CultureInfo.InvariantCulture)
			},
			operations
		},
			s_jsonOptions);
	}
}
