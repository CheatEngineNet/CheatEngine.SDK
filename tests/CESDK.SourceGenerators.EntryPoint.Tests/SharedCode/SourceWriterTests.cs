using System.Text;
using CESDK.SourceGenerators.Shared;

namespace CESDK.SourceGenerators.EntryPoint.Tests.SharedCode;

public sealed class SourceWriterTests
{
    [Fact]
    public void WriteLine_at_level_zero_writes_text_and_lf()
    {
        SourceWriter writer = new();

        writer.WriteLine("class C;");

        Assert.Equal("class C;\n", writer.ToString());
        Assert.Equal(9, writer.Length);
    }

    [Fact]
    public void Blocks_indent_by_four_spaces_per_level()
    {
        SourceWriter writer = new();

        writer.WriteLine("namespace N");
        writer.OpenBlock();
        writer.WriteLine("class C");
        writer.OpenBlock();
        writer.WriteLine("int _f;");
        writer.CloseBlock();
        writer.CloseBlock();

        Assert.Equal("namespace N\n{\n    class C\n    {\n        int _f;\n    }\n}\n", writer.ToString());
        Assert.Equal(0, writer.IndentLevel);
    }

    [Fact]
    public void Empty_line_inside_a_block_carries_no_trailing_whitespace()
    {
        SourceWriter writer = new();

        writer.OpenBlock();
        writer.WriteLine("a;");
        writer.WriteLine();
        writer.WriteLine("b;");
        writer.CloseBlock();

        Assert.Equal("{\n    a;\n\n    b;\n}\n", writer.ToString());
    }

    [Fact]
    public void Write_fragments_indent_only_the_start_of_the_line()
    {
        SourceWriter writer = new();

        writer.Indent();
        writer.Write("return ");
        writer.Write("42");
        writer.Write(';');
        writer.WriteLine();

        Assert.Equal("    return 42;\n", writer.ToString());
    }

    [Fact]
    public void Write_multi_line_text_reindents_every_line_and_normalises_crlf()
    {
        SourceWriter writer = new();

        writer.Indent();
        writer.Write("if (x)\r\n{\r\n    y();\n\n}");
        writer.WriteLine();

        Assert.Equal("    if (x)\n    {\n        y();\n\n    }\n", writer.ToString());
    }

    [Fact]
    public void Write_lone_carriage_return_is_a_line_break_like_for_the_csharp_lexer()
    {
        // A lone CR ends a line for the C# lexer: dropping it would glue "// comment\rcode();" into one comment,
        // keeping it would put a second kind of line ending into the output.
        SourceWriter writer = new();

        writer.Indent();
        writer.Write("// comment\rcode();\r\rdone();");

        Assert.Equal("    // comment\n    code();\n\n    done();", writer.ToString());
    }

    [Fact]
    public void Write_crlf_split_across_two_writes_is_one_line_break()
    {
        SourceWriter writer = new();

        writer.Write("a\r");
        writer.Write("\nb");
        writer.Write('\r');
        writer.Write('\n');
        writer.Write("c");

        Assert.Equal("a\nb\nc", writer.ToString());
    }

    [Fact]
    public void Write_carriage_return_character_leaves_no_indentation_behind()
    {
        SourceWriter writer = new();

        writer.Indent();
        writer.Write('\r');
        writer.Write("x");

        Assert.Equal("\n    x", writer.ToString());
    }

    [Fact]
    public void WriteLine_after_text_ending_in_carriage_return_adds_its_own_line_break()
    {
        // Same result as for text ending in '\n': the text ended a line, the call ends another one. The explicit
        // call also forgets the pending CR, so a following '\n' is a line break again.
        SourceWriter writer = new();

        writer.Write("a\r");
        writer.WriteLine();
        writer.Write("\nb");

        Assert.Equal("a\n\n\nb", writer.ToString());
    }

    [Fact]
    public void Output_never_contains_a_carriage_return()
    {
        SourceWriter writer = new();

        writer.WriteLine("a\r\nb\rc\n\rd\r");
        writer.Write('\r');

        Assert.DoesNotContain('\r', writer.ToString());
    }

    [Fact]
    public void Write_newline_character_ends_the_line()
    {
        SourceWriter writer = new();

        writer.Indent();
        writer.Write('a');
        writer.Write('\n');
        writer.Write('b');

        Assert.Equal("    a\n    b", writer.ToString());
    }

    [Fact]
    public void CloseBlock_with_suffix_appends_it_after_the_brace()
    {
        SourceWriter writer = new();

        writer.WriteLine("int[] values =");
        writer.OpenBlock();
        writer.WriteLine("1,");
        writer.CloseBlock(";");

        Assert.Equal("int[] values =\n{\n    1,\n};\n", writer.ToString());
    }

    [Fact]
    public void Unindent_below_zero_throws()
    {
        SourceWriter writer = new();

        Assert.Throws<InvalidOperationException>(writer.Unindent);
        Assert.Throws<InvalidOperationException>(writer.CloseBlock);
    }

    [Fact]
    public void Write_null_text_throws()
    {
        SourceWriter writer = new();

        Assert.Throws<ArgumentNullException>(() => writer.Write(null!));
    }

    [Fact]
    public void Clear_resets_text_and_indentation()
    {
        SourceWriter writer = new();
        writer.OpenBlock();
        writer.Write("pending");

        writer.Clear();
        writer.WriteLine("fresh");

        Assert.Equal("fresh\n", writer.ToString());
        Assert.Equal(0, writer.IndentLevel);
    }

    [Fact]
    public void ToSourceText_is_utf8_and_round_trips_the_text()
    {
        SourceWriter writer = new();
        writer.WriteLine("// caf\u00E9");

        var text = writer.ToSourceText();

        Assert.Equal(Encoding.UTF8, text.Encoding);
        Assert.Equal(writer.ToString(), text.ToString());
    }
}
