using System.Text.RegularExpressions;

using CheatEngine.SDK.Repository.Tests.Infrastructure;

namespace CheatEngine.SDK.Repository.Tests.Diagnostics;

/// <summary>
///     The diagnostic identifiers that SDK product code declares on its own API through
///     <c>[Obsolete(DiagnosticId = "CESDKnnnn")]</c> and <c>[Experimental("CESDKnnnn")]</c>. The C# compiler raises them
///     in
///     consumer code, so, like the analyzer descriptors that <c>DiagnosticCatalogTests</c> covers, each one needs a
///     documentation page, a row in <c>analyzers/docs/README.md</c>, the repository help-link format and an identifier
///     from its reserved range (shared-contracts section 3.2: <c>5xxx</c> experimental gates, <c>7xxx</c> obsoletions).
/// </summary>
/// <remarks>
///     This project has no ProjectReference by design, so the attributes are read from the committed sources of
///     <c>libs/</c> and <c>src/</c> rather than by reflection over built assemblies. Only code is scanned: comments,
///     including XML documentation that quotes an attribute in a <c>&lt;c&gt;</c> element, and string or character
///     literals never count as a declaration. An attribute is found alone, inside an attribute list
///     (<c>[EditorBrowsable(...), Obsolete(...)]</c>) and after a target specifier (<c>[method: Obsolete(...)]</c>).
///     Because the scan reads text, an identifier must be written as a <c>"CESDKnnnn"</c> string literal: a constant or
///     any other expression is reported rather than skipped.
/// </remarks>
public sealed partial class ApiDiagnosticIdTests
{
	private const string HelpUrlFormat =
		"https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/analyzers/docs/{0}.md";

	/// <summary>
	///     Reviewed obsoletions outside the <c>7xxx</c> range: the legacy Lua registration pair shares its usage analyzer
	///     identifier (shared-contracts section 3.3, S-REG-ANALYZER, CESDK1006).
	/// </summary>
	private static readonly HashSet<string> s_reviewedObsoleteIdsOutsideTheRange =
		new(StringComparer.Ordinal) { "CESDK1006" };

	[Fact]
	public void the_source_scan_finds_the_declared_api_diagnostic_ids()
	{
		IReadOnlyList<ApiDiagnosticId> ids = ScanApiDiagnosticIds();

		Assert.Contains(ids, static id => id is { Kind: ApiDiagnosticKind.Obsolete, Id: "CESDK7001" } &&
		                                  string.Equals(id.File, "libs/CheatEngine.SDK.Engine/Runtime/PointerSize.cs",
			                                  StringComparison.Ordinal));
	}

	[Fact]
	public void every_obsolete_or_experimental_diagnostic_id_is_a_cesdk_string_literal()
	{
		List<string> offenders = [];
		foreach (ApiDiagnosticId id in ScanApiDiagnosticIds())
		{
			if (!id.IsLiteral)
			{
				offenders.Add($"{id.File}: {id.Kind} identifier '{id.Id}' is not a \"CESDKnnnn\" string literal.");
			}
		}

		Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
	}

