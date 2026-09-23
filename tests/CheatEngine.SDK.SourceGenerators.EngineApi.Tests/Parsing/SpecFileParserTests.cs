using CheatEngine.SDK.SourceGenerators.EngineApi.Model;
using CheatEngine.SDK.SourceGenerators.EngineApi.Parsing;
using CheatEngine.SDK.SourceGenerators.EngineApi.Tests.Infrastructure;
using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

namespace CheatEngine.SDK.SourceGenerators.EngineApi.Tests.Parsing;

/// <summary>
///     Dependency-free tests of <see cref="SpecFileParser" />: no Roslyn type is touched anywhere in this class, which is
///     exactly what being dependency-free to parse inside a netstandard2.0 generator means in practice.
/// </summary>
public sealed class SpecFileParserTests
{
	[Fact]
	public void IsSpecFile_matches_the_convention_extension_case_insensitively()
	{
		Assert.True(SpecFileParser.IsSpecFile("memory-scalars.cheatengine-sdk-api.txt"));
		Assert.True(SpecFileParser.IsSpecFile(@"C:\repo\Specs\memory-scalars.CHEATENGINE-SDK-API.TXT"));
		Assert.False(SpecFileParser.IsSpecFile("notes.txt"));
		Assert.False(SpecFileParser.IsSpecFile("memory.cheatengine-sdk-api.txt.bak"));
		Assert.False(SpecFileParser.IsSpecFile(null));
	}

	[Fact]
	public void Nominal_spec_parses_into_four_sorted_calls_and_two_shared_free_cache_fields()
	{
		SpecFileModel spec = SpecFileParser.Parse("memory-scalars.cheatengine-sdk-api.txt", SpecSources.Memory);

		Assert.Empty(spec.Issues.AsSpan().ToArray());
		Assert.Equal("Demo.Engine.Generated", spec.Namespace);
		Assert.Equal("MemoryScalars", spec.TypeName);
		Assert.Equal(4, spec.Calls.Length);
		Assert.Equal(["readInteger", "readQword", "writeInteger", "writeQword"], spec.CachedGlobals.AsSpan().ToArray());

		// Sorted by C# method name (ordinal).
		string[] methodNames = [.. spec.Calls.AsSpan().ToArray().Select(static c => c.Call.MethodName)];
		Assert.Equal(["TryReadInt32", "TryReadInt64", "WriteInt32", "WriteInt64"], methodNames);

		SpecCallModel tryReadInt32 = spec.Calls[0];
		Assert.Equal("readInteger", tryReadInt32.Call.GlobalName);
		Assert.Equal(LuaCallForm.Try, tryReadInt32.Call.Form);
		Assert.Equal("public static", tryReadInt32.Call.Modifiers);
		Assert.Equal(2, tryReadInt32.Call.Arguments.Length);
		Assert.Equal(LuaValueKind.Address, tryReadInt32.Call.Arguments[0].Kind);
		Assert.True(tryReadInt32.Call.Arguments[1].IsFixed);
		Assert.Equal(LuaValueKind.Boolean, tryReadInt32.Call.Arguments[1].Kind);
		Assert.Equal("true", tryReadInt32.Call.Arguments[1].FixedValue);
		Assert.Single(tryReadInt32.Call.Results.AsSpan().ToArray());
		Assert.Equal(LuaValueKind.Int32, tryReadInt32.Call.Results[0].Kind);
		Assert.StartsWith("Reads a 32-bit integer", tryReadInt32.Summary, StringComparison.Ordinal);

		SpecCallModel writeInt32 = spec.Calls[2];
		Assert.Equal(LuaCallForm.Throwing, writeInt32.Call.Form);
		Assert.Equal(LuaValueKind.Boolean, Assert.NotNull(writeInt32.Call.ReturnKind));
		Assert.Empty(writeInt32.Call.Results.AsSpan().ToArray());
	}

	[Fact]
	public void Two_forms_of_the_same_global_share_one_cache_field()
	{
		SpecFileModel spec = SpecFileParser.Parse("shared.cheatengine-sdk-api.txt", SpecSources.SharedGlobal);

		Assert.Empty(spec.Issues.AsSpan().ToArray());
		Assert.Equal(2, spec.Calls.Length);
		Assert.Equal(["readInteger"], spec.CachedGlobals.AsSpan().ToArray());
		Assert.Equal(spec.Calls[0].Call.CacheFieldName, spec.Calls[1].Call.CacheFieldName);
	}

	[Fact]
	public void Comments_and_blank_lines_are_ignored_wherever_they_appear()
	{
		const string Text = """
		                    # a file comment
		                    namespace: Demo
		                    # a comment between header keys
		                    type: T
		                    contract: ce77
		                    # a comment between contract keys
		                    provenance: ExactInstalledFile: CE 7.7 celua.txt test fixture
		                    minimum-ce: 7.7.0.10621
		                    architecture: x64
		                    thread: unknown
		                    ownership: none

		                    # a comment before an entry
		                    global: readInteger
		                    # a comment between entry keys
		                    method: TryReadInt32
		                    form: try
		                    arg: address:address
		                    result: value:int32
		                    nil: none
		                    doc: Reads an integer.
		                    # a trailing comment
		                    """;

		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", Text);

		Assert.Empty(spec.Issues.AsSpan().ToArray());
		Assert.Single(spec.Calls.AsSpan().ToArray());
	}

