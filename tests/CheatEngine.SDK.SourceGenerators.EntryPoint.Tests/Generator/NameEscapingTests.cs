using System.Text;

using CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.Infrastructure;

using Microsoft.CodeAnalysis.CSharp;

namespace CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.Generator;

/// <summary>
///     The display name travels from an attribute argument into a <c>u8</c> literal. For every case: the literal text is
///     the expected one, the generated file compiles clean, and the bytes the compiled factory returns equal
///     <c>Encoding.UTF8.GetBytes(name)</c>.
/// </summary>
public sealed class NameEscapingTests(RoslynFixture roslyn) : IClassFixture<RoslynFixture>
{
	// Key -> (display name, expected line fragment in the generated file). Looked up by key so that theory rows stay
	// plain ASCII identifiers (some names contain unpaired surrogates, which do not survive test-case serialisation).
	private static readonly Dictionary<string, (string Name, string ExpectedFragment)> Cases =
		new(StringComparer.Ordinal)
		{
			["plain"] = ("Demo Plugin 1.0", """Utf8Name => "Demo Plugin 1.0"u8;"""),
			["quotes"] = ("Say \"hi\"", """Utf8Name => "Say \"hi\""u8;"""),
			["triple quotes"] = ("\"\"\"raw\"\"\"", """Utf8Name => "\"\"\"raw\"\"\""u8;"""),
			["backslashes"] = (@"C:\Tools\CE\", """Utf8Name => "C:\\Tools\\CE\\"u8;"""),
			["backslash before quote"] = ("a\\\"b", """Utf8Name => "a\\\"b"u8;"""),
			["escape look-alike"] = (@"\n is not a newline, \u0041 is not A",
				"""Utf8Name => "\\n is not a newline, \\u0041 is not A"u8;"""),
			["line breaks and tab"] = ("line1\r\nline2\ttab", """Utf8Name => "line1\r\nline2\ttab"u8;"""),
			["short escapes"] = ("\0\a\b\f\v", """Utf8Name => "\0\a\b\f\v"u8;"""),
			["nul before digit"] = ("a\01", """Utf8Name => "a\01"u8;"""),
			["control characters"] = ("\u0001\u001F\u007F\u0085\u009F",
				"""Utf8Name => "\u0001\u001F\u007F\u0085\u009F"u8;"""),
			["unicode line separators"] = ("a\u2028b\u2029c", """Utf8Name => "a\u2028b\u2029c"u8;"""),
			["latin accents"] = ("Caf\u00E9 \u00DCber", """Utf8Name => "Caf\u00E9 \u00DCber"u8;"""),
			["cjk"] = ("\u65E5\u672C\u8A9E", """Utf8Name => "\u65E5\u672C\u8A9E"u8;"""),
			["surrogate pair"] = ("\U0001F600 plugin \U0001D11E", """Utf8Name => "\U0001F600 plugin \U0001D11E"u8;"""),
			["byte order mark"] = ("\uFEFFname", """Utf8Name => "\uFEFFname"u8;"""),
			["lone high surrogate"] = ("bad\uD800end", """Utf8Name => "bad\uFFFDend"u8;"""),
			["lone low surrogate"] = ("\uDC00start", """Utf8Name => "\uFFFDstart"u8;"""),
			["reversed surrogates"] = ("x\uDE00\uD83Dy", """Utf8Name => "x\uFFFD\uFFFDy"u8;"""),
			["trailing high surrogate"] = ("end\uD83D", """Utf8Name => "end\uFFFD"u8;"""),
			["csharp punctuation"] = ("{braces} $dollar @at 'single' // comment /* block */",
				"""Utf8Name => "{braces} $dollar @at 'single' // comment /* block */"u8;"""),
			["surrounding spaces"] = ("  padded  ", """Utf8Name => "  padded  "u8;""")
		};

	public static TheoryData<string> CaseKeys => [.. Cases.Keys];

	[Theory]
	[MemberData(nameof(CaseKeys))]
	public void Generator_display_name_is_escaped_into_a_u8_literal(string caseKey)
	{
		(string name, string expectedFragment) = Cases[caseKey];

		// Roslyn's own literal formatter writes the attribute argument: independent of the escaper under test.
		GeneratorRun run = roslyn.Run(PluginSources.WithNameExpression(SymbolDisplay.FormatLiteral(name, true)));

		string generated = run.SingleGeneratedText;
		Assert.Contains("> " + expectedFragment + "\n", generated, StringComparison.Ordinal);

		// Holds because the plugin type of these cases (Demo.DemoPlugin) is ASCII: whatever the display name is, the
		// literal adds no other character. Identifiers are written as declared (PluginLocationTests, non-ASCII case).
		Assert.All(generated,
			static c => Assert.True(c is '\n' or >= ' ' and <= '~',
				$"Non-ASCII character U+{(int) c:X4} in generated text."));
		run.AssertCompilesClean();
	}

	[Theory]
	[MemberData(nameof(CaseKeys))]
	public void Generator_display_name_bytes_equal_the_utf8_encoding_of_the_name(string caseKey)
	{
		(string name, _) = Cases[caseKey];
		GeneratorRun run = roslyn.Run(PluginSources.WithNameExpression(SymbolDisplay.FormatLiteral(name, true)));
		using LoadedBootstrap bootstrap = LoadedBootstrap.Load(roslyn.Environment, run.OutputCompilation);

		Assert.Equal(1, bootstrap.Initialize(IntPtr.Zero, 0));

		// Encoding.UTF8 replaces unpaired surrogates with U+FFFD, which is the documented behaviour of the literal too.
		Assert.Equal(Encoding.UTF8.GetBytes(name), bootstrap.LastUtf8Name);
	}

	[Fact]
	public void Generator_verbatim_and_raw_string_arguments_are_read_by_value()
	{
		GeneratorRun verbatim = roslyn.Run(PluginSources.WithNameExpression("@\"C:\\dir \"\"quoted\"\" \""));
		GeneratorRun raw = roslyn.Run(PluginSources.WithNameExpression("\"\"\"C:\\dir \"quoted\" \"\"\""));

		const string Expected = """Utf8Name => "C:\\dir \"quoted\" "u8;""";
		Assert.Contains(Expected, verbatim.SingleGeneratedText, StringComparison.Ordinal);
		Assert.Contains(Expected, raw.SingleGeneratedText, StringComparison.Ordinal);
		verbatim.AssertCompilesClean();
		raw.AssertCompilesClean();
	}
}
