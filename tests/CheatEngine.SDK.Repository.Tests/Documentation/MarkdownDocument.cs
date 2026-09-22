using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace CheatEngine.SDK.Repository.Tests.Documentation;

/// <summary>
///     A dependency-free parser for the part of CommonMark and GitHub Flavored Markdown that the documentation integrity
///     tests need: inline links and images (including link text that spans lines), link reference definitions, raw HTML
///     <c>href</c>/<c>src</c> attributes, ATX headings with their GitHub anchors, explicit HTML anchors, and the regions
///     whose content is not markup (fenced code, code spans, HTML comments, backslash escapes).
/// </summary>
/// <remarks>
///     <para>
///         The parser is conservative where the specification is ambiguous for this use: a fence opener may be indented by
///         any amount (fences inside list items), indented code blocks are not treated as code because list continuations
///         use indentation too, and an unclosed inline HTML comment hides nothing. Setext headings are not recognised:
///         the repository writes ATX headings only.
///     </para>
///     <para>
///         Line endings may be <c>\r\n</c> or <c>\n</c>. Structure is found on a masked copy of the text in which opaque
///         regions are replaced by a placeholder of the same length, so every position still maps to its line and the
///         link targets are read from the original text.
///     </para>
/// </remarks>
internal sealed partial class MarkdownDocument
{
	/// <summary>Private-use character that replaces opaque content in the masked copy; never whitespace nor markup.</summary>
	private const char Opaque = '\uE000';

	private readonly bool[] _fenced;
	private readonly int[] _lineStarts;

	private MarkdownDocument(string text, string[] lines, int[] lineStarts, bool[] fenced, List<MarkdownLink> links,
		List<MarkdownHeading> headings, HashSet<string> anchors)
	{
		Text = text;
		Lines = lines;
		_lineStarts = lineStarts;
		_fenced = fenced;
		Links = links;
		Headings = headings;
		Anchors = anchors;
	}

	/// <summary>The text with every line ending normalized to <c>\n</c> and without a byte-order mark.</summary>
	public string Text
	{
		get;
	}

	/// <summary>The lines of <see cref="Text" />, without line terminators; line <c>n</c> is at index <c>n - 1</c>.</summary>
	public IReadOnlyList<string> Lines
	{
		get;
	}

	/// <summary>Every link, image, reference definition and HTML <c>href</c>/<c>src</c> target, in document order.</summary>
	public IReadOnlyList<MarkdownLink> Links
	{
		get;
	}

	/// <summary>Every ATX heading outside fenced code and HTML comments, in document order.</summary>
	public IReadOnlyList<MarkdownHeading> Headings
	{
		get;
	}

	/// <summary>The fragments this page answers: heading anchors and explicit <c>&lt;a id&gt;</c>/<c>&lt;a name&gt;</c> anchors.</summary>
	public IReadOnlySet<string> Anchors
	{
		get;
	}

	/// <summary>Parses Markdown text.</summary>
	public static MarkdownDocument Parse(string text)
	{
		ArgumentNullException.ThrowIfNull(text);

		string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
		if (normalized.Length > 0 && normalized[0] == '\uFEFF')
		{
			normalized = normalized[1..];
		}

		string[] lines = normalized.Split('\n');
		int[] lineStarts = new int[lines.Length];
		for (int n = 1; n < lines.Length; n++)
		{
			lineStarts[n] = lineStarts[n - 1] + lines[n - 1].Length + 1;
		}

		bool[] fenced = FindFencedLines(lines);
		char[] masked = normalized.ToCharArray();
		for (int n = 0; n < lines.Length; n++)
		{
			if (fenced[n])
			{
				MaskRange(masked, lineStarts[n], lineStarts[n] + lines[n].Length);
			}
		}

		MaskHtmlCommentBlocks(masked, lines, lineStarts);

		List<MarkdownLink> links = [];
		HashSet<string> anchors = new(StringComparer.Ordinal);
		List<MarkdownHeading> headings = ReadHeadings(masked, lines, lineStarts, anchors, out bool[] headingLines);

		foreach ((int start, int end) in Blocks(masked, lines, lineStarts, headingLines))
		{
			MaskInlineOpaqueRegions(masked, start, end);
			ReadInlineLinks(masked, normalized, start, end, lineStarts, links);
			ReadHtmlAttributes(masked, normalized, start, end, lineStarts, links, anchors);
		}

		ReadReferenceDefinitions(masked, normalized, lines, lineStarts, fenced, links);
		List<MarkdownLink> ordered = [.. links.OrderBy(static link => link.Line)];
		return new MarkdownDocument(normalized, lines, lineStarts, fenced, ordered, headings, anchors);
	}

