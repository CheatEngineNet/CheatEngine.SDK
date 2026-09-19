using CESDK.SourceGenerators.EngineApi.Parsing;
using CESDK.SourceGenerators.EngineApi.Tests.Infrastructure;
using CESDK.SourceGenerators.Shared.LuaEmit;

namespace CESDK.SourceGenerators.EngineApi.Tests.Parsing;

/// <summary>
///     Dependency-free tests of <see cref="SpecFileParser" />: no Roslyn type is touched anywhere in this class, which is
///     exactly what being dependency-free to parse inside a netstandard2.0 generator means in practice.
/// </summary>
public sealed class SpecFileParserTests
{
    [Fact]
    public void IsSpecFile_matches_the_convention_extension_case_insensitively()
    {
        Assert.True(SpecFileParser.IsSpecFile("memory-scalars.cesdk-api.txt"));
        Assert.True(SpecFileParser.IsSpecFile(@"C:\repo\Specs\memory-scalars.CESDK-API.TXT"));
        Assert.False(SpecFileParser.IsSpecFile("notes.txt"));
        Assert.False(SpecFileParser.IsSpecFile("memory.cesdk-api.txt.bak"));
        Assert.False(SpecFileParser.IsSpecFile(null));
    }

    [Fact]
    public void Nominal_spec_parses_into_four_sorted_calls_and_two_shared_free_cache_fields()
    {
        var spec = SpecFileParser.Parse("memory-scalars.cesdk-api.txt", SpecSources.Memory);

        Assert.Empty(spec.Issues.AsSpan().ToArray());
        Assert.Equal("Demo.Engine.Generated", spec.Namespace);
        Assert.Equal("MemoryScalars", spec.TypeName);
        Assert.Equal(4, spec.Calls.Length);
        Assert.Equal(["readInteger", "readQword", "writeInteger", "writeQword"], spec.CachedGlobals.AsSpan().ToArray());

        // Sorted by C# method name (ordinal).
        string[] methodNames = [.. spec.Calls.AsSpan().ToArray().Select(static c => c.Call.MethodName)];
        Assert.Equal(["TryReadInt32", "TryReadInt64", "WriteInt32", "WriteInt64"], methodNames);

        var tryReadInt32 = spec.Calls[0];
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

        var writeInt32 = spec.Calls[2];
        Assert.Equal(LuaCallForm.Throwing, writeInt32.Call.Form);
        Assert.Equal(LuaValueKind.Boolean, Assert.NotNull(writeInt32.Call.ReturnKind));
        Assert.Empty(writeInt32.Call.Results.AsSpan().ToArray());
    }

    [Fact]
    public void Two_forms_of_the_same_global_share_one_cache_field()
    {
        var spec = SpecFileParser.Parse("shared.cesdk-api.txt", SpecSources.SharedGlobal);

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

                            # a comment before an entry
                            global: readInteger
                            # a comment between entry keys
                            method: TryReadInt32
                            form: try
                            arg: address:address
                            result: value:int32
                            doc: Reads an integer.
                            # a trailing comment
                            """;

        var spec = SpecFileParser.Parse("x.cesdk-api.txt", Text);

        Assert.Empty(spec.Issues.AsSpan().ToArray());
        Assert.Single(spec.Calls.AsSpan().ToArray());
    }

    [Fact]
    public void Indentation_and_CRLF_line_endings_are_tolerated()
    {
        var text = SpecSources.SingleTry.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\n", "\r\n", StringComparison.Ordinal);
        text = "  namespace: Demo.One\r\n  type: One\r\n\r\n" +
               text[text.IndexOf("global:", StringComparison.Ordinal)..];

        var spec = SpecFileParser.Parse("x.cesdk-api.txt", text);

        Assert.Empty(spec.Issues.AsSpan().ToArray());
        Assert.Equal("Demo.One", spec.Namespace);
        Assert.Single(spec.Calls.AsSpan().ToArray());
    }

    [Fact]
    public void Empty_text_produces_one_issue_and_no_output()
    {
        var spec = SpecFileParser.Parse("empty.cesdk-api.txt", string.Empty);

        Assert.Equal(string.Empty, spec.Namespace);
        Assert.Equal(string.Empty, spec.TypeName);
        Assert.Empty(spec.Calls.AsSpan().ToArray());
        Assert.Single(spec.Issues.AsSpan().ToArray());
    }

    [Fact]
    public void A_comment_only_file_is_treated_as_empty()
    {
        var spec = SpecFileParser.Parse("x.cesdk-api.txt", "# nothing here\n# still nothing\n");

        Assert.Empty(spec.Calls.AsSpan().ToArray());
        Assert.Single(spec.Issues.AsSpan().ToArray());
    }

    [Fact]
    public void The_global_namespace_is_written_as_an_empty_namespace_value()
    {
        const string Text = """
                            namespace:
                            type: Root

                            global: readInteger
                            method: TryReadInt32
                            form: try
                            arg: address:address
                            result: value:int32
                            doc: Reads an integer.
                            """;

        var spec = SpecFileParser.Parse("x.cesdk-api.txt", Text);

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

                            global readInteger
                            method: TryReadInt32
                            form: try
                            arg: address:address
                            result: value:int32
                            doc: Reads an integer.
                            """;

        var spec = SpecFileParser.Parse("x.cesdk-api.txt", Text);

        Assert.Empty(spec.Calls.AsSpan().ToArray());
        Assert.Contains(spec.Issues,
            static issue => issue.Message.Contains("Malformed line", StringComparison.Ordinal));
    }