	[Fact]
	public void Indentation_and_CRLF_line_endings_are_tolerated()
	{
		string text = SpecSources.SingleTry.Replace("\r\n", "\n", StringComparison.Ordinal)
			.Replace("\n", "\r\n", StringComparison.Ordinal);
		text = "  namespace: Demo.One\r\n  type: One\r\n" +
			   SpecSources.Ce77.Replace("\n", "\r\n    ", StringComparison.Ordinal) + "\r\n" +
			   text[text.IndexOf("global:", StringComparison.Ordinal)..];

		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", text);

		Assert.Empty(spec.Issues.AsSpan().ToArray());
		Assert.Equal("Demo.One", spec.Namespace);
		Assert.Single(spec.Calls.AsSpan().ToArray());
	}

	[Fact]
	public void Empty_text_produces_one_issue_and_no_output()
	{
		SpecFileModel spec = SpecFileParser.Parse("empty.cheatengine-sdk-api.txt", string.Empty);

		Assert.Equal(string.Empty, spec.Namespace);
		Assert.Equal(string.Empty, spec.TypeName);
		Assert.Empty(spec.Calls.AsSpan().ToArray());
		Assert.Single(spec.Issues.AsSpan().ToArray());
	}

	[Fact]
	public void A_comment_only_file_is_treated_as_empty()
	{
		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", "# nothing here\n# still nothing\n");

		Assert.Empty(spec.Calls.AsSpan().ToArray());
		Assert.Single(spec.Issues.AsSpan().ToArray());
	}

	[Fact]
	public void The_global_namespace_is_written_as_an_empty_namespace_value()
	{
		const string Text = """
		                    namespace:
		                    type: Root
		                    contract: ce77
		                    provenance: ExactInstalledFile: CE 7.7 celua.txt test fixture
		                    minimum-ce: 7.7.0.10621
		                    architecture: x64
		                    thread: unknown
		                    ownership: none

		                    global: readInteger
		                    method: TryReadInt32
		                    form: try
		                    arg: address:address
		                    result: value:int32
		                    nil: none
		                    doc: Reads an integer.
		                    """;

		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", Text);

		Assert.Empty(spec.Issues.AsSpan().ToArray());
		Assert.Equal(string.Empty, spec.Namespace);
		Assert.Equal("Root", spec.TypeName);
	}

	[Fact]
	public void A_line_without_a_colon_marks_its_whole_block_malformed()
	{
		const string Text = """
		                    namespace: Demo
		                    type: T
		                    contract: ce77
		                    provenance: ExactInstalledFile: CE 7.7 celua.txt test fixture
		                    minimum-ce: 7.7.0.10621
		                    architecture: x64
		                    thread: unknown
		                    ownership: none

		                    global readInteger
		                    method: TryReadInt32
		                    form: try
		                    arg: address:address
		                    result: value:int32
		                    nil: none
		                    doc: Reads an integer.
		                    """;

		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", Text);

		Assert.Empty(spec.Calls.AsSpan().ToArray());
		Assert.Contains(spec.Issues,
			static issue => issue.Message.Contains("Malformed line", StringComparison.Ordinal));
	}

	[Fact]
	public void A_malformed_header_line_drops_the_whole_file()
	{
		const string Text = "namespace Demo\ntype: T\n";

		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", Text);

		Assert.Equal(string.Empty, spec.Namespace);
		Assert.Empty(spec.Calls.AsSpan().ToArray());
		Assert.NotEmpty(spec.Issues.AsSpan().ToArray());
	}

	[Fact]
	public void A_duplicate_header_key_fails_the_header()
	{
		const string Text = "namespace: Demo\nnamespace: Other\ntype: T\n";

		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", Text);

		Assert.Equal(string.Empty, spec.Namespace);
		Assert.Contains(spec.Issues,
			static issue => issue.Message.Contains("Duplicate header key", StringComparison.Ordinal));
	}

	[Fact]
	public void An_unknown_header_key_fails_the_header()
	{
		const string Text = "namespace: Demo\ntype: T\nauthor: someone\n";

		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", Text);

		Assert.Equal(string.Empty, spec.Namespace);
		Assert.Contains(spec.Issues,
			static issue => issue.Message.Contains("Unknown header key", StringComparison.Ordinal));
	}

	[Fact]
	public void A_missing_type_key_fails_the_header()
	{
		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", "namespace: Demo\n");

		Assert.Empty(spec.Calls.AsSpan().ToArray());
		Assert.Contains(spec.Issues, static issue => issue.Message.Contains("'type'", StringComparison.Ordinal));
	}

