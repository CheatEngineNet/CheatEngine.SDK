using System.Globalization;
using System.Text;

using CheatEngine.SDK.SourceGenerators.EngineApi.Model;
using CheatEngine.SDK.SourceGenerators.EngineApi.Parsing;
using CheatEngine.SDK.SourceGenerators.EngineApi.Tests.Infrastructure;

using FsCheck;
using FsCheck.Fluent;

namespace CheatEngine.SDK.SourceGenerators.EngineApi.Tests.Parsing;

/// <summary>Generated-input checks of the parser's refusal, ordering and line-ending contracts.</summary>
public sealed class SpecFileParserFuzzTests
{
	private const string FilePath = "fuzz.cheatengine-sdk-api.txt";
	private static readonly Config s_configuration = Config.QuickThrowOnFailure.WithMaxTest(1000).WithEndSize(128);

	[Fact]
	public void Arbitrary_utf16_input_reports_localized_issues_without_throwing()
	{
		Prop.ForAll<ushort[]>(characters =>
		{
			string text = new([.. characters.Select(static value => (char) value)]);
			foreach (string input in (string[]) [text, SpecSources.SingleTry + "\n\n" + text, text + "\0\ud800\uffff"])
			{
				SpecFileModel spec = SpecFileParser.Parse(FilePath, input);
				string[] lines = input.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
				Assert.Equal(FilePath, spec.SourcePath);
				foreach (SpecIssue issue in spec.Issues.AsSpan())
				{
					Assert.InRange(issue.Line, 1, lines.Length);
					Assert.InRange(issue.Column, 1, lines[issue.Line - 1].Length + 1);
				}
			}

			return true;
		}).Check(s_configuration);
	}

	[Fact]
	public void Generated_entries_drop_every_duplicate_and_preserve_sorted_calls_and_globals()
	{
		Prop.ForAll<byte[]>(values =>
		{
			// The anchor is always valid and unique; 254 always has at least two entries to reject.
			int[] entries = [-1, .. values.Select(static value => (int) value), 254, 254];
			SpecFileModel spec = SpecFileParser.Parse(FilePath, BuildSpec(entries));
			int[] surviving = [.. entries.GroupBy(static value => value).Where(static group => group.Count() == 1)
				.Select(static group => group.Key)];
			string[] expectedMethods = [.. surviving.Select(MethodName).Order(StringComparer.Ordinal)];
			string[] expectedGlobals = [.. surviving.Select(GlobalName).Distinct(StringComparer.Ordinal)
				.Order(StringComparer.Ordinal)];

			Assert.Equal(expectedMethods, spec.Calls.AsSpan().ToArray().Select(static call => call.Call.MethodName),
				StringComparer.Ordinal);
			Assert.Equal(expectedGlobals, spec.CachedGlobals.AsSpan().ToArray());
			Assert.Equal(entries.Length - surviving.Length, spec.Issues.Length);
			Assert.All(spec.Issues.AsSpan().ToArray(),
				static issue => Assert.Contains("Duplicate method name", issue.Message, StringComparison.Ordinal));
			return true;
		}).Check(s_configuration);
	}

	[Fact]
	public void Generated_specs_preserve_models_and_diagnostics_across_line_endings()
	{
		Prop.ForAll<byte[]>(values =>
		{
			string text = BuildSpec([-1, .. values.Select(static value => (int) value), 254, 254])
				+ "\n\nunknown-key: " + Convert.ToHexString(values);
			SpecFileModel expected = SpecFileParser.Parse(FilePath, text);

			Assert.NotEmpty(expected.Calls.AsSpan().ToArray());
			Assert.NotEmpty(expected.Issues.AsSpan().ToArray());
			Assert.Equal(expected, SpecFileParser.Parse(FilePath, text.Replace("\n", "\r\n", StringComparison.Ordinal)));
			Assert.Equal(expected, SpecFileParser.Parse(FilePath, text.Replace('\n', '\r')));
			return true;
		}).Check(s_configuration);
	}

	private static string BuildSpec(int[] entries)
	{
		StringBuilder text = new("namespace: Fuzz.Generated\ntype: FuzzApi\n" + SpecSources.Ce77);
		foreach (int entry in entries)
		{
			text.Append("\nglobal: ").Append(GlobalName(entry)).Append("\nmethod: ").Append(MethodName(entry))
				.Append("\nform: try\nresult: value:int32\nnil: none\ndoc: Generated parser fixture.\n");
		}

		return text.ToString();
	}

	private static string MethodName(int value) => value < 0 ? "ReadAnchor" : "Read" + value.ToString("D3", CultureInfo.InvariantCulture);
	private static string GlobalName(int value) => value < 0 ? "anchorGlobal" : "sampleGlobal" + (value % 8).ToString(CultureInfo.InvariantCulture);
}