	/// <summary>Whether a 1-based line is a fence delimiter or the content of fenced code.</summary>
	public bool IsInFencedCode(int line)
	{
		return _fenced[line - 1];
	}

	/// <summary>
	///     Absolute local paths anywhere in the raw text, fenced code included: drive paths such as <c>D:\x</c> or
	///     <c>C:/x</c>, <c>file:</c> URIs and user-profile folders. Environment placeholders such as <c>%APPDATA%\x</c> are
	///     not local paths. Each match reports the whitespace-delimited token that contains it.
	/// </summary>
	public IReadOnlyList<MarkdownTextMatch> FindLocalPaths()
	{
		List<MarkdownTextMatch> matches = [];
		int previousTokenStart = -1;
		foreach (Match match in LocalPathRegex.Matches(Text))
		{
			int tokenStart = match.Index;
			while (tokenStart > 0 && !char.IsWhiteSpace(Text[tokenStart - 1]))
			{
				tokenStart--;
			}

			if (tokenStart == previousTokenStart)
			{
				continue;
			}

			previousTokenStart = tokenStart;
			int tokenEnd = match.Index + match.Length;
			while (tokenEnd < Text.Length && !char.IsWhiteSpace(Text[tokenEnd]))
			{
				tokenEnd++;
			}

			matches.Add(new MarkdownTextMatch(LineOf(tokenStart), Text[tokenStart..tokenEnd]));
		}

		return matches;
	}

	/// <summary>Every match of <paramref name="pattern" /> over the raw text, fenced code included.</summary>
	public IReadOnlyList<MarkdownTextMatch> FindAll(Regex pattern)
	{
		ArgumentNullException.ThrowIfNull(pattern);
		List<MarkdownTextMatch> matches = [];
		foreach (Match match in pattern.Matches(Text))
		{
			matches.Add(new MarkdownTextMatch(LineOf(match.Index), match.Value));
		}

		return matches;
	}

	/// <summary>
	///     The GitHub anchor of a heading before duplicate suffixing: the rendered text (code spans keep their content,
	///     links keep their text, images, HTML tags, comments and emphasis markers disappear), lower-cased, with letters,
	///     marks, digits, <c>-</c> and <c>_</c> kept, each space turned into <c>-</c>, and every other character dropped.
	/// </summary>
	public static string ToAnchorBase(string headingText)
	{
		ArgumentNullException.ThrowIfNull(headingText);
		string plain = ToPlainText(headingText).ToLowerInvariant();
		StringBuilder anchor = new(plain.Length);
		foreach (Rune rune in plain.EnumerateRunes())
		{
			if (rune.Value is ' ' or '-' or '_')
			{
				anchor.Append(rune.Value == ' ' ? '-' : (char) rune.Value);
				continue;
			}

			switch (Rune.GetUnicodeCategory(rune))
			{
				case UnicodeCategory.UppercaseLetter:
				case UnicodeCategory.LowercaseLetter:
				case UnicodeCategory.TitlecaseLetter:
				case UnicodeCategory.ModifierLetter:
				case UnicodeCategory.OtherLetter:
				case UnicodeCategory.NonSpacingMark:
				case UnicodeCategory.SpacingCombiningMark:
				case UnicodeCategory.EnclosingMark:
				case UnicodeCategory.DecimalDigitNumber:
				case UnicodeCategory.LetterNumber:
				case UnicodeCategory.OtherNumber:
					anchor.Append(rune.ToString());
					break;
				default:
					break;
			}
		}

		return anchor.ToString();
	}

