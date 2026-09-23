using System.Text;

namespace CheatEngine.SDK.Repository.Tests.SourceScanning;

/// <summary>
///     Lexical helpers for C# source read as text: the repository tests reference no Roslyn package. Comments, string
///     literals (regular, verbatim, interpolated and raw, interpolation holes included) and character literals can be
///     blanked, so a rule that looks for identifiers never matches documentation or text.
/// </summary>
/// <remarks>
///     This is a heuristic lexer, not a C# parser, and it has two known limits for interpolated strings. First, the code
///     inside an interpolation hole is blanked with the literal, so an identifier used only there, such as a raw
///     <c>LuaApi</c> call in <c>$"{LuaApi.lua_gettop(state)}"</c>, is invisible to every scan built on this class.
///     Second, a non-raw interpolated string whose hole contains its own string literal (<c>$"{Name("x")}"</c>) ends at
///     the first nested quote, so the rest of that line is split at the wrong places. The first shape does not occur in
///     <c>libs/**</c> today. The second occurs once, in an error message of <c>LuaApi</c>, where the mis-split stays on
///     that line and exposes no identifier a rule looks for. A rule that must see such uses needs a Roslyn-based scan.
/// </remarks>
internal static class CSharpCode
{
	/// <summary>Replaces comments and literals by spaces, keeping every newline so line numbers do not move.</summary>
	internal static string BlankCommentsAndLiterals(string source)
	{
		StringBuilder output = new(source.Length);
		int index = 0;
		while (index < source.Length)
		{
			int end = EndOfCommentOrLiteral(source, index);
			if (end == index)
			{
				output.Append(source[index]);
				index++;
				continue;
			}

			foreach (char character in source.AsSpan(index, end - index))
			{
				output.Append(character == '\n' ? '\n' : ' ');
			}

			index = end;
		}

		return output.ToString();
	}

	// Returns the end (exclusive) of the comment or literal that starts at index, or index when none starts there.
	private static int EndOfCommentOrLiteral(string source, int index)
	{
		if (StartsWith(source, index, "//"))
		{
			int newline = source.IndexOf('\n', index);
			return newline < 0 ? source.Length : newline;
		}

		if (StartsWith(source, index, "/*"))
		{
			int close = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
			return close < 0 ? source.Length : close + 2;
		}

		int prefix = index;
		while (prefix < source.Length && source[prefix] is '$' or '@')
		{
			prefix++;
		}

		if (prefix < source.Length && source[prefix] == '"')
		{
			bool verbatim = source.AsSpan(index, prefix - index).Contains('@');
			return EndOfString(source, prefix, verbatim);
		}

		return prefix == index && source[index] == '\'' ? EndOfQuoted(source, index, '\'') : index;
	}

	private static int EndOfString(string source, int quote, bool verbatim)
	{
		int quotes = 0;
		while (quote + quotes < source.Length && source[quote + quotes] == '"')
		{
			quotes++;
		}

		if (quotes >= 3)
		{
			string delimiter = new('"', quotes);
			int close = source.IndexOf(delimiter, quote + quotes, StringComparison.Ordinal);
			return close < 0 ? source.Length : close + quotes;
		}

		return verbatim ? EndOfVerbatim(source, quote) : EndOfQuoted(source, quote, '"');
	}

	private static int EndOfVerbatim(string source, int quote)
	{
		int position = quote + 1;
		while (position < source.Length)
		{
			if (source[position] == '"')
			{
				if (position + 1 < source.Length && source[position + 1] == '"')
				{
					position += 2;
					continue;
				}

				return position + 1;
			}

			position++;
		}

		return source.Length;
	}

	private static int EndOfQuoted(string source, int open, char quote)
	{
		int position = open + 1;
		while (position < source.Length && source[position] != quote && source[position] != '\n')
		{
			position += source[position] == '\\' ? 2 : 1;
		}

		return Math.Min(position + 1, source.Length);
	}

	private static bool StartsWith(string source, int index, string value)
	{
		return string.CompareOrdinal(source, index, value, 0, value.Length) == 0;
	}

	/// <summary>The 1-based line of <paramref name="index" /> in <paramref name="text" />.</summary>
	internal static int LineOf(string text, int index)
	{
		int line = 1;
		for (int position = 0; position < index; position++)
		{
			if (text[position] == '\n')
			{
				line++;
			}
		}

		return line;
	}
}
