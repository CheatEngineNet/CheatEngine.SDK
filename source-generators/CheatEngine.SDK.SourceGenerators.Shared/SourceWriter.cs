using System;
using System.Text;

using Microsoft.CodeAnalysis.Text;

namespace CheatEngine.SDK.SourceGenerators.Shared;

/// <summary>
///     Indentation-aware text writer for emitting C# source: a thin layer over one <see cref="StringBuilder" />.
/// </summary>
/// <remarks>
///     <para>
///         Generators emit text, not syntax trees (building nodes and calling <c>NormalizeWhitespace</c> costs far more
///         than
///         appending characters). The writer keeps the current indentation level and applies it lazily, when the first
///         character of a line is written, so blank lines never carry trailing whitespace.
///     </para>
///     <para>
///         Output is deterministic and host-independent: lines end with <see cref="NewLine" /> (<c>\n</c>) whatever the
///         operating system (<c>Environment.NewLine</c> is banned in compiler extensions, RS1035), indentation is four
///         spaces
///         per level, and nothing here formats numbers or dates. Text handed to <see cref="Write(string)" /> may span
///         several
///         lines (for example a raw string literal): every line is re-indented to the current level.
///     </para>
///     <para>
///         Line breaks in written text: <c>\n</c>, <c>\r\n</c> and a lone <c>\r</c> each end the line and come out as one
///         <see cref="NewLine" />, so the output never contains a carriage return. A lone <c>\r</c> is a line terminator
///         for
///         the C# lexer: dropping it could glue a <c>//</c> comment to the code after it, and copying it would put a
///         second
///         kind of line ending into the file. A <c>\r\n</c> pair is recognised even when its two characters arrive in two
///         consecutive <c>Write</c> calls. The other line terminators of C# (U+0085, U+2028, U+2029) are written as they
///         are: they only make sense inside literals, where <see cref="CSharpLiteral" /> escapes them.
///     </para>
///     <para>
///         Allocation budget: the builder's buffer, plus one string when the text is materialised. Indentation is appended
///         with <see cref="StringBuilder.Append(char, int)" /> and line segments with
///         <see cref="StringBuilder.Append(string, int, int)" />: no intermediate strings. Not thread-safe; one writer per
///         emitted file.
///     </para>
/// </remarks>
internal sealed class SourceWriter
{
	/// <summary>Line terminator of all generated text.</summary>
	public const char NewLine = '\n';

	/// <summary>Spaces per indentation level.</summary>
	public const int IndentSize = 4;

	private static readonly char[] LineBreakCharacters = ['\r', '\n'];

	private readonly StringBuilder _builder;

	// The last thing written was a line break caused by '\r': a '\n' that comes next completes the same CR LF pair
	// and must not end a second line. Anything written in between (text, an explicit WriteLine) clears it.
	private bool _afterCarriageReturn;
	private bool _atLineStart = true;

	/// <summary>Creates a writer whose buffer starts at <paramref name="capacity" /> characters.</summary>
	public SourceWriter(int capacity = 1024)
	{
		_builder = new StringBuilder(capacity);
	}

	/// <summary>Current indentation level (0 = column 0).</summary>
	public int IndentLevel
	{
		get;
		private set;
	}

	/// <summary>Number of characters written so far.</summary>
	public int Length => _builder.Length;

	/// <summary>Increases the indentation of the lines that follow by one level.</summary>
	public void Indent()
	{
		IndentLevel++;
	}

	/// <summary>Decreases the indentation by one level.</summary>
	/// <exception cref="InvalidOperationException">The level is already 0: the emitter's blocks are unbalanced.</exception>
	public void Unindent()
	{
		if (IndentLevel == 0)
		{
			throw new InvalidOperationException("Unbalanced indentation: Unindent() without a matching Indent().");
		}

		IndentLevel--;
	}

	/// <summary>
	///     Writes <paramref name="value" />, indenting first when it starts a line. <c>\n</c> and <c>\r</c> end the line
	///     instead (a <c>\n</c> directly after a <c>\r</c> belongs to the same line break).
	/// </summary>
	public void Write(char value)
	{
		if (value is '\r' or NewLine)
		{
			WriteLineBreakCharacter(value);
			return;
		}

		WriteIndentationIfNeeded();
		_builder.Append(value);
		_afterCarriageReturn = false;
	}

	/// <summary>
	///     Writes <paramref name="text" />. Embedded line breaks (<c>\n</c>, <c>\r\n</c> or a lone <c>\r</c>) end the
	///     current line; each non-empty line is indented to the current level.
	/// </summary>
	public void Write(string text)
	{
		if (text is null)
		{
			throw new ArgumentNullException(nameof(text));
		}

		int start = 0;
		while (start < text.Length)
		{
			int lineBreak = text.IndexOfAny(LineBreakCharacters, start);
			int end = lineBreak < 0 ? text.Length : lineBreak;

			if (end > start)
			{
				WriteIndentationIfNeeded();
				_builder.Append(text, start, end - start);
				_afterCarriageReturn = false;
			}

			if (lineBreak < 0)
			{
				return;
			}

			WriteLineBreakCharacter(text[lineBreak]);
			start = lineBreak + 1;
		}
	}

	/// <summary>Ends the current line. On an empty line nothing but the terminator is written.</summary>
	public void WriteLine()
	{
		_builder.Append(NewLine);
		_atLineStart = true;
		_afterCarriageReturn = false;
	}

	/// <summary>Writes <paramref name="text" /> (see <see cref="Write(string)" />) and ends the line.</summary>
	public void WriteLine(string text)
	{
		Write(text);
		WriteLine();
	}

	/// <summary>Writes <c>{</c> on its own line and indents what follows.</summary>
	public void OpenBlock()
	{
		WriteLine("{");
		Indent();
	}

	/// <summary>Unindents and writes <c>}</c> on its own line.</summary>
	public void CloseBlock()
	{
		Unindent();
		WriteLine("}");
	}

	/// <summary>
	///     Unindents and writes <c>}</c> followed by <paramref name="suffix" /> (for example <c>;</c> or <c>);</c>) on
	///     its own line.
	/// </summary>
	public void CloseBlock(string suffix)
	{
		Unindent();
		Write('}');
		WriteLine(suffix);
	}

	/// <summary>Empties the writer so that the buffer can be reused for another file.</summary>
	public void Clear()
	{
		_builder.Clear();
		IndentLevel = 0;
		_atLineStart = true;
		_afterCarriageReturn = false;
	}

	/// <summary>The text written so far.</summary>
	public override string ToString()
	{
		return _builder.ToString();
	}

	/// <summary>
	///     The text written so far as a UTF-8 <see cref="SourceText" />, the form <c>AddSource</c> wants (an encoding is
	///     required for the file to be embeddable in the PDB and to have a checksum).
	/// </summary>
	public SourceText ToSourceText()
	{
		return SourceText.From(_builder.ToString(), Encoding.UTF8);
	}

	// '\r' ends the line and is remembered; '\n' ends the line unless it completes a CR LF pair that already did.
	private void WriteLineBreakCharacter(char value)
	{
		if (value == '\r')
		{
			WriteLine();
			_afterCarriageReturn = true;
		}
		else if (_afterCarriageReturn)
		{
			_afterCarriageReturn = false;
		}
		else
		{
			WriteLine();
		}
	}

	private void WriteIndentationIfNeeded()
	{
		if (!_atLineStart)
		{
			return;
		}

		_builder.Append(' ', IndentLevel * IndentSize);
		_atLineStart = false;
	}
}