	private int LineOf(int offset)
	{
		return LineOf(_lineStarts, offset);
	}

	private static int LineOf(int[] lineStarts, int offset)
	{
		int index = Array.BinarySearch(lineStarts, offset);
		return (index >= 0 ? index : ~index - 1) + 1;
	}

	/// <summary>Marks fence delimiter lines and fenced content. An unclosed fence runs to the end of the document.</summary>
	private static bool[] FindFencedLines(string[] lines)
	{
		bool[] fenced = new bool[lines.Length];
		char fenceCharacter = '\0';
		int fenceLength = 0;
		for (int n = 0; n < lines.Length; n++)
		{
			if (fenceLength == 0)
			{
				Match opener = FenceOpenerRegex.Match(lines[n]);
				if (!opener.Success)
				{
					continue;
				}

				string fence = opener.Groups["fence"].Value;
				if (fence[0] == '`' && opener.Groups["info"].Value.Contains('`', StringComparison.Ordinal))
				{
					continue;
				}

				fenceCharacter = fence[0];
				fenceLength = fence.Length;
				fenced[n] = true;
				continue;
			}

			fenced[n] = true;
			string trimmed = lines[n].Trim();
			if (trimmed.Length >= fenceLength && IsRunOf(trimmed, fenceCharacter))
			{
				fenceLength = 0;
			}
		}

		return fenced;
	}

	/// <summary>Masks HTML comment blocks: a line that starts with <c>&lt;!--</c> hides everything up to <c>--&gt;</c>.</summary>
	private static void MaskHtmlCommentBlocks(char[] masked, string[] lines, int[] lineStarts)
	{
		for (int n = 0; n < lines.Length; n++)
		{
			int start = lineStarts[n];
			int indentation = 0;
			while (indentation < 4 && start + indentation < masked.Length && masked[start + indentation] == ' ')
			{
				indentation++;
			}

			if (indentation > 3 || !StartsWith(masked, start + indentation, masked.Length, "<!--"))
			{
				continue;
			}

			int close = IndexOf(masked, "-->", start + indentation + 4, masked.Length);
			int end = close < 0 ? masked.Length : close + 3;
			MaskRange(masked, start + indentation, end);
		}
	}

	private static List<MarkdownHeading> ReadHeadings(char[] masked, string[] lines, int[] lineStarts,
		HashSet<string> anchors, out bool[] headingLines)
	{
		List<MarkdownHeading> headings = [];
		Dictionary<string, int> occurrences = new(StringComparer.Ordinal);
		headingLines = new bool[lines.Length];
		for (int n = 0; n < lines.Length; n++)
		{
			string line = new(masked, lineStarts[n], lines[n].Length);
			Match heading = AtxHeadingRegex.Match(line);
			if (!heading.Success)
			{
				continue;
			}

			headingLines[n] = true;
			string content = StripClosingSequence(heading.Groups["text"].Value);
			string anchor = Deduplicate(ToAnchorBase(content), occurrences);
			headings.Add(new MarkdownHeading(heading.Groups["hashes"].Length, content, anchor, n + 1));
			if (anchor.Length > 0)
			{
				anchors.Add(anchor);
			}
		}

		return headings;
	}

	/// <summary>The duplicate rule of GitHub's slugger: the second <c>x</c> becomes <c>x-1</c>, then <c>x-2</c>, and so on.</summary>
	private static string Deduplicate(string anchor, Dictionary<string, int> occurrences)
	{
		string result = anchor;
		while (occurrences.ContainsKey(result))
		{
			occurrences[anchor]++;
			result = anchor + "-" + occurrences[anchor].ToString(CultureInfo.InvariantCulture);
		}

		occurrences[result] = 0;
		return result;
	}

	private static string StripClosingSequence(string content)
	{
		string trimmed = content.Trim();
		int end = trimmed.Length;
		while (end > 0 && trimmed[end - 1] == '#')
		{
			end--;
		}

		if (end == 0)
		{
			return string.Empty;
		}

		return end < trimmed.Length && trimmed[end - 1] is ' ' or '\t' ? trimmed[..end].TrimEnd() : trimmed;
	}