    [Fact]
    public void A_malformed_header_line_drops_the_whole_file()
    {
        const string Text = "namespace Demo\ntype: T\n";

        var spec = SpecFileParser.Parse("x.cesdk-api.txt", Text);

        Assert.Equal(string.Empty, spec.Namespace);
        Assert.Empty(spec.Calls.AsSpan().ToArray());
        Assert.NotEmpty(spec.Issues.AsSpan().ToArray());
    }

    [Fact]
    public void A_duplicate_header_key_fails_the_header()
    {
        const string Text = "namespace: Demo\nnamespace: Other\ntype: T\n";

        var spec = SpecFileParser.Parse("x.cesdk-api.txt", Text);

        Assert.Equal(string.Empty, spec.Namespace);
        Assert.Contains(spec.Issues,
            static issue => issue.Message.Contains("Duplicate header key", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unknown_header_key_fails_the_header()
    {
        const string Text = "namespace: Demo\ntype: T\nauthor: someone\n";

        var spec = SpecFileParser.Parse("x.cesdk-api.txt", Text);

        Assert.Equal(string.Empty, spec.Namespace);
        Assert.Contains(spec.Issues,
            static issue => issue.Message.Contains("Unknown header key", StringComparison.Ordinal));
    }

    [Fact]
    public void A_missing_type_key_fails_the_header()
    {
        var spec = SpecFileParser.Parse("x.cesdk-api.txt", "namespace: Demo\n");

        Assert.Empty(spec.Calls.AsSpan().ToArray());
        Assert.Contains(spec.Issues, static issue => issue.Message.Contains("'type'", StringComparison.Ordinal));
    }

    [Fact]
    public void An_invalid_namespace_fails_the_header()
    {
        var spec = SpecFileParser.Parse("x.cesdk-api.txt", "namespace: 1Bad.Name\ntype: T\n");

        Assert.Contains(spec.Issues,
            static issue => issue.Message.Contains("not a valid namespace", StringComparison.Ordinal));
    }

    [Fact]
    public void An_invalid_type_name_fails_the_header()
    {
        var spec = SpecFileParser.Parse("x.cesdk-api.txt", "namespace: Demo\ntype: 1Bad\n");

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
        var entry = """
                    global: readInteger
                    method: TryReadInt32
                    form: try
                    arg: address:address
                    result: value:int32
                    doc: Reads an integer.
                    """;

        var edited = string.Join(
            '\n',
            entry.Split('\n').Where(line => !line.StartsWith(missingKey + ":", StringComparison.Ordinal)));

        var spec = SpecFileParser.Parse("x.cesdk-api.txt", "namespace: Demo\ntype: T\n\n" + edited);

        Assert.Empty(spec.Calls.AsSpan().ToArray());
        Assert.Contains(spec.Issues, issue => issue.Message.Contains("'" + missingKey + "'", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unknown_entry_key_drops_the_entry()
    {
        const string Text = """
                            namespace: Demo
                            type: T

                            global: readInteger
                            method: TryReadInt32
                            form: try
                            arg: address:address
                            result: value:int32
                            doc: Reads an integer.
                            extra: nonsense
                            """;

        var spec = SpecFileParser.Parse("x.cesdk-api.txt", Text);

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

                            global: readInteger
                            global: writeInteger
                            method: TryReadInt32
                            form: try
                            arg: address:address
                            result: value:int32
                            doc: Reads an integer.
                            """;

        var spec = SpecFileParser.Parse("x.cesdk-api.txt", Text);

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
        var text = "namespace: Demo\ntype: T\n\nglobal: " + badName +
                   "\nmethod: M\nform: try\narg: address:address\nresult: value:int32\ndoc: d.\n";

        var spec = SpecFileParser.Parse("x.cesdk-api.txt", text);

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

                            global: readInteger
                            method: 1Bad
                            form: try
                            arg: address:address
                            result: value:int32
                            doc: d.
                            """;

        var spec = SpecFileParser.Parse("x.cesdk-api.txt", Text);

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

                            global: readInteger
                            method: class
                            form: try
                            arg: address:address
                            result: value:int32
                            doc: d.
                            """;

        var spec = SpecFileParser.Parse("x.cesdk-api.txt", Text);

        Assert.Empty(spec.Issues.AsSpan().ToArray());
        Assert.Equal("@class", spec.Calls[0].Call.MethodName);
    }

    [Fact]
    public void An_invalid_form_value_drops_the_entry()
    {
        const string Text = """
                            namespace: Demo
                            type: T

                            global: readInteger
                            method: M
                            form: maybe
                            arg: address:address
                            result: value:int32
                            doc: d.
                            """;

        var spec = SpecFileParser.Parse("x.cesdk-api.txt", Text);

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

                            global: readInteger
                            method: M
                            form: try
                            arg: address:address
                            doc: d.
                            """;

        var spec = SpecFileParser.Parse("x.cesdk-api.txt", Text);

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

                            global: readInteger
                            method: M
                            form: try
                            arg: address:address
                            result: value:int32
                            return: int32
                            doc: d.
                            """;

        var spec = SpecFileParser.Parse("x.cesdk-api.txt", Text);

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

                            global: writeInteger
                            method: M
                            form: throwing
                            arg: address:address
                            result: value:int32
                            doc: d.
                            """;

        var spec = SpecFileParser.Parse("x.cesdk-api.txt", Text);

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

                            global: beep
                            method: Beep
                            form: throwing
                            doc: Calls a global with no arguments and no result.
                            """;

        var spec = SpecFileParser.Parse("x.cesdk-api.txt", Text);

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
        var text = "namespace: Demo\ntype: T\n\nglobal: readInteger\nmethod: M\nform: try\n" + argLine +
                   "\nresult: value:int32\ndoc: d.\n";

        var spec = SpecFileParser.Parse("x.cesdk-api.txt", text);

        Assert.Empty(spec.Calls.AsSpan().ToArray());
        Assert.NotEmpty(spec.Issues.AsSpan().ToArray());
    }

    [Theory]
    [InlineData("fixed: boolean:maybe")]
    [InlineData("fixed: int32:1")]
    [InlineData("fixed: boolean:true; System.Console.WriteLine()")]
    public void A_fixed_argument_accepts_only_boolean_literals(string fixedLine)
    {
        var text = "namespace: Demo\ntype: T\n\nglobal: readInteger\nmethod: M\nform: try\narg: address:address\n" +
                   fixedLine + "\nresult: value:int32\ndoc: d.\n";

        var spec = SpecFileParser.Parse("x.cesdk-api.txt", text);

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

                            global: readInteger
                            method: M
                            form: try
                            arg: address:address
                            result: value:utf8
                            doc: d.
                            """;

        var spec = SpecFileParser.Parse("x.cesdk-api.txt", Text);

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

                            global: readInteger
                            method: M
                            form: throwing
                            arg: address:address
                            return: utf8
                            doc: d.
                            """;

        var spec = SpecFileParser.Parse("x.cesdk-api.txt", Text);

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

                            global: readInteger
                            method: M
                            form: throwing
                            arg: address:address
                            return: notakind
                            doc: d.
                            """;

        var spec = SpecFileParser.Parse("x.cesdk-api.txt", Text);

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

                            global: upper
                            method: Upper
                            form: throwing
                            arg: text:string?
                            return: string
                            doc: Upper-cases a string.
                            """;

        var spec = SpecFileParser.Parse("x.cesdk-api.txt", Text);

        Assert.Empty(spec.Issues.AsSpan().ToArray());
        var argument = spec.Calls[0].Call.Arguments[0];
        Assert.Equal(LuaValueKind.String, argument.Kind);
        Assert.True(argument.IsNullable);
    }

    [Fact]
    public void A_duplicate_method_name_drops_every_entry_that_uses_it_with_one_issue_per_line()
    {
        const string Text = """
                            namespace: Demo
                            type: T

                            global: readInteger
                            method: M
                            form: try
                            arg: address:address
                            result: value:int32
                            doc: first.

                            global: readQword
                            method: M
                            form: try
                            arg: address:address
                            result: value:int64
                            doc: second.
                            """;

        var spec = SpecFileParser.Parse("x.cesdk-api.txt", Text);

        Assert.Empty(spec.Calls.AsSpan().ToArray());
        Assert.Empty(spec.CachedGlobals.AsSpan().ToArray());
        var duplicateIssues = spec.Issues.AsSpan().ToArray().Count(static issue =>
            issue.Message.Contains("Duplicate method name", StringComparison.Ordinal));
        Assert.Equal(2, duplicateIssues);
    }

    [Fact]
    public void An_invalid_entry_does_not_prevent_other_entries_from_being_emitted()
    {
        const string Text = """
                            namespace: Demo
                            type: T

                            global: notAName!
                            method: Bad
                            form: try
                            arg: address:address
                            result: value:int32
                            doc: bad.

                            global: readInteger
                            method: TryReadInt32
                            form: try
                            arg: address:address
                            result: value:int32
                            doc: good.
                            """;

        var spec = SpecFileParser.Parse("x.cesdk-api.txt", Text);

        Assert.Single(spec.Calls.AsSpan().ToArray());
        Assert.Equal("TryReadInt32", spec.Calls[0].Call.MethodName);
        Assert.NotEmpty(spec.Issues.AsSpan().ToArray());
    }

    [Fact]
    public void The_hint_name_is_empty_until_SpecFiles_assigns_it()
    {
        var spec = SpecFileParser.Parse("x.cesdk-api.txt", SpecSources.SingleTry);

        Assert.Equal(string.Empty, spec.HintName);
    }
}
