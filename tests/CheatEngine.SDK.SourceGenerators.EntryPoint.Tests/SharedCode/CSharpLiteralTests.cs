using System.Text;
using CheatEngine.SDK.SourceGenerators.Shared;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.SharedCode;

public sealed class CSharpLiteralTests
{
    // Values with unpaired surrogates are kept out of theory data: they do not survive test-case serialisation.
    private static readonly string[] RoundTripValues =
    [
        string.Empty,
        "plain ASCII ~!@#$%^&*()_+-=[]{};':,./<>?|`",
        "quote \" backslash \\ both \\\"",
        "\0\a\b\f\n\r\t\v",
        "nul before digit \01",
        "\u0001\u001B\u001F\u007F\u0080\u0085\u009F\u00A0",
        "\u2028\u2029\uFEFF\uFFFD\uFFFF",
        "Caf\u00E9 \u65E5\u672C\u8A9E \u0416",
        "\U0001F600\U0001D11E\U0010FFFF",
        "lone high \uD800 lone low \uDC00 reversed \uDE00\uD83D end \uD83D"
    ];

    [Theory]
    [InlineData("", "\"\"")]
    [InlineData("abc", "\"abc\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    [InlineData("a\\b", "\"a\\\\b\"")]
    [InlineData("\n\r\t", "\"\\n\\r\\t\"")]
    [InlineData("\u00E9", "\"\\u00E9\"")]
    [InlineData("\U0001F600", "\"\\U0001F600\"")]
    [InlineData("\u2028", "\"\\u2028\"")]
    public void ToStringLiteral_escapes_to_printable_ascii(string value, string expected)
    {
        Assert.Equal(expected, CSharpLiteral.ToStringLiteral(value));
    }

    [Fact]
    public void ToStringLiteral_keeps_unpaired_surrogates_as_escapes()
    {
        Assert.Equal("\"\\uD800x\\uDC00\"", CSharpLiteral.ToStringLiteral("\uD800x\uDC00"));
    }

    [Fact]
    public void ToUtf8Literal_replaces_unpaired_surrogates_with_the_replacement_character()
    {
        Assert.Equal("\"\\uFFFDx\\uFFFD\"u8", CSharpLiteral.ToUtf8Literal("\uD800x\uDC00"));
    }

    [Fact]
    public void ToUtf8Literal_appends_the_u8_suffix()
    {
        Assert.Equal("\"name\"u8", CSharpLiteral.ToUtf8Literal("name"));
    }

    [Fact]
    public void ToStringLiteral_output_is_lexed_back_to_the_same_value_by_the_compiler()
    {
        foreach (var value in RoundTripValues)
        {
            var literal = CSharpLiteral.ToStringLiteral(value);

            Assert.All(literal, static c => Assert.InRange(c, ' ', '~'));
            var expression = Assert.IsType<LiteralExpressionSyntax>(SyntaxFactory.ParseExpression(literal));
            Assert.Empty(expression.GetDiagnostics());
            Assert.Equal(SyntaxKind.StringLiteralExpression, expression.Kind());
            Assert.Equal(value, expression.Token.ValueText);
        }
    }

    [Fact]
    public void ToUtf8Literal_output_is_lexed_back_to_the_utf8_encoding_of_the_value()
    {
        foreach (var value in RoundTripValues)
        {
            var literal = CSharpLiteral.ToUtf8Literal(value);

            Assert.All(literal, static c => Assert.InRange(c, ' ', '~'));
            var expression = Assert.IsType<LiteralExpressionSyntax>(
                SyntaxFactory.ParseExpression(literal, options: new CSharpParseOptions(LanguageVersion.CSharp14)));
            Assert.Empty(expression.GetDiagnostics());
            Assert.Equal(SyntaxKind.Utf8StringLiteralExpression, expression.Kind());

            // Same bytes as the runtime encoder, replacement of unpaired surrogates included.
            Assert.Equal(Encoding.UTF8.GetBytes(value), Encoding.UTF8.GetBytes(expression.Token.ValueText));
        }
    }

    [Fact]
    public void Append_overloads_write_into_the_given_builder()
    {
        StringBuilder builder = new("x = ");

        CSharpLiteral.AppendStringLiteral(builder, "a");
        builder.Append(", ");
        CSharpLiteral.AppendUtf8Literal(builder, "b");

        Assert.Equal("x = \"a\", \"b\"u8", builder.ToString());
    }

    [Fact]
    public void Null_arguments_throw()
    {
        Assert.Throws<ArgumentNullException>(() => CSharpLiteral.ToStringLiteral(null!));
        Assert.Throws<ArgumentNullException>(() => CSharpLiteral.ToUtf8Literal(null!));
        Assert.Throws<ArgumentNullException>(() => CSharpLiteral.AppendStringLiteral(null!, "a"));
    }
}