	/// <summary>Paragraph-like blocks: runs of non-blank lines, with each heading line as a block of its own.</summary>
	private static List<(int Start, int End)> Blocks(char[] masked, string[] lines, int[] lineStarts, bool[] headingLines)
	{
		List<(int Start, int End)> blocks = [];
		int blockStart = -1;
		int blockEnd = -1;
		for (int n = 0; n < lines.Length; n++)
		{
			int start = lineStarts[n];
			int end = start + lines[n].Length;
			bool blank = IsBlank(masked, start, end);
			if (blank || headingLines[n])
			{
				if (blockStart >= 0)
				{
					blocks.Add((blockStart, blockEnd));
					blockStart = -1;
				}

				if (headingLines[n])
				{
					blocks.Add((start, end));
				}

				continue;
			}

			if (blockStart < 0)
			{
				blockStart = start;
			}

			blockEnd = end;
		}

		if (blockStart >= 0)
		{
			blocks.Add((blockStart, blockEnd));
		}

		return blocks;
	}

	/// <summary>Masks backslash escapes, code spans and inline HTML comments of one block, left to right.</summary>
	private static void MaskInlineOpaqueRegions(char[] masked, int start, int end)
	{
		int i = start;
		while (i < end)
		{
			char character = masked[i];
			if (character == '\\' && i + 1 < end && IsAsciiPunctuation(masked[i + 1]))
			{
				MaskRange(masked, i, i + 2);
				i += 2;
				continue;
			}

			if (character == '`')
			{
				int run = RunLength(masked, i, end, '`');
				int closing = FindBacktickRun(masked, i + run, end, run);
				if (closing < 0)
				{
					i += run;
					continue;
				}

				MaskRange(masked, i, closing + run);
				i = closing + run;
				continue;
			}

			if (character == '<' && StartsWith(masked, i, end, "<!--"))
			{
				int close = IndexOf(masked, "-->", i + 4, end);
				if (close >= 0)
				{
					MaskRange(masked, i, close + 3);
					i = close + 3;
					continue;
				}
			}

			i++;
		}
	}

	/// <summary>
	///     Reads <c>[text](destination "title")</c> and <c>![alt](destination)</c>. The text may contain balanced brackets,
	///     an image (badges) and line breaks; every opening bracket is tried, so an image nested in a link is found too.
	/// </summary>
	private static void ReadInlineLinks(char[] masked, string original, int start, int end, int[] lineStarts,
		List<MarkdownLink> links)
	{
		for (int i = start; i < end; i++)
		{
			if (masked[i] != '[')
			{
				continue;
			}

			int close = FindClosingBracket(masked, i, end);
			if (close < 0 || close + 1 >= end || masked[close + 1] != '(')
			{
				continue;
			}

			if (!TryReadDestination(masked, original, close + 2, end, out string destination))
			{
				continue;
			}

			MarkdownLinkKind kind = i > start && masked[i - 1] == '!' ? MarkdownLinkKind.Image : MarkdownLinkKind.Inline;
			links.Add(new MarkdownLink(kind, destination, LineOf(lineStarts, close)));
		}
	}

	private static int FindClosingBracket(char[] masked, int open, int end)
	{
		int depth = 0;
		for (int i = open; i < end; i++)
		{
			if (masked[i] == '[')
			{
				depth++;
			}
			else if (masked[i] == ']')
			{
				depth--;
				if (depth == 0)
				{
					return i;
				}
			}
		}

		return -1;
	}

