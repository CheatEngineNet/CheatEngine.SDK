using System.Text.RegularExpressions;

using CheatEngine.SDK.Repository.Tests.Infrastructure;

namespace CheatEngine.SDK.Repository.Tests.ApiGates;

/// <summary>
///     Every <c>[Experimental("CESDKxxxx")]</c> and <c>[Obsolete(DiagnosticId = "CESDKxxxx")]</c> declared by shipping
///     source (<c>libs/</c>, <c>src/</c>) is a public diagnostic without an analyzer descriptor: the compiler reports it.
///     Like an analyzer rule it needs a help link and a page (shared contracts section 3.2): the attribute uses the
///     repository's <c>UrlFormat</c>, <c>analyzers/docs/&lt;id&gt;.md</c> exists and starts with the id, and the
///     diagnostics index lists it. Experimental gates use the <c>CESDK5xxx</c> range.
/// </summary>
/// <remarks>
///     Shared-contracts section 3.2 describes this catalog as built "by reflection over the packed assemblies". This
///     project has no <c>ProjectReference</c> by design (its own top-of-file comment), so it reads the committed source
///     of <c>libs/</c> and <c>src/</c> instead; this is a reported deviation, not a silent one. A source scan cannot
///     detect a declaration whose attribute text is present but does not actually bind (a named-argument typo, a member
///     excluded from the public surface, a conditional-compilation exclusion), which reflection over the built assembly
///     would catch. The scan blanks <c>//</c> and <c>/* */</c> comments and captures each attribute's argument list up
///     to its own balanced closing parenthesis, so a documentation example and a ']' inside a string argument (for
///     example inside <c>UrlFormat</c>) are both handled; it does not also mask string and character literals, so a
///     comment marker that a literal argument contains is still misread as starting a real comment.
/// </remarks>
public sealed partial class ApiGateDiagnosticTests
{
	private const string UrlFormat = "https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/analyzers/docs/{0}.md";

	[Fact]
	public void Every_api_gate_attribute_uses_a_CESDK_id_and_the_documentation_url_format()
	{
		List<ApiGate> gates = ApiGates();
		List<string> offenders = [];
		foreach (ApiGate gate in gates)
		{
			string expectedRange = gate.Kind == ApiGateKind.Experimental ? "^CESDK5[0-9]{3}$" : "^CESDK[0-9]{4}$";
			if (!Regex.IsMatch(gate.Id, expectedRange, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
			{
				offenders.Add($"{gate.File}: {gate.Kind} id '{gate.Id}' is outside {expectedRange}.");
			}

			if (!string.Equals(gate.UrlFormat, UrlFormat, StringComparison.Ordinal))
			{
				offenders.Add($"{gate.File}: {gate.Kind}('{gate.Id}') has UrlFormat '{gate.UrlFormat}', expected '{UrlFormat}'.");
			}
		}

		Assert.Contains(gates, static gate => string.Equals(gate.Id, "CESDK5010", StringComparison.Ordinal));
		Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
	}

	[Fact]
	public void Every_api_gate_id_has_a_documentation_page_and_an_index_row()
	{
		string index = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "analyzers", "docs", "README.md"));
		List<string> offenders = [];
		foreach (string id in ApiGates().Select(static gate => gate.Id).Distinct(StringComparer.Ordinal))
		{
			string page = Path.Combine(RepositoryRoot.Path, "analyzers", "docs", id + ".md");
			if (!File.Exists(page))
			{
				offenders.Add($"analyzers/docs/{id}.md is missing.");
			}
			else if (!File.ReadAllText(page).StartsWith("# " + id + ":", StringComparison.Ordinal))
			{
				offenders.Add($"analyzers/docs/{id}.md does not start with '# {id}:'.");
			}

			if (!index.Contains($"| [{id}]({id}.md) |", StringComparison.Ordinal))
			{
				offenders.Add($"analyzers/docs/README.md has no table row for {id}.");
			}
		}

		Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
	}

	[Fact]
	public void The_attribute_scanner_ignores_comments_and_reads_multi_line_arguments()
	{
		string[] sample =
		[
			"/// <c>[Experimental(\"CESDK5999\")]</c> in documentation is not a declaration.",
			"// [Obsolete(\"text\", DiagnosticId = \"CESDK7999\")]",
			"[MainThreadOnly, Experimental(\"CESDK5998\",",
			"    UrlFormat = \"https://example.invalid/{0}\")]",
			"[System.Obsolete(\"Use the new API.\", DiagnosticId = \"CESDK7998\", UrlFormat = \"u\")]",
			"[Obsolete(\"No diagnostic id.\")]"
		];

		List<ApiGate> gates = Scan("libs/Sample.cs", sample);

		Assert.Equal(
			["Experimental CESDK5998 https://example.invalid/{0}", "Obsolete CESDK7998 u"],
			gates.Select(static gate => $"{gate.Kind} {gate.Id} {gate.UrlFormat}"), StringComparer.Ordinal);
	}

	// A block comment can span several lines and does not start with "//" on every one of them; a naive per-line "//"
	// check (the scanner's previous shape) never blanks it.
	[Fact]
	public void The_attribute_scanner_ignores_a_multi_line_block_comment()
	{
		string[] sample =
		[
			"/* An earlier design considered",
			"   [Experimental(\"CESDK5997\")] here. */",
			"[Experimental(\"CESDK5996\")]"
		];

		List<ApiGate> gates = Scan("libs/Sample.cs", sample);

		Assert.Equal(["Experimental CESDK5996 "], gates.Select(static gate => $"{gate.Kind} {gate.Id} {gate.UrlFormat}"),
			StringComparer.Ordinal);
	}

