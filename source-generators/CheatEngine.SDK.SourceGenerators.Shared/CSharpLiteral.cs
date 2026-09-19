using System;
using System.Text;

namespace CheatEngine.SDK.SourceGenerators.Shared;

/// <summary>
///     Turns arbitrary text into C# string literals: regular (<c>"..."</c>) and UTF-8 (<c>"..."u8</c>).
/// </summary>
/// <remarks>
///     <para>
///         The output is pure printable ASCII, so it does not depend on the encoding of the generated file and cannot be
///         broken by characters that the C# lexer treats as line terminators inside a regular literal (U+0085, U+2028,
///         U+2029). Rules: <c>"</c> and <c>\</c> get a backslash; NUL, BEL, BS, FF, LF, CR, TAB and VT use their short
///         escapes; every other character outside U+0020..U+007E becomes <c>\uXXXX</c>; a valid surrogate pair becomes one
///         <c>\UXXXXXXXX</c>. The variable-length <c>\x</c> escape is never produced (it would swallow following hex
///         digits).
///     </para>
///     <para>
///         Unpaired surrogates: a UTF-16 literal keeps them (<c>\uD800</c> is legal there). A UTF-8 literal cannot encode
///         them (the compiler rejects it, CS9026), so <see cref="AppendUtf8Literal" /> writes U+FFFD instead, which is
///         exactly what <see cref="Encoding.UTF8" /> does at run time. The bytes of the emitted <c>u8</c> literal
///         therefore
///         always equal <c>Encoding.UTF8.GetBytes(value)</c>.
///     </para>
///     <para>Formatting is culture-invariant (hex digits come from a fixed table).</para>
/// </remarks>
internal static class CSharpLiteral
{
    private const string HexDigits = "0123456789ABCDEF";
    private const char ReplacementCharacter = '\uFFFD';

    /// <summary>Returns <paramref name="value" /> as a regular C# string literal, quotes included.</summary>
    public static string ToStringLiteral(string value)
    {
        StringBuilder builder = new(GuessCapacity(value));
        AppendStringLiteral(builder, value);
        return builder.ToString();
    }

    /// <summary>Returns <paramref name="value" /> as a C# UTF-8 string literal (<c>"..."u8</c>).</summary>
    public static string ToUtf8Literal(string value)
    {
        StringBuilder builder = new(GuessCapacity(value) + 2);
        AppendUtf8Literal(builder, value);
        return builder.ToString();
    }

    /// <summary>Appends <paramref name="value" /> as a regular C# string literal, quotes included.</summary>
    public static void AppendStringLiteral(StringBuilder builder, string value)
    {
        AppendQuoted(builder, value, false);
    }

    /// <summary>Appends <paramref name="value" /> as a C# UTF-8 string literal (<c>"..."u8</c>).</summary>
    public static void AppendUtf8Literal(StringBuilder builder, string value)
    {
        AppendQuoted(builder, value, true);
        builder.Append("u8");
    }

    private static int GuessCapacity(string value)
    {
        return value is null ? 2 : value.Length + 8;
    }

    private static void AppendQuoted(StringBuilder builder, string value, bool replaceUnpairedSurrogates)
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));

        if (value is null) throw new ArgumentNullException(nameof(value));

        builder.Append('"');
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                AppendHexEscape(builder, 'U', char.ConvertToUtf32(c, value[i + 1]), 8);
                i++;
            }
            else if (char.IsSurrogate(c))
            {
                AppendHexEscape(builder, 'u', replaceUnpairedSurrogates ? ReplacementCharacter : c, 4);
            }
            else
            {
                AppendCharacter(builder, c);
            }
        }

        builder.Append('"');
    }

    private static void AppendCharacter(StringBuilder builder, char c)
    {
        switch (c)
        {
            case '"':
                builder.Append("\\\"");
                break;
            case '\\':
                builder.Append("\\\\");
                break;
            case '\0':
                builder.Append("\\0");
                break;
            case '\a':
                builder.Append("\\a");
                break;
            case '\b':
                builder.Append("\\b");
                break;
            case '\f':
                builder.Append("\\f");
                break;
            case '\n':
                builder.Append("\\n");
                break;
            case '\r':
                builder.Append("\\r");
                break;
            case '\t':
                builder.Append("\\t");
                break;
            case '\v':
                builder.Append("\\v");
                break;
            default:
                if (c is >= ' ' and <= '~')
                    builder.Append(c);
                else
                    AppendHexEscape(builder, 'u', c, 4);

                break;
        }
    }

    private static void AppendHexEscape(StringBuilder builder, char kind, int codePoint, int digits)
    {
        builder.Append('\\').Append(kind);
        for (var shift = (digits - 1) * 4; shift >= 0; shift -= 4)
            builder.Append(HexDigits[(codePoint >> shift) & 0xF]);
    }
}