	/// <summary>Reads a link destination, an optional title and the closing parenthesis, starting after <c>](</c>.</summary>
	private static bool TryReadDestination(char[] masked, string original, int position, int end, out string destination)
	{
		destination = string.Empty;
		int start = SkipWhitespace(masked, position, end);
		bool angled = start < end && masked[start] == '<';
		int after = angled ? EndOfAngleDestination(masked, start, end) : EndOfBareDestination(masked, start, end);
		if (after < 0)
		{
			return false;
		}

		string raw = angled ? original[(start + 1)..(after - 1)] : original[start..after];
		int i = SkipWhitespace(masked, after, end);
		if (i > after && i < end && masked[i] is '"' or '\'' or '(')
		{
			i = EndOfTitle(masked, i, end);
			if (i < 0)
			{
				return false;
			}

			i = SkipWhitespace(masked, i, end);
		}

		if (i >= end || masked[i] != ')')
		{
			return false;
		}

		destination = Unescape(raw);
		return true;
	}

	/// <summary>The position after the <c>&gt;</c> of a <c>&lt;destination&gt;</c>, or -1 when it is not closed on its line.</summary>
	private static int EndOfAngleDestination(char[] masked, int open, int end)
	{
		int close = open + 1;
		while (close < end && masked[close] is not ('>' or '<' or '\n'))
		{
			close++;
		}

		return close < end && masked[close] == '>' ? close + 1 : -1;
	}

	/// <summary>
	///     The position after a bare destination, which stops at whitespace or at an unbalanced <c>)</c>; -1 when a
	///     parenthesis of the destination stays open.
	/// </summary>
	private static int EndOfBareDestination(char[] masked, int start, int end)
	{
		int i = start;
		int depth = 0;
		while (i < end && !char.IsWhiteSpace(masked[i]))
		{
			if (masked[i] == '(')
			{
				depth++;
			}
			else if (masked[i] == ')')
			{
				if (depth == 0)
				{
					break;
				}

				depth--;
			}

			i++;
		}

		return depth == 0 ? i : -1;
	}

	/// <summary>The position after a <c>"title"</c>, <c>'title'</c> or <c>(title)</c>, or -1 when it is not closed.</summary>
	private static int EndOfTitle(char[] masked, int open, int end)
	{
		char closing = masked[open] == '(' ? ')' : masked[open];
		int close = open + 1;
		while (close < end && masked[close] != closing)
		{
			close++;
		}

		return close < end ? close + 1 : -1;
	}

	/// <summary>Reads <c>href</c>/<c>src</c> attributes of raw HTML tags, and <c>id</c>/<c>name</c> anchors of <c>a</c> tags.</summary>
	private static void ReadHtmlAttributes(char[] masked, string original, int start, int end, int[] lineStarts,
		List<MarkdownLink> links, HashSet<string> anchors)
	{
		string block = new(masked, start, end - start);
		foreach (Match tag in HtmlTagRegex.Matches(block))
		{
			Group attributes = tag.Groups["attributes"];
			bool isAnchorTag = string.Equals(tag.Groups["name"].Value, "a", StringComparison.OrdinalIgnoreCase);
			foreach (Match attribute in HtmlAttributeRegex.Matches(attributes.Value))
			{
				Group value = attribute.Groups["value"];
				int valueStart = start + attributes.Index + value.Index;
				string text = WebUtility.HtmlDecode(original.Substring(valueStart, value.Length));
				string name = attribute.Groups["name"].Value;
				if (string.Equals(name, "href", StringComparison.OrdinalIgnoreCase)
					|| string.Equals(name, "src", StringComparison.OrdinalIgnoreCase))
				{
					links.Add(new MarkdownLink(MarkdownLinkKind.HtmlAttribute, text, LineOf(lineStarts, valueStart)));
				}
				else if (isAnchorTag
						 && (string.Equals(name, "id", StringComparison.OrdinalIgnoreCase)
							 || string.Equals(name, "name", StringComparison.OrdinalIgnoreCase)))
				{
					anchors.Add(text);
				}
			}
		}
	}

