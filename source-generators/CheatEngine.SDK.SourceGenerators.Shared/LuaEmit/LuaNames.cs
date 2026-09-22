using System;
using System.Diagnostics.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

/// <summary>
///     The rule for the names given to <c>[LuaFunction]</c> and <c>[LuaGlobal]</c>: a Lua <i>Name</i> as the Lua 5.3
///     manual defines it (section 3.1, ASCII letters, digits and underscores, not starting with a digit) that is not a
///     reserved word. Shared by the generators (which stay silent on an invalid name) and the analyzer that reports it.
/// </summary>
/// <remarks>
///     A global can technically carry any string as its key, but a script can only spell an identifier, and the
///     generated code derives C# identifiers from the name (the cache field <c>s_luaGlobal_&lt;name&gt;</c>, the thunk
///     <c>__LuaThunk_&lt;name&gt;</c>), so the rule keeps both sides simple. Lua is locale-independent here: only
///     ASCII letters count, as in the reference implementation's default build. No length limit: a name only ever
///     becomes a compile-time <c>"..."u8</c> literal (<c>CSharpLiteral.ToUtf8Literal</c>) and a C# identifier suffix,
///     never a run-time UTF-8 transcode, so it never reaches <c>CheatEngine.SDK.Lua.Text.Utf8Scratch</c>'s
///     <see langword="stackalloc" />/pool
///     path (that path is for the run-time <em>content</em> of a <see cref="LuaValueKind.String" /> value, an unrelated,
///     already length-agnostic concern of the <c>CheatEngine.SDK.Lua</c> layer).
/// </remarks>
[SuppressMessage(
	"Meziantou.Analyzer",
	"MA0182",
	Justification =
		"This shared internal helper is consumed by the designated friend generator and analyzer assemblies.")]
internal static class LuaNames
{
	/// <summary>The 22 reserved words of Lua 5.3 (manual, section 3.1), which cannot name a global a script can reference.</summary>
	private static readonly string[] ReservedWords =
	[
		"and", "break", "do", "else", "elseif", "end", "false", "for", "function", "goto", "if", "in",
		"local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while"
	];

	/// <summary>Whether <paramref name="name" /> is a Lua identifier that is not a reserved word.</summary>
	/// <param name="name">The candidate; <see langword="null" /> and empty are invalid.</param>
	public static bool IsValidName(string? name)
	{
		if (string.IsNullOrEmpty(name) || !IsIdentifierStart(name![0]))
		{
			return false;
		}

		for (int i = 1; i < name.Length; i++)
		{
			if (!IsIdentifierPart(name[i]))
			{
				return false;
			}
		}

		return Array.IndexOf(ReservedWords, name) < 0;
	}

	/// <summary>Whether <paramref name="name" /> is one of Lua's reserved words.</summary>
	public static bool IsReservedWord(string name)
	{
		return name is not null && Array.IndexOf(ReservedWords, name) >= 0;
	}

	private static bool IsIdentifierStart(char c)
	{
		return c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '_';
	}

	private static bool IsIdentifierPart(char c)
	{
		return IsIdentifierStart(c) || c is >= '0' and <= '9';
	}
}