	[Fact]
	public void every_obsolete_or_experimental_diagnostic_id_has_a_documentation_page_and_a_readme_row()
	{
		string readme = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "analyzers", "docs", "README.md"));
		List<string> offenders = [];
		foreach (ApiDiagnosticId id in ScanApiDiagnosticIds())
		{
			if (!id.IsLiteral)
			{
				// Reported by every_obsolete_or_experimental_diagnostic_id_is_a_cesdk_string_literal.
				continue;
			}

			string page = Path.Combine(RepositoryRoot.Path, "analyzers", "docs", id.Id + ".md");
			if (!File.Exists(page))
			{
				offenders.Add($"{id.File}: {id.Id} has no page analyzers/docs/{id.Id}.md.");
			}
			else if (!File.ReadAllText(page).StartsWith("# " + id.Id + ":", StringComparison.Ordinal))
			{
				offenders.Add($"analyzers/docs/{id.Id}.md does not start with '# {id.Id}:'.");
			}

			if (!readme.Contains($"| [{id.Id}]({id.Id}.md) |", StringComparison.Ordinal))
			{
				offenders.Add($"analyzers/docs/README.md has no table row for {id.Id}.");
			}
		}

		Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
	}

	[Fact]
	public void every_obsolete_or_experimental_diagnostic_id_uses_the_repository_help_url_format()
	{
		List<string> offenders = [];
		foreach (ApiDiagnosticId id in ScanApiDiagnosticIds())
		{
			if (!string.Equals(id.UrlFormat, HelpUrlFormat, StringComparison.Ordinal))
			{
				offenders.Add(
					$"{id.File}: {id.Id} has UrlFormat '{id.UrlFormat ?? "(none)"}', expected '{HelpUrlFormat}'.");
			}
		}

		Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
	}

	[Fact]
	public void obsolete_ids_are_in_the_7xxx_range_and_experimental_ids_in_the_5xxx_range()
	{
		List<string> offenders = [];
		foreach (ApiDiagnosticId id in ScanApiDiagnosticIds())
		{
			if (!id.IsLiteral)
			{
				// Reported by every_obsolete_or_experimental_diagnostic_id_is_a_cesdk_string_literal.
				continue;
			}

			char rangeDigit = id.Id["CESDK".Length];
			bool inRange = id.Kind == ApiDiagnosticKind.Obsolete
				? rangeDigit == '7' || s_reviewedObsoleteIdsOutsideTheRange.Contains(id.Id)
				: rangeDigit == '5';
			if (!inRange)
			{
				offenders.Add($"{id.File}: {id.Kind} id {id.Id} is outside its reserved range.");
			}
		}

		Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
	}

	[Fact]
	public void the_scan_reads_both_attribute_shapes_and_ignores_obsoletions_without_an_identifier()
	{
		const string Source = """
		                      [Obsolete("old", DiagnosticId = "CESDK7999", UrlFormat = "https://example.invalid/{0}")]
		                      public static void A() { }
		                      [Obsolete("no identifier")]
		                      public static void B() { }
		                      /// <summary>Gated by <c>[Experimental("CESDK5999")]</c> until a receipt exists.</summary>
		                      [Experimental("CESDK5999", UrlFormat = "https://example.invalid/{0}.md")]
		                      public static void C() { }
		                      [System.Diagnostics.CodeAnalysis.Experimental("CESDK5998")]
		                      public static void D() { }
		                      """;

		List<ApiDiagnosticId> ids = [.. Scan("sample.cs", Source)];

		Assert.Equal(3, ids.Count);
		Assert.Equal(
			new ApiDiagnosticId("sample.cs", ApiDiagnosticKind.Obsolete, "CESDK7999", "https://example.invalid/{0}"),
			ids[0]);
		Assert.Equal(new ApiDiagnosticId("sample.cs", ApiDiagnosticKind.Experimental, "CESDK5999",
			"https://example.invalid/{0}.md"), ids[1]);
		Assert.Equal(new ApiDiagnosticId("sample.cs", ApiDiagnosticKind.Experimental, "CESDK5998", null), ids[2]);
	}

	[Fact]
	public void the_scan_ignores_attribute_text_in_comments_and_literals_and_keeps_urls_that_contain_slashes()
	{
		const string Source = """"
		                      // [Obsolete("line comment", DiagnosticId = "CESDK7901")]
		                      /* [Experimental("CESDK5901")] spans
		                         [Obsolete("block comment", DiagnosticId = "CESDK7902")] */
		                      ///  <c>[Obsolete("documentation", DiagnosticId = "CESDK7903")]</c>
		                      private const string Regular = "[Experimental(\"CESDK5902\")]";
		                      private const string Verbatim = @"[Experimental(""CESDK5903"")] /* not a comment";
		                      private const string Raw = """
		                          [Obsolete("raw", DiagnosticId = "CESDK7904")] // not a comment
		                          """;
		                      [Obsolete("real", DiagnosticId = "CESDK7905", UrlFormat = "https://example.invalid/{0}.md")] // [Experimental("CESDK5905")]
		                      public static void Real() { }
		                      private const string Url = "https://example.invalid/"; [Experimental("CESDK5906", UrlFormat = "https://example.invalid/{0}")]
		                      private const char Quote = '"'; [Obsolete("after a character literal", DiagnosticId = "CESDK7906", UrlFormat = "u")]
		                      private static string F(int x) => $"{(x > 0 ? "[Experimental(\"CESDK5904\")]" : "")} // text"; [Experimental("CESDK5907", UrlFormat = "v")]
		                      """";

		ApiDiagnosticId[] expected =
		[
			new("sample.cs", ApiDiagnosticKind.Obsolete, "CESDK7905", "https://example.invalid/{0}.md"),
			new("sample.cs", ApiDiagnosticKind.Experimental, "CESDK5906", "https://example.invalid/{0}"),
			new("sample.cs", ApiDiagnosticKind.Obsolete, "CESDK7906", "u"),
			new("sample.cs", ApiDiagnosticKind.Experimental, "CESDK5907", "v")
		];

		Assert.Equal(expected, Scan("sample.cs", Source));
	}

	[Fact]
	public void an_attribute_quoted_in_a_string_does_not_hide_the_declaration_that_follows_it()
	{
		const string Source = """
		                      private const string Hint = "write [Obsolete(\"x\" here"; [Obsolete("real", DiagnosticId = "CESDK7907", UrlFormat = "w")]
		                      [Obsolete("message with f(x)] inside", DiagnosticId = "CESDK7908", UrlFormat = "x")]
		                      """;

		ApiDiagnosticId[] expected =
		[
			new("sample.cs", ApiDiagnosticKind.Obsolete, "CESDK7907", "w"),
			new("sample.cs", ApiDiagnosticKind.Obsolete, "CESDK7908", "x")
		];

		Assert.Equal(expected, Scan("sample.cs", Source));
	}

	[Fact]
	public void the_scan_reads_attribute_lists_and_targets_and_reports_identifiers_that_are_not_cesdk_literals()
	{
		const string Source = """
		                      [EditorBrowsable(EditorBrowsableState.Never), Obsolete("listed", DiagnosticId = "CESDK7909", UrlFormat = "a")]
		                      [method: Obsolete("targeted", DiagnosticId = "CESDK7910", UrlFormat = "b")]
		                      [return: global::System.Diagnostics.CodeAnalysis.ExperimentalAttribute("CESDK5908")]
		                      [Experimental("CESDK5909"), EditorBrowsable(EditorBrowsableState.Never)]
		                      [Obsolete(nameof(Old), DiagnosticId = "CESDK7911", UrlFormat = "c")]
		                      [Obsolete("constant", DiagnosticId = Ids.Old, UrlFormat = "d")]
		                      [Experimental(Ids.Gate)]
		                      [Obsolete("foreign", DiagnosticId = "SYSLIB0999")]
		                      private static int Call(int x) => Math.Max(x, Obsolete(x)) + Invoke(1, Experimental(2));
		                      """;

		ApiDiagnosticId[] expected =
		[
			new("sample.cs", ApiDiagnosticKind.Obsolete, "CESDK7909", "a"),
			new("sample.cs", ApiDiagnosticKind.Obsolete, "CESDK7910", "b"),
			new("sample.cs", ApiDiagnosticKind.Experimental, "CESDK5908", null),
			new("sample.cs", ApiDiagnosticKind.Experimental, "CESDK5909", null),
			new("sample.cs", ApiDiagnosticKind.Obsolete, "CESDK7911", "c"),
			new("sample.cs", ApiDiagnosticKind.Obsolete, "Ids.Old", "d", false),
			new("sample.cs", ApiDiagnosticKind.Experimental, "Ids.Gate", null, false),
			new("sample.cs", ApiDiagnosticKind.Obsolete, "\"SYSLIB0999\"", null, false)
		];

		Assert.Equal(expected, Scan("sample.cs", Source));
	}

	private static List<ApiDiagnosticId> ScanApiDiagnosticIds()
	{
		List<ApiDiagnosticId> ids = [];
		foreach (string file in RepositoryRoot.EnumerateSourceFiles("*.cs"))
		{
			if (!file.StartsWith("libs/", StringComparison.Ordinal) &&
			    !file.StartsWith("src/", StringComparison.Ordinal))
			{
				continue;
			}

			ids.AddRange(Scan(file, File.ReadAllText(Path.Combine(RepositoryRoot.Path, file))));
		}

		return ids;
	}

	private static IEnumerable<ApiDiagnosticId> Scan(string file, string source)
	{
		CodeMask mask = CodeMask.Create(source);
		string code = mask.Code;
		foreach (Match attribute in AttributePattern().Matches(mask.Structure))
		{
			Group argumentsGroup = attribute.Groups["arguments"];
			string arguments = code.Substring(argumentsGroup.Index, argumentsGroup.Length);
			bool experimental = attribute.Groups["name"].Value.EndsWith("Experimental", StringComparison.Ordinal);
			ApiDiagnosticKind kind = experimental ? ApiDiagnosticKind.Experimental : ApiDiagnosticKind.Obsolete;
			Match url = UrlFormatPattern().Match(arguments);
			string? urlFormat = url.Success ? url.Groups["url"].Value : null;
			Match id = experimental ? ExperimentalIdPattern().Match(arguments) : ObsoleteIdPattern().Match(arguments);
			if (id.Success)
			{
				yield return new ApiDiagnosticId(file, kind, id.Groups["id"].Value, urlFormat);
				continue;
			}

			// An [Experimental] always names an identifier; an [Obsolete] names one only through DiagnosticId.
			Match expression = experimental
				? ExperimentalExpressionPattern().Match(arguments)
				: ObsoleteExpressionPattern().Match(arguments);
			if (expression.Success)
			{
				yield return new ApiDiagnosticId(file, kind, expression.Groups["expression"].Value.Trim(), urlFormat,
					false);
			}
		}
	}

	// An attribute opens a list ("[", with an optional "target:" specifier) or follows a comma inside one, and is followed
	// by "]" or ",". The arguments are paren-balanced in the structure view, where literals hold no parenthesis.
	[GeneratedRegex(
		@"(?:\[\s*(?:[A-Za-z_][A-Za-z0-9_]*\s*:(?!:)\s*)?|,\s*)(?<name>(?:global::)?(?:System\.)?Obsolete|(?:global::)?(?:System\.Diagnostics\.CodeAnalysis\.)?Experimental)(?:Attribute)?\s*\((?<arguments>(?>[^()]+|\((?<depth>)|\)(?<-depth>))*)(?(depth)(?!))\)\s*(?=[\],])",
		RegexOptions.CultureInvariant, 1000)]
	private static partial Regex AttributePattern();

	[GeneratedRegex("""DiagnosticId\s*=\s*"(?<id>CESDK\d{4})"\s*""", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex ObsoleteIdPattern();

	[GeneratedRegex("""^\s*"(?<id>CESDK\d{4})"\s*""", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex ExperimentalIdPattern();

	[GeneratedRegex("""DiagnosticId\s*=\s*(?<expression>[^,]+)""", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex ObsoleteExpressionPattern();

	[GeneratedRegex("""^\s*(?<expression>[^,]+)""", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex ExperimentalExpressionPattern();

	[GeneratedRegex("""UrlFormat\s*=\s*"(?<url>[^"]*)"\s*""", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex UrlFormatPattern();

	private enum ApiDiagnosticKind
	{
		Unknown = 0,
		Obsolete = 1,
		Experimental = 2
	}

	/// <summary>One declared identifier; <c>Id</c> holds the expression text when it is not a CESDK string literal.</summary>
	private sealed record ApiDiagnosticId(
		string File,
		ApiDiagnosticKind Kind,
		string Id,
		string? UrlFormat,
		bool IsLiteral = true);

	/// <summary>
	///     Two same-length views of a C# source. <see cref="Code" /> blanks comments (<c>//</c>, <c>///</c>,
	///     <c>/* */</c>) and keeps literal text, because attribute arguments are literals and a <c>//</c> inside a
	///     <c>UrlFormat</c> string is not a comment. <see cref="Structure" /> also blanks brackets and parentheses inside
	///     string and character literals, so a pattern run over it can neither start nor end inside a literal. Regular,
	///     verbatim, raw and interpolated strings are recognised; code inside interpolation holes is lexed as code. Line
	///     breaks are kept, so positions and line numbers match the source.
	/// </summary>
	private sealed class CodeMask
	{
		private readonly char[] _code;
		private readonly string _source;
		private readonly char[] _structure;
		private int _position;

		private CodeMask(string source)
		{
			_source = source;
			_code = source.ToCharArray();
			_structure = source.ToCharArray();
		}

		/// <summary>The source with comments blanked.</summary>
		public string Code => new(_code);

		/// <summary>The source with comments, and brackets and parentheses inside literals, blanked.</summary>
		public string Structure => new(_structure);

		public static CodeMask Create(string source)
		{
			CodeMask mask = new(source);
			mask.ReadCode(false);
			return mask;
		}

		/// <summary>Reads code until the end, or, inside an interpolation hole, up to its closing brace.</summary>
		private void ReadCode(bool inHole)
		{
			int depth = 0;
			while (_position < _source.Length)
			{
				char current = _source[_position];
				if (current == '/' && Peek(1) == '/')
				{
					BlankLineComment();
				}
				else if (current == '/' && Peek(1) == '*')
				{
					BlankBlockComment();
				}
				else if (current == '\'')
				{
					ReadCharacterLiteral();
				}
				else if (!TryReadStringLiteral())
				{
					if (inHole)
					{
						if (current is '(' or '[' or '{')
						{
							depth++;
						}
						else if (current is ')' or ']' or '}' && depth > 0)
						{
							depth--;
						}
						else if (current == '}')
						{
							return;
						}
						else if (current == ':' && depth == 0)
						{
							// Format specifier: literal text up to the closing brace of the hole.
							while (_position < _source.Length && _source[_position] != '}')
							{
								ConsumeLiteral(1);
							}

							return;
						}
					}

					_position++;
				}
			}
		}

		private bool TryReadStringLiteral()
		{
			int index = _position;
			int dollars = 0;
			bool verbatim = false;
			while (index < _source.Length && _source[index] is '$' or '@')
			{
				if (_source[index] == '$')
				{
					dollars++;
				}
				else
				{
					verbatim = true;
				}

				index++;
			}

			if (index >= _source.Length || _source[index] != '"')
			{
				return false;
			}

			int quotes = CountRun(index, '"');
			if (!verbatim && quotes >= 3)
			{
				ConsumeLiteral(index - _position + quotes);
				ReadRawStringBody(quotes, dollars);
			}
			else if (!verbatim && quotes == 2)
			{
				ConsumeLiteral(index - _position + 2);
			}
			else
			{
				ConsumeLiteral(index - _position + 1);
				ReadQuotedStringBody(verbatim, dollars > 0);
			}

			return true;
		}

		private void ReadQuotedStringBody(bool verbatim, bool interpolated)
		{
			while (_position < _source.Length)
			{
				char current = _source[_position];
				if (!verbatim && current == '\n')
				{
					return;
				}

				if (!verbatim && current == '\\')
				{
					ConsumeLiteral(Math.Min(2, _source.Length - _position));
				}
				else if (verbatim && current == '"' && Peek(1) == '"')
				{
					ConsumeLiteral(2);
				}
				else if (current == '"')
				{
					ConsumeLiteral(1);
					return;
				}
				else if (!interpolated || !TryReadHole(1, false))
				{
					ConsumeLiteral(1);
				}
			}
		}

		private void ReadRawStringBody(int quotes, int dollars)
		{
			while (_position < _source.Length)
			{
				if (_source[_position] == '"')
				{
					int run = CountRun(_position, '"');
					ConsumeLiteral(run);
					if (run >= quotes)
					{
						return;
					}
				}
				else if (dollars == 0 || !TryReadHole(dollars, true))
				{
					ConsumeLiteral(1);
				}
			}
		}

		/// <summary>
		///     At an opening brace of an interpolated string: consumes escaped braces as text, or the hole with its code.
		///     A quoted string escapes a brace by doubling it; a raw string with <paramref name="dollars" /> dollar signs
		///     opens a hole with that many braces.
		/// </summary>
		private bool TryReadHole(int dollars, bool raw)
		{
			if (_source[_position] != '{')
			{
				return false;
			}

			int run = CountRun(_position, '{');
			bool opensHole = raw ? run >= dollars : run % 2 == 1;
			ConsumeLiteral(run);
			if (!opensHole)
			{
				return true;
			}

			ReadCode(true);
			ConsumeLiteral(Math.Min(raw ? dollars : 1, CountRun(_position, '}')));
			return true;
		}

		private void ReadCharacterLiteral()
		{
			ConsumeLiteral(1);
			while (_position < _source.Length && _source[_position] != '\n')
			{
				char current = _source[_position];
				ConsumeLiteral(current == '\\' ? Math.Min(2, _source.Length - _position) : 1);
				if (current == '\'')
				{
					return;
				}
			}
		}

		private void BlankLineComment()
		{
			while (_position < _source.Length && _source[_position] is not ('\r' or '\n'))
			{
				Blank(_position);
				_position++;
			}
		}

		private void BlankBlockComment()
		{
			Blank(_position);
			Blank(_position + 1);
			_position += 2;
			while (_position < _source.Length)
			{
				bool closes = _source[_position] == '*' && Peek(1) == '/';
				if (_source[_position] is not ('\r' or '\n'))
				{
					Blank(_position);
				}

				_position++;
				if (closes)
				{
					Blank(_position);
					_position++;
					return;
				}
			}
		}

		private void Blank(int index)
		{
			_code[index] = ' ';
			_structure[index] = ' ';
		}

		private void ConsumeLiteral(int count)
		{
			for (int end = _position + count; _position < end; _position++)
			{
				if (_source[_position] is '[' or ']' or '(' or ')')
				{
					_structure[_position] = ' ';
				}
			}
		}

		private int CountRun(int index, char character)
		{
			int end = index;
			while (end < _source.Length && _source[end] == character)
			{
				end++;
			}

			return end - index;
		}

		private char Peek(int offset)
		{
			return _position + offset < _source.Length ? _source[_position + offset] : '\0';
		}
	}
}