	/// <summary>Reads <c>[label]: destination</c> lines; footnote definitions (<c>[^1]:</c>) are not link definitions.</summary>
	private static void ReadReferenceDefinitions(char[] masked, string original, string[] lines, int[] lineStarts,
		bool[] fenced, List<MarkdownLink> links)
	{
		for (int n = 0; n < lines.Length; n++)
		{
			if (fenced[n])
			{
				continue;
			}

			string line = new(masked, lineStarts[n], lines[n].Length);
			Match definition = ReferenceDefinitionRegex.Match(line);
			if (!definition.Success)
			{
				continue;
			}

			Group destination = definition.Groups["destination"];
			string raw = original.Substring(lineStarts[n] + destination.Index, destination.Length);
			links.Add(new MarkdownLink(MarkdownLinkKind.ReferenceDefinition, Unescape(raw), n + 1));
		}
	}

	/// <summary>The rendered text of a heading, as GitHub reads it to build the anchor.</summary>
	private static string ToPlainText(string markdown)
	{
		StringBuilder plain = new(markdown.Length);
		int i = 0;
		while (i < markdown.Length)
		{
			int next = markdown[i] switch
			{
				'\\' => AppendEscape(markdown, i, plain),
				'`' => AppendCodeSpan(markdown, i, plain),
				'<' => SkipHtml(markdown, i),
				'!' or '[' => AppendLinkText(markdown, i, plain),
				'&' => AppendEntity(markdown, i, plain),
				'_' or '*' => AppendEmphasisRun(markdown, i, plain),
				_ => -1
			};

			if (next < 0)
			{
				plain.Append(markdown[i]);
				next = i + 1;
			}

			i = next;
		}

		return plain.ToString();
	}

	/// <summary>A backslash escape keeps the escaped character. Returns the next position, or -1 when it is no escape.</summary>
	private static int AppendEscape(string markdown, int i, StringBuilder plain)
	{
		if (i + 1 >= markdown.Length || !IsAsciiPunctuation(markdown[i + 1]))
		{
			return -1;
		}

		plain.Append(markdown[i + 1]);
		return i + 2;
	}

	/// <summary>A code span keeps its content; an unmatched backtick run stays literal.</summary>
	private static int AppendCodeSpan(string markdown, int i, StringBuilder plain)
	{
		int run = RunLength(markdown, i, '`');
		int closing = FindBacktickRun(markdown, i + run, run);
		if (closing < 0)
		{
			plain.Append('`', run);
			return i + run;
		}

		plain.Append(NormalizeCodeSpan(markdown[(i + run)..closing]));
		return closing + run;
	}

	/// <summary>HTML comments and tags have no text. Returns the position after them, or -1.</summary>
	private static int SkipHtml(string markdown, int i)
	{
		if (markdown.AsSpan(i).StartsWith("<!--", StringComparison.Ordinal))
		{
			int close = markdown.IndexOf("-->", i + 4, StringComparison.Ordinal);
			if (close >= 0)
			{
				return close + 3;
			}
		}

		Match tag = InlineHtmlTagRegex.Match(markdown, i);
		return tag.Success ? i + tag.Length : -1;
	}

	/// <summary>A link keeps its text and an image has none. Returns the position after it, or -1.</summary>
	private static int AppendLinkText(string markdown, int i, StringBuilder plain)
	{
		bool image = markdown[i] == '!';
		int open = image ? i + 1 : i;
		if (open >= markdown.Length || markdown[open] != '['
			|| !TryReadHeadingLink(markdown, open, out int end, out string text))
		{
			return -1;
		}

		if (!image)
		{
			plain.Append(ToPlainText(text));
		}

		return end;
	}

	/// <summary>An HTML entity is decoded. Returns the position after it, or -1.</summary>
	private static int AppendEntity(string markdown, int i, StringBuilder plain)
	{
		Match entity = EntityRegex.Match(markdown, i);
		if (!entity.Success)
		{
			return -1;
		}

		plain.Append(WebUtility.HtmlDecode(entity.Value));
		return i + entity.Length;
	}

	/// <summary>
	///     Emphasis markers have no text: every <c>*</c>, and every <c>_</c> run at a word boundary. CommonMark never opens
	///     or closes underscore emphasis inside a word, so <c>snake_case</c> keeps its underscore.
	/// </summary>
	private static int AppendEmphasisRun(string markdown, int i, StringBuilder plain)
	{
		char marker = markdown[i];
		int run = RunLength(markdown, i, marker);
		bool atWordStart = i == 0 || !char.IsLetterOrDigit(markdown[i - 1]);
		bool atWordEnd = i + run >= markdown.Length || !char.IsLetterOrDigit(markdown[i + run]);
		if (marker == '_' && !atWordStart && !atWordEnd)
		{
			plain.Append(marker, run);
		}

		return i + run;
	}