	[Fact]
	public void An_invalid_namespace_fails_the_header()
	{
		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", "namespace: 1Bad.Name\ntype: T\n");

		Assert.Contains(spec.Issues,
			static issue => issue.Message.Contains("not a valid namespace", StringComparison.Ordinal));
	}

	[Fact]
	public void An_invalid_type_name_fails_the_header()
	{
		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", "namespace: Demo\ntype: 1Bad\n");

		Assert.Contains(spec.Issues,
			static issue => issue.Message.Contains("not a valid type name", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("global")]
	[InlineData("method")]
	[InlineData("form")]
	[InlineData("doc")]
	public void An_entry_missing_a_required_key_is_dropped(string missingKey)
	{
		string entry = """
		               global: readInteger
		               method: TryReadInt32
		               form: try
		               arg: address:address
		               result: value:int32
		               nil: none
		               doc: Reads an integer.
		               """;

		string edited = string.Join(
			'\n',
			entry.Split('\n').Where(line => !line.StartsWith(missingKey + ":", StringComparison.Ordinal)));

		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", SpecSources.Ce77Header("Demo", "T") + edited);

		Assert.Empty(spec.Calls.AsSpan().ToArray());
		Assert.Contains(spec.Issues, issue => issue.Message.Contains("'" + missingKey + "'", StringComparison.Ordinal));
	}

	[Fact]
	public void An_unknown_entry_key_drops_the_entry()
	{
		const string Text = """
		                    namespace: Demo
		                    type: T
		                    contract: ce77
		                    provenance: ExactInstalledFile: CE 7.7 celua.txt test fixture
		                    minimum-ce: 7.7.0.10621
		                    architecture: x64
		                    thread: unknown
		                    ownership: none

		                    global: readInteger
		                    method: TryReadInt32
		                    form: try
		                    arg: address:address
		                    result: value:int32
		                    nil: none
		                    doc: Reads an integer.
		                    extra: nonsense
		                    """;

		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", Text);

		Assert.Empty(spec.Calls.AsSpan().ToArray());
		Assert.Contains(spec.Issues,
			static issue => issue.Message.Contains("Unknown entry key", StringComparison.Ordinal));
	}

	[Fact]
	public void A_duplicate_entry_key_drops_the_entry()
	{
		const string Text = """
		                    namespace: Demo
		                    type: T
		                    contract: ce77
		                    provenance: ExactInstalledFile: CE 7.7 celua.txt test fixture
		                    minimum-ce: 7.7.0.10621
		                    architecture: x64
		                    thread: unknown
		                    ownership: none

		                    global: readInteger
		                    global: writeInteger
		                    method: TryReadInt32
		                    form: try
		                    arg: address:address
		                    result: value:int32
		                    nil: none
		                    doc: Reads an integer.
		                    """;

		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", Text);

		Assert.Empty(spec.Calls.AsSpan().ToArray());
		Assert.Contains(spec.Issues,
			static issue => issue.Message.Contains("Duplicate entry key", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("Not A Name")]
	[InlineData("1leading")]
	[InlineData("end")] // a Lua reserved word
	public void An_invalid_lua_global_name_drops_the_entry(string badName)
	{
		string text = "namespace: Demo\ntype: T\n" + SpecSources.Ce77 + "\nglobal: " + badName +
					  "\nmethod: M\nform: try\narg: address:address\nresult: value:int32\nnil: none\ndoc: d.\n";

		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", text);

		Assert.Empty(spec.Calls.AsSpan().ToArray());
		Assert.Contains(spec.Issues,
			static issue => issue.Message.Contains("not a valid Lua global name", StringComparison.Ordinal));
	}

	[Fact]
	public void An_invalid_method_name_drops_the_entry()
	{
		const string Text = """
		                    namespace: Demo
		                    type: T
		                    contract: ce77
		                    provenance: ExactInstalledFile: CE 7.7 celua.txt test fixture
		                    minimum-ce: 7.7.0.10621
		                    architecture: x64
		                    thread: unknown
		                    ownership: none

		                    global: readInteger
		                    method: 1Bad
		                    form: try
		                    arg: address:address
		                    result: value:int32
		                    nil: none
		                    doc: d.
		                    """;

		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", Text);

		Assert.Empty(spec.Calls.AsSpan().ToArray());
		Assert.Contains(spec.Issues,
			static issue => issue.Message.Contains("not a valid C# method name", StringComparison.Ordinal));
	}

	[Fact]
	public void A_method_name_that_is_a_reserved_word_is_escaped_with_at()
	{
		const string Text = """
		                    namespace: Demo
		                    type: T
		                    contract: ce77
		                    provenance: ExactInstalledFile: CE 7.7 celua.txt test fixture
		                    minimum-ce: 7.7.0.10621
		                    architecture: x64
		                    thread: unknown
		                    ownership: none

		                    global: readInteger
		                    method: class
		                    form: try
		                    arg: address:address
		                    result: value:int32
		                    nil: none
		                    doc: d.
		                    """;

		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", Text);

		Assert.Empty(spec.Issues.AsSpan().ToArray());
		Assert.Equal("@class", spec.Calls[0].Call.MethodName);
	}

	[Fact]
	public void An_invalid_form_value_drops_the_entry()
	{
		const string Text = """
		                    namespace: Demo
		                    type: T
		                    contract: ce77
		                    provenance: ExactInstalledFile: CE 7.7 celua.txt test fixture
		                    minimum-ce: 7.7.0.10621
		                    architecture: x64
		                    thread: unknown
		                    ownership: none

		                    global: readInteger
		                    method: M
		                    form: maybe
		                    arg: address:address
		                    result: value:int32
		                    nil: none
		                    doc: d.
		                    """;

		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", Text);

		Assert.Empty(spec.Calls.AsSpan().ToArray());
		Assert.Contains(spec.Issues,
			static issue => issue.Message.Contains("not a valid form", StringComparison.Ordinal));
	}

	[Fact]
	public void A_try_entry_with_no_result_is_dropped()
	{
		const string Text = """
		                    namespace: Demo
		                    type: T
		                    contract: ce77
		                    provenance: ExactInstalledFile: CE 7.7 celua.txt test fixture
		                    minimum-ce: 7.7.0.10621
		                    architecture: x64
		                    thread: unknown
		                    ownership: none

		                    global: readInteger
		                    method: M
		                    form: try
		                    arg: address:address
		                    nil: none
		                    doc: d.
		                    """;

		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", Text);

		Assert.Empty(spec.Calls.AsSpan().ToArray());
		Assert.Contains(spec.Issues,
			static issue => issue.Message.Contains("needs at least one 'result'", StringComparison.Ordinal));
	}

	[Fact]
	public void A_try_entry_with_a_return_is_dropped()
	{
		const string Text = """
		                    namespace: Demo
		                    type: T
		                    contract: ce77
		                    provenance: ExactInstalledFile: CE 7.7 celua.txt test fixture
		                    minimum-ce: 7.7.0.10621
		                    architecture: x64
		                    thread: unknown
		                    ownership: none

		                    global: readInteger
		                    method: M
		                    form: try
		                    arg: address:address
		                    result: value:int32
		                    return: int32
		                    nil: none
		                    doc: d.
		                    """;

		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", Text);

		Assert.Empty(spec.Calls.AsSpan().ToArray());
		Assert.Contains(spec.Issues,
			static issue => issue.Message.Contains("must not declare 'return'", StringComparison.Ordinal));
	}

	[Fact]
	public void A_throwing_entry_with_a_result_is_dropped()
	{
		const string Text = """
		                    namespace: Demo
		                    type: T
		                    contract: ce77
		                    provenance: ExactInstalledFile: CE 7.7 celua.txt test fixture
		                    minimum-ce: 7.7.0.10621
		                    architecture: x64
		                    thread: unknown
		                    ownership: none

		                    global: writeInteger
		                    method: M
		                    form: throwing
		                    arg: address:address
		                    result: value:int32
		                    nil: none
		                    doc: d.
		                    """;

		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", Text);

		Assert.Empty(spec.Calls.AsSpan().ToArray());
		Assert.Contains(spec.Issues,
			static issue => issue.Message.Contains("must not declare 'result'", StringComparison.Ordinal));
	}

	[Fact]
	public void A_throwing_entry_without_a_return_is_a_void_wrapper()
	{
		const string Text = """
		                    namespace: Demo
		                    type: T
		                    contract: ce77
		                    provenance: ExactInstalledFile: CE 7.7 celua.txt test fixture
		                    minimum-ce: 7.7.0.10621
		                    architecture: x64
		                    thread: unknown
		                    ownership: none

		                    global: beep
		                    method: Beep
		                    form: throwing
		                    nil: none
		                    doc: Calls a global with no arguments and no result.
		                    """;

		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", Text);

		Assert.Empty(spec.Issues.AsSpan().ToArray());
		Assert.Null(spec.Calls[0].Call.ReturnKind);
		Assert.Empty(spec.Calls[0].Call.Arguments.AsSpan().ToArray());
	}

	[Theory]
	[InlineData("arg: address:notakind")]
	[InlineData("arg: :address")]
	[InlineData("arg: address")]
	[InlineData("arg: 1bad:address")]
	public void A_malformed_or_unknown_kind_argument_drops_the_entry(string argLine)
	{
		string text = "namespace: Demo\ntype: T\n" + SpecSources.Ce77 + "\nglobal: readInteger\nmethod: M\nform: try\n" + argLine +
					  "\nresult: value:int32\nnil: none\ndoc: d.\n";

		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", text);

		Assert.Empty(spec.Calls.AsSpan().ToArray());
		Assert.NotEmpty(spec.Issues.AsSpan().ToArray());
	}

	[Theory]
	[InlineData("fixed: boolean:maybe")]
	[InlineData("fixed: int32:1")]
	[InlineData("fixed: boolean:true; System.Console.WriteLine()")]
	public void A_fixed_argument_accepts_only_boolean_literals(string fixedLine)
	{
		string text = "namespace: Demo\ntype: T\n" + SpecSources.Ce77 + "\nglobal: readInteger\nmethod: M\nform: try\narg: address:address\n" +
					  fixedLine + "\nresult: value:int32\nnil: none\ndoc: d.\n";

		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", text);

		Assert.Empty(spec.Calls.AsSpan().ToArray());
		Assert.Contains(spec.Issues,
			static issue => issue.Message.Contains("fixed argument", StringComparison.Ordinal));
	}

	[Fact]
	public void A_utf8_result_is_rejected_because_it_would_dangle()
	{
		const string Text = """
		                    namespace: Demo
		                    type: T
		                    contract: ce77
		                    provenance: ExactInstalledFile: CE 7.7 celua.txt test fixture
		                    minimum-ce: 7.7.0.10621
		                    architecture: x64
		                    thread: unknown
		                    ownership: none

		                    global: readInteger
		                    method: M
		                    form: try
		                    arg: address:address
		                    result: value:utf8
		                    nil: none
		                    doc: d.
		                    """;

		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", Text);

		Assert.Empty(spec.Calls.AsSpan().ToArray());
		Assert.Contains(spec.Issues,
			static issue => issue.Message.Contains("cannot be a result", StringComparison.Ordinal));
	}

	[Fact]
	public void A_utf8_return_is_rejected_because_it_would_dangle()
	{
		const string Text = """
		                    namespace: Demo
		                    type: T
		                    contract: ce77
		                    provenance: ExactInstalledFile: CE 7.7 celua.txt test fixture
		                    minimum-ce: 7.7.0.10621
		                    architecture: x64
		                    thread: unknown
		                    ownership: none

		                    global: readInteger
		                    method: M
		                    form: throwing
		                    arg: address:address
		                    return: utf8
		                    nil: none
		                    doc: d.
		                    """;

		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", Text);

		Assert.Empty(spec.Calls.AsSpan().ToArray());
		Assert.Contains(spec.Issues,
			static issue => issue.Message.Contains("cannot be a return type", StringComparison.Ordinal));
	}

	[Fact]
	public void An_invalid_return_kind_drops_the_entry()
	{
		const string Text = """
		                    namespace: Demo
		                    type: T
		                    contract: ce77
		                    provenance: ExactInstalledFile: CE 7.7 celua.txt test fixture
		                    minimum-ce: 7.7.0.10621
		                    architecture: x64
		                    thread: unknown
		                    ownership: none

		                    global: readInteger
		                    method: M
		                    form: throwing
		                    arg: address:address
		                    return: notakind
		                    nil: none
		                    doc: d.
		                    """;

		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", Text);

		Assert.Empty(spec.Calls.AsSpan().ToArray());
		Assert.Contains(spec.Issues,
			static issue => issue.Message.Contains("not a valid return kind", StringComparison.Ordinal));
	}

	[Fact]
	public void A_string_argument_may_be_declared_nullable()
	{
		const string Text = """
		                    namespace: Demo
		                    type: T
		                    contract: ce77
		                    provenance: ExactInstalledFile: CE 7.7 celua.txt test fixture
		                    minimum-ce: 7.7.0.10621
		                    architecture: x64
		                    thread: unknown
		                    ownership: none

		                    global: upper
		                    method: Upper
		                    form: throwing
		                    arg: text:string?
		                    return: string
		                    nil: none
		                    doc: Upper-cases a string.
		                    """;

		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", Text);

		Assert.Empty(spec.Issues.AsSpan().ToArray());
		LuaArgumentModel argument = spec.Calls[0].Call.Arguments[0];
		Assert.Equal(LuaValueKind.String, argument.Kind);
		Assert.True(argument.IsNullable);
	}

	[Fact]
	public void A_duplicate_method_name_drops_every_entry_that_uses_it_with_one_issue_per_line()
	{
		const string Text = """
		                    namespace: Demo
		                    type: T
		                    contract: ce77
		                    provenance: ExactInstalledFile: CE 7.7 celua.txt test fixture
		                    minimum-ce: 7.7.0.10621
		                    architecture: x64
		                    thread: unknown
		                    ownership: none

		                    global: readInteger
		                    method: M
		                    form: try
		                    arg: address:address
		                    result: value:int32
		                    nil: none
		                    doc: first.

		                    global: readQword
		                    method: M
		                    form: try
		                    arg: address:address
		                    result: value:int64
		                    nil: none
		                    doc: second.
		                    """;

		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", Text);

		Assert.Empty(spec.Calls.AsSpan().ToArray());
		Assert.Empty(spec.CachedGlobals.AsSpan().ToArray());
		int duplicateIssues = spec.Issues.AsSpan().ToArray().Count(static issue =>
			issue.Message.Contains("Duplicate method name", StringComparison.Ordinal));
		Assert.Equal(2, duplicateIssues);
	}

	[Fact]
	public void An_invalid_entry_does_not_prevent_other_entries_from_being_emitted()
	{
		const string Text = """
		                    namespace: Demo
		                    type: T
		                    contract: ce77
		                    provenance: ExactInstalledFile: CE 7.7 celua.txt test fixture
		                    minimum-ce: 7.7.0.10621
		                    architecture: x64
		                    thread: unknown
		                    ownership: none

		                    global: notAName!
		                    method: Bad
		                    form: try
		                    arg: address:address
		                    result: value:int32
		                    nil: none
		                    doc: bad.

		                    global: readInteger
		                    method: TryReadInt32
		                    form: try
		                    arg: address:address
		                    result: value:int32
		                    nil: none
		                    doc: good.
		                    """;

		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", Text);

		Assert.Single(spec.Calls.AsSpan().ToArray());
		Assert.Equal("TryReadInt32", spec.Calls[0].Call.MethodName);
		Assert.NotEmpty(spec.Issues.AsSpan().ToArray());
	}

	[Fact]
	public void The_hint_name_is_empty_until_SpecFiles_assigns_it()
	{
		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", SpecSources.SingleTry);

		Assert.Equal(string.Empty, spec.HintName);
	}

	/// <summary>A ce77 contract becomes immutable per-entry data rather than an ignored comment beside the spec.</summary>
	[Fact]
	public void A_ce77_contract_is_attached_to_every_valid_entry_with_its_nil_semantics()
	{
		SpecFileModel spec = SpecFileParser.Parse("memory.cheatengine-sdk-api.txt", SpecSources.Memory);

		Assert.Empty(spec.Issues.AsSpan().ToArray());
		Assert.Equal(4, spec.Calls.Length);
		Assert.Equal("7.7.0.10621", Assert.IsType<SpecFileContract>(spec.Contract).MinimumCheatEngineVersion);
		int absenceCount = 0;
		int noneCount = 0;
		foreach (SpecCallModel entry in spec.Calls)
		{
			SpecContract contract = Assert.IsType<SpecContract>(entry.Contract);
			Assert.Equal("ExactInstalledFile: CE 7.7 celua.txt scalar memory globals", contract.Provenance);
			Assert.Equal("7.7.0.10621", contract.MinimumCheatEngineVersion);
			Assert.Equal("x64", contract.Architecture);
			Assert.Equal("unknown", contract.ThreadAffinity);
			Assert.Equal("none", contract.Ownership);
			if (string.Equals(contract.NilSemantics, "absence", StringComparison.Ordinal))
			{
				absenceCount++;
			}

			if (string.Equals(contract.NilSemantics, "none", StringComparison.Ordinal))
			{
				noneCount++;
			}
		}

		Assert.Equal(2, absenceCount);
		Assert.Equal(2, noneCount);
	}

	/// <summary>Contract fields may not remain free-form comments: an invalid status is a localized grammar issue.</summary>
	[Fact]
	public void An_invalid_ce77_provenance_records_the_provenance_value_location()
	{
		const string Text =
			"namespace: Demo\ntype: T\ncontract: ce77\nprovenance: unverified note\nminimum-ce: 7.7.0.10621\narchitecture: x64\nthread: unknown\nownership: none\n";

		SpecFileModel spec = SpecFileParser.Parse("contract.cheatengine-sdk-api.txt", Text);

		SpecIssue issue = Assert.Single(spec.Issues);
		Assert.Equal(4, issue.Line);
		Assert.Equal(13, issue.Column);
		Assert.Contains("not a valid provenance", issue.Message, StringComparison.Ordinal);
	}

	/// <summary>Every independently malformed CE 7.7 header fact is retained as its own source-located issue.</summary>
	[Fact]
	public void Invalid_ce77_contract_facts_report_each_exact_value_location()
	{
		const string Text =
			"namespace: Demo\ntype: T\ncontract: ce77\nprovenance: not proof\nminimum-ce: seven\narchitecture: x86\nthread: worker\nownership: shared\n";

		SpecFileModel spec = SpecFileParser.Parse("contract.cheatengine-sdk-api.txt", Text);

		Assert.Equal(5, spec.Issues.Length);
		AssertIssue(spec, "not a valid provenance", 4, 13);
		AssertIssue(spec, "not a valid minimum CE version", 5, 13);
		AssertIssue(spec, "not a supported Engine API architecture", 6, 15);
		AssertIssue(spec, "not a valid thread contract", 7, 9);
		AssertIssue(spec, "not a valid ownership contract", 8, 12);
	}

	/// <summary>Parser issues preserve the exact value column from an indented additional-file field.</summary>
	[Fact]
	public void An_invalid_argument_kind_records_its_value_column()
	{
		const string Text =
			"namespace: Demo\ntype: T\n" + SpecSources.Ce77 + "\n    global: readInteger\n    method: M\n    form: try\n    arg: address:notakind\n    result: value:int32\n    nil: none\n    doc: d.\n";

		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", Text);

		SpecIssue issue = Assert.Single(spec.Issues);
		Assert.Equal(13, issue.Line);
		Assert.Equal(10, issue.Column);
	}

	/// <summary>Reserved implementation locals and hidden raw-core method identities cannot reach generated C#.</summary>
	[Fact]
	public void Parameter_and_generated_member_identity_collisions_drop_the_affected_entries()
	{
		const string Text =
			"namespace: Demo\ntype: T\n" + SpecSources.Ce77 + "\nglobal: readInteger\nmethod: BadParameter\nform: try\narg: __L:int32\nresult: value:int32\nnil: none\ndoc: bad.\n\nglobal: readInteger\nmethod: Read\nform: try\narg: address:address\nresult: value:int32\nnil: none\ndoc: raw core.\n\nglobal: readQword\nmethod: __ReadRaw\nform: try\nresult: value:int64\nnil: none\ndoc: collision.\n";

		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", Text);

		Assert.Empty(spec.Calls.AsSpan().ToArray());
		Assert.Contains(spec.Issues,
			static issue => issue.Message.Contains("reserved local", StringComparison.Ordinal));
		Assert.Contains(spec.Issues,
			static issue => issue.Message.Contains("Generated member", StringComparison.Ordinal));
	}

	[Fact]
	public void A_parameter_named_operation_is_rejected_as_an_emitter_local_collision()
	{
		const string Text =
			"namespace: Demo\ntype: T\n" + SpecSources.Ce77 + "\nglobal: readInteger\nmethod: BadOperation\nform: try\narg: __operation:int32\nresult: value:int32\nnil: none\ndoc: bad.\n";

		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", Text);

		Assert.Empty(spec.Calls.AsSpan().ToArray());
		SpecIssue issue = Assert.Single(spec.Issues.AsSpan().ToArray());
		Assert.Contains("__operation", issue.Message, StringComparison.Ordinal);
		Assert.Contains("reserved local", issue.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Spec_with_entries_without_contract_reports_the_missing_ce77_contract()
	{
		const string Legacy =
			"# a legacy fixture\nnamespace: Demo\ntype: T\n\nglobal: readInteger\nmethod: TryReadInt32\nform: try\narg: address:address\nresult: value:int32\ndoc: d.\n";

		SpecFileModel spec = SpecFileParser.Parse("legacy.cheatengine-sdk-api.txt", Legacy);

		Assert.Empty(spec.Calls.AsSpan().ToArray());
		SpecIssue issue = Assert.Single(spec.Issues);
		Assert.Equal(SpecIssueKind.MissingContract, issue.Kind);
		Assert.Equal(2, issue.Line);
		Assert.Equal(1, issue.Column);
		Assert.Contains("'contract: ce77'", issue.Message, StringComparison.Ordinal);

		// A header-only reservation stays readable without the contract: it generates nothing either way.
		SpecFileModel reservation = SpecFileParser.Parse("reservation.cheatengine-sdk-api.txt", "namespace: Demo\ntype: T\n");
		Assert.True(reservation.Issues.IsEmpty);
		Assert.Null(reservation.Contract);
	}

	[Fact]
	public void Fixed_and_optional_arguments_keep_their_declaration_order()
	{
		string text = SpecSources.Ce77Header("Demo", "T") +
					  "global: g\nmethod: G\nform: throwing\narg: a:int32\nfixed: boolean:true\narg: b:int64\nopt: c:address\nopt: d:string\nnil: none\ndoc: d.\n";

		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", text);

		Assert.True(spec.Issues.IsEmpty);
		LuaArgumentModel[] arguments = spec.Calls[0].Call.Arguments.AsSpan().ToArray();
		Assert.Equal(["a", "true", "b", "c", "d"], arguments.Select(static a => a.Name), StringComparer.Ordinal);
		Assert.Equal([false, true, false, false, false], arguments.Select(static a => a.IsFixed));
		Assert.Equal([false, false, false, true, true], arguments.Select(static a => a.IsOptional));
		Assert.Equal(LuaValueKind.Address, arguments[3].Kind);
		Assert.Equal("global::CheatEngine.SDK.Lua.Marshalling.LuaOptional<string>", arguments[4].GeneratedTypeName);
	}

	[Theory]
	[InlineData("opt: a:int32\narg: b:int32\n", "'b:int32' follows an 'opt' argument")]
	[InlineData("opt: a:int32\nfixed: boolean:true\n", "'boolean:true' follows an 'opt' argument")]
	[InlineData("opt: a:utf8\n", "'utf8' cannot be an 'opt' kind")]
	[InlineData("opt: a:string?\n", "'string?' cannot be an 'opt' kind")]
	public void Argument_after_an_optional_argument_is_refused(string arguments, string message)
	{
		string text = SpecSources.Ce77Header("Demo", "T") + "global: g\nmethod: G\nform: throwing\n" + arguments +
					  "nil: none\ndoc: d.\n";

		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", text);

		Assert.Empty(spec.Calls.AsSpan().ToArray());
		SpecIssue issue = Assert.Single(spec.Issues);
		Assert.Equal(SpecIssueKind.OptionalArgument, issue.Kind);
		Assert.Contains(message, issue.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("try", "opt-result: a:int32\nresult: b:int32\n", "'b:int32' is a required 'result' after an 'opt-result'")]
	[InlineData("outcome", "rest: a:int64\nresult: b:int32\n", "'b:int32' must be declared before the 'rest' result")]
	[InlineData("outcome", "opt-result: a:string?\n", "'string?' cannot be an 'opt-result' kind")]
	[InlineData("outcome", "rest: a:address\n", "'address' cannot be a 'rest' kind")]
	[InlineData("outcome", "rest: a:string\n", "'string' cannot be a 'rest' kind")]
	public void Optional_result_before_a_required_result_is_refused(string form, string results, string message)
	{
		string text = SpecSources.Ce77Header("Demo", "T") + "global: g\nmethod: G\nform: " + form + "\n" + results +
					  "nil: none\ndoc: d.\n";

		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", text);

		Assert.Empty(spec.Calls.AsSpan().ToArray());
		SpecIssue issue = Assert.Single(spec.Issues);
		Assert.Equal(SpecIssueKind.ResultShape, issue.Kind);
		Assert.Contains(message, issue.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Outcome_form_is_parsed()
	{
		string text = SpecSources.Ce77Header("Demo", "T") +
					  "global: g\nmethod: G\nform: outcome\narg: a:address\nresult: first:int64\nopt-result: second:address\nrest: values:double\nnil: absence\ndoc: d.\n\n" +
					  "global: beep\nmethod: Beep\nform: outcome\nnil: none\ndoc: d.\n";

		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", text);

		Assert.True(spec.Issues.IsEmpty);
		SpecCallModel beep = spec.Calls[0];
		Assert.Equal(LuaCallForm.Outcome, beep.Call.Form);
		Assert.True(beep.Call.Results.IsEmpty);
		LuaGlobalCallModel call = spec.Calls[1].Call;
		Assert.Equal(LuaCallForm.Outcome, call.Form);
		Assert.Equal([LuaResultShape.Value, LuaResultShape.Optional, LuaResultShape.Variadic],
			call.Results.AsSpan().ToArray().Select(static r => r.Shape));
		Assert.Equal("values", call.Results[2].DestinationName);
		Assert.Equal("valuesCount", call.Results[2].Name);
		Assert.True(call.HasDynamicResults);
	}

	[Fact]
	public void Rest_result_requires_the_outcome_form()
	{
		string text = SpecSources.Ce77Header("Demo", "T") +
					  "global: g\nmethod: TryG\nform: try\nresult: first:int32\nrest: values:int32\nnil: none\ndoc: d.\n";

		SpecFileModel spec = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", text);

		Assert.Empty(spec.Calls.AsSpan().ToArray());
		SpecIssue issue = Assert.Single(spec.Issues);
		Assert.Equal(SpecIssueKind.ResultShape, issue.Kind);
		Assert.Equal(14, issue.Line);
		Assert.Equal(7, issue.Column);
		Assert.Contains("only the 'outcome' form", issue.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Reformatting_a_spec_keeps_its_contract_and_call_model()
	{
		string compact = SpecSources.Ce77Header("Demo", "T") +
						 "global: g\nmethod: G\nform: outcome\narg: a:int32\nopt: b:address\nresult: r:int64\nopt-result: s:string\nnil: absence\ndoc: Reads.\n";
		string reformatted = "# reformatted\r\n  namespace:   Demo  \r\n\ttype: T\r\n" +
							 SpecSources.Ce77.Replace("\n", "\r\n  # a comment inside the header\r\n", StringComparison.Ordinal) +
							 "\r\n\r\n   # a comment before the entry\r\n  global:  g\r\n  method: G\r\n  # a comment inside the entry\r\n" +
							 "  form:outcome\r\n  arg:   a:int32\r\n  opt: b:address  \r\n  result: r:int64\r\n  opt-result: s:string\r\n" +
							 "  nil: absence\r\n  doc: Reads.\r\n\r\n";

		SpecFileModel first = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", compact);
		SpecFileModel second = SpecFileParser.Parse("x.cheatengine-sdk-api.txt", reformatted);

		Assert.True(first.Issues.IsEmpty);
		Assert.True(second.Issues.IsEmpty,
			string.Join(" | ", second.Issues.AsImmutableArray().Select(static i => i.Line + ": " + i.Message)));
		Assert.Equal(first.Contract, second.Contract);
		Assert.Equal(first.Calls[0].Call, second.Calls[0].Call);
		Assert.Equal(first.Calls[0].Contract, second.Calls[0].Contract);
		Assert.Equal(first.Calls[0].Summary, second.Calls[0].Summary);
	}

	private static void AssertIssue(SpecFileModel spec, string messageFragment, int line, int column)
	{
		SpecIssue issue = Assert.Single(spec.Issues.AsSpan().ToArray(), issue =>
			issue.Message.Contains(messageFragment, StringComparison.Ordinal));
		Assert.Equal(line, issue.Line);
		Assert.Equal(column, issue.Column);
	}
}