	// The argument list is captured up to its own balanced closing parenthesis, not up to the first ']'; a UrlFormat
	// (or any other string argument) that happens to contain ']' must not truncate the match early.
	[Fact]
	public void The_attribute_scanner_keeps_a_closing_bracket_inside_a_string_argument()
	{
		string[] sample = ["[Experimental(\"CESDK5995\", UrlFormat = \"https://example.invalid/{0}]tail\")]"];

		List<ApiGate> gates = Scan("libs/Sample.cs", sample);

		Assert.Equal(["Experimental CESDK5995 https://example.invalid/{0}]tail"],
			gates.Select(static gate => $"{gate.Kind} {gate.Id} {gate.UrlFormat}"), StringComparer.Ordinal);
	}

	private static List<ApiGate> ApiGates()
	{
		List<ApiGate> gates = [];
		foreach (string file in RepositoryRoot.EnumerateSourceFiles("*.cs"))
		{
			if (file.StartsWith("libs/", StringComparison.Ordinal) || file.StartsWith("src/", StringComparison.Ordinal))
			{
				gates.AddRange(Scan(file, File.ReadAllLines(Path.Combine(RepositoryRoot.Path, file))));
			}
		}

		return gates;
	}

	private static List<ApiGate> Scan(string file, string[] lines)
	{
		string code = BlankComments(string.Join("\n", lines));
		List<ApiGate> gates = [];
		foreach (Match match in GateAttribute().Matches(code))
		{
			string arguments = match.Groups["arguments"].Value;
			string url = UrlFormatArgument().Match(arguments) is { Success: true } urlMatch
				? urlMatch.Groups["value"].Value
				: string.Empty;
			if (string.Equals(match.Groups["name"].Value, "Experimental", StringComparison.Ordinal))
			{
				Match id = FirstStringArgument().Match(arguments);
				gates.Add(new ApiGate(file, ApiGateKind.Experimental, id.Success ? id.Groups["value"].Value : "", url));
			}
			else if (DiagnosticIdArgument().Match(arguments) is { Success: true } idMatch)
			{
				gates.Add(new ApiGate(file, ApiGateKind.Obsolete, idMatch.Groups["value"].Value, url));
			}
		}

		return gates;
	}

	// Blanks "//" line comments and "/* */" block comments (which need not start at column 0 or fit on one line),
	// keeping every line break so that line numbers in offender messages still line up with the source. A regular
	// double-quoted string is skipped verbatim first, because every gate's UrlFormat argument is one and contains its
	// own "//" (e.g. "https://github.com/..."), which must never be misread as a comment start. This project
	// deliberately has no ProjectReference (see the type doc comment), so the scan reads source text, never a built or
	// packed assembly; it does not also recognise verbatim, raw or interpolated strings or character literals
	// (shared-contracts section 3.2 is reported as a deviation), none of which this repository's gate declarations use.
	private static string BlankComments(string source)
	{
		char[] blanked = source.ToCharArray();
		int index = 0;
		while (index < blanked.Length)
		{
			if (blanked[index] == '"')
			{
				index++;
				while (index < blanked.Length && blanked[index] != '"' && blanked[index] != '\n')
				{
					index += blanked[index] == '\\' && index + 1 < blanked.Length && blanked[index + 1] != '\n' ? 2 : 1;
				}

				if (index < blanked.Length && blanked[index] == '"')
				{
					index++;
				}
			}
			else if (blanked[index] == '/' && index + 1 < blanked.Length && blanked[index + 1] == '/')
			{
				while (index < blanked.Length && blanked[index] != '\n')
				{
					blanked[index] = ' ';
					index++;
				}
			}
			else if (blanked[index] == '/' && index + 1 < blanked.Length && blanked[index + 1] == '*')
			{
				int closing = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
				int end = closing < 0 ? blanked.Length : closing + 2;
				for (; index < end; index++)
				{
					if (blanked[index] != '\n')
					{
						blanked[index] = ' ';
					}
				}
			}
			else
			{
				index++;
			}
		}

		return new string(blanked);
	}

	// An attribute of an attribute list: '[' or ',' before the name, then the argument list up to its own balanced
	// closing parenthesis (so a ']' inside a string argument, such as a UrlFormat, cannot truncate the match early),
	// followed by ']' or ','.
	[GeneratedRegex(
		@"[\[,]\s*(?:System\.Diagnostics\.CodeAnalysis\.|System\.)?(?<name>Experimental|Obsolete)(?:Attribute)?\s*\((?<arguments>(?>[^()]+|\((?<depth>)|\)(?<-depth>))*)(?(depth)(?!))\)\s*[\],]",
		RegexOptions.CultureInvariant | RegexOptions.Singleline, matchTimeoutMilliseconds: 1000)]
	private static partial Regex GateAttribute();

	[GeneratedRegex(@"^\s*""(?<value>[^""]*)""", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex FirstStringArgument();

	[GeneratedRegex(@"DiagnosticId\s*=\s*""(?<value>[^""]*)""", RegexOptions.CultureInvariant,
		matchTimeoutMilliseconds: 1000)]
	private static partial Regex DiagnosticIdArgument();

	[GeneratedRegex(@"UrlFormat\s*=\s*""(?<value>[^""]*)""", RegexOptions.CultureInvariant,
		matchTimeoutMilliseconds: 1000)]
	private static partial Regex UrlFormatArgument();

	private enum ApiGateKind
	{
		Unknown = 0,
		Experimental,
		Obsolete
	}

	private sealed record ApiGate(string File, ApiGateKind Kind, string Id, string UrlFormat);
}