	/// <summary>Reads <c>[text](destination)</c> or <c>[text][label]</c> inside a heading.</summary>
	private static bool TryReadHeadingLink(string markdown, int open, out int end, out string text)
	{
		end = open;
		text = string.Empty;
		int depth = 0;
		int close = -1;
		for (int i = open; i < markdown.Length; i++)
		{
			if (markdown[i] == '[')
			{
				depth++;
			}
			else if (markdown[i] == ']' && --depth == 0)
			{
				close = i;
				break;
			}
		}

		if (close < 0 || close + 1 >= markdown.Length || markdown[close + 1] is not ('(' or '['))
		{
			return false;
		}

		char opener = markdown[close + 1];
		char closer = opener == '(' ? ')' : ']';
		int nesting = 0;
		for (int i = close + 1; i < markdown.Length; i++)
		{
			if (markdown[i] == opener)
			{
				nesting++;
			}
			else if (markdown[i] == closer && --nesting == 0)
			{
				end = i + 1;
				text = markdown[(open + 1)..close];
				return true;
			}
		}

		return false;
	}

	private static string NormalizeCodeSpan(string content)
	{
		string singleLine = content.Replace('\n', ' ');
		bool padded = singleLine.Length >= 2 && singleLine[0] == ' ' && singleLine[^1] == ' '
					  && singleLine.AsSpan().ContainsAnyExcept(' ');
		return padded ? singleLine[1..^1] : singleLine;
	}

	private static string Unescape(string raw)
	{
		if (!raw.Contains('\\', StringComparison.Ordinal))
		{
			return raw;
		}

		StringBuilder unescaped = new(raw.Length);
		for (int i = 0; i < raw.Length; i++)
		{
			if (raw[i] == '\\' && i + 1 < raw.Length && IsAsciiPunctuation(raw[i + 1]))
			{
				i++;
			}

			unescaped.Append(raw[i]);
		}

		return unescaped.ToString();
	}

	private static bool IsAsciiPunctuation(char character)
	{
		return character is >= '!' and <= '/' or >= ':' and <= '@' or >= '[' and <= '`' or >= '{' and <= '~';
	}

	private static bool IsRunOf(string text, char character)
	{
		foreach (char current in text)
		{
			if (current != character)
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>Whitespace only, or entirely hidden (a fenced line or an HTML comment block line).</summary>
	private static bool IsBlank(char[] masked, int start, int end)
	{
		for (int i = start; i < end; i++)
		{
			if (!char.IsWhiteSpace(masked[i]) && masked[i] != Opaque)
			{
				return false;
			}
		}

		return true;
	}

	private static int SkipWhitespace(char[] masked, int position, int end)
	{
		while (position < end && char.IsWhiteSpace(masked[position]))
		{
			position++;
		}

		return position;
	}

	private static int RunLength(char[] text, int start, int end, char character)
	{
		int i = start;
		while (i < end && text[i] == character)
		{
			i++;
		}

		return i - start;
	}

	private static int RunLength(string text, int start, char character)
	{
		int i = start;
		while (i < text.Length && text[i] == character)
		{
			i++;
		}

		return i - start;
	}

	/// <summary>The start of the next backtick run of exactly <paramref name="length" /> characters, or -1.</summary>
	private static int FindBacktickRun(char[] text, int start, int end, int length)
	{
		int i = start;
		while (i < end)
		{
			if (text[i] != '`')
			{
				i++;
				continue;
			}

			int run = RunLength(text, i, end, '`');
			if (run == length)
			{
				return i;
			}

			i += run;
		}

		return -1;
	}

	private static int FindBacktickRun(string text, int start, int length)
	{
		int i = start;
		while (i < text.Length)
		{
			if (text[i] != '`')
			{
				i++;
				continue;
			}

			int run = RunLength(text, i, '`');
			if (run == length)
			{
				return i;
			}

			i += run;
		}

		return -1;
	}

	private static bool StartsWith(char[] text, int position, int end, string value)
	{
		if (position + value.Length > end)
		{
			return false;
		}

		return text.AsSpan(position, value.Length).SequenceEqual(value);
	}

	private static int IndexOf(char[] text, string value, int start, int end)
	{
		if (start >= end)
		{
			return -1;
		}

		int index = text.AsSpan(start, end - start).IndexOf(value);
		return index < 0 ? -1 : start + index;
	}

	/// <summary>Replaces a range by <see cref="Opaque" />, keeping line feeds so blocks and lines stay where they were.</summary>
	private static void MaskRange(char[] masked, int start, int end)
	{
		for (int i = start; i < end; i++)
		{
			if (masked[i] != '\n')
			{
				masked[i] = Opaque;
			}
		}
	}

	[GeneratedRegex("^[ \\t]*(?<fence>`{3,}|~{3,})(?<info>.*)$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
		DocumentationConventions.RegexTimeoutMilliseconds)]
	private static partial Regex FenceOpenerRegex
	{
		get;
	}

	[GeneratedRegex("^ {0,3}(?<hashes>#{1,6})(?:[ \\t]+(?<text>.*))?$",
		RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, DocumentationConventions.RegexTimeoutMilliseconds)]
	private static partial Regex AtxHeadingRegex
	{
		get;
	}

	[GeneratedRegex("<(?<name>[A-Za-z][A-Za-z0-9-]*)(?<attributes>(?:\\s[^<>]*)?)>",
		RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, DocumentationConventions.RegexTimeoutMilliseconds)]
	private static partial Regex HtmlTagRegex
	{
		get;
	}

	[GeneratedRegex("(?<![A-Za-z0-9_.:-])(?<name>[A-Za-z_:][A-Za-z0-9_.:-]*)\\s*=\\s*(?:\"(?<value>[^\"]*)\"|'(?<value>[^']*)'|(?<value>[^\\s\"'=<>`]+))",
		RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, DocumentationConventions.RegexTimeoutMilliseconds)]
	private static partial Regex HtmlAttributeRegex
	{
		get;
	}

	[GeneratedRegex("\\G</?[A-Za-z][A-Za-z0-9-]*(?:\\s[^<>]*)?/?>", RegexOptions.CultureInvariant,
		DocumentationConventions.RegexTimeoutMilliseconds)]
	private static partial Regex InlineHtmlTagRegex
	{
		get;
	}

	[GeneratedRegex("\\G&(?:#[0-9]{1,7}|#[xX][0-9A-Fa-f]{1,6}|[A-Za-z][A-Za-z0-9]{1,31});", RegexOptions.CultureInvariant,
		DocumentationConventions.RegexTimeoutMilliseconds)]
	private static partial Regex EntityRegex
	{
		get;
	}

	[GeneratedRegex("^ {0,3}\\[(?!\\^)(?<label>[^\\]]+)\\]:[ \\t]*(?:<(?<destination>[^<>]*)>|(?<destination>[^\\s<]\\S*))",
		RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, DocumentationConventions.RegexTimeoutMilliseconds)]
	private static partial Regex ReferenceDefinitionRegex
	{
		get;
	}

	/// <summary>
	///     A drive path (<c>D:\</c>, <c>C:/</c>) not preceded by a word character, a <c>file:</c> URI, or a user-profile
	///     folder (<c>\Users\</c>, <c>/Users/</c>, <c>/home/</c>).
	/// </summary>
	[GeneratedRegex("(?<![A-Za-z0-9_])[A-Za-z]:[\\\\/]|(?<![A-Za-z0-9_])(?i:file):/|\\\\Users\\\\|/Users/|/home/",
		RegexOptions.CultureInvariant, DocumentationConventions.RegexTimeoutMilliseconds)]
	private static partial Regex LocalPathRegex
	{
		get;
	}
}
