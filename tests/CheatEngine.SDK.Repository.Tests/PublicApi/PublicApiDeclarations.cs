using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CheatEngine.SDK.Repository.Tests.PublicApi;

/// <summary>
///     Name-level reading of PublicAPI declaration lines and of ApiCompat documentation ids (DocIds), enough to relate a
///     compatibility suppression to the declaration it concerns. Matching is by declaring type and member name, not by
///     overload: two overloads of one method share a key. Explicit interface implementations are not mapped.
/// </summary>
internal static partial class PublicApiDeclarations
{
	private static readonly string[] s_modifiers =
		["abstract", "const", "extern", "new", "override", "readonly", "required", "sealed", "static", "virtual"];

	/// <summary>
	///     The declared name of a PublicAPI line: modifiers, the oblivious marker <c>~</c>, generic argument lists,
	///     parameters, the return type and an enum value are dropped. <c>T.M(int x) -&gt; void</c> gives <c>T.M</c>,
	///     <c>T.P.get -&gt; int</c> gives <c>T.P.get</c>, <c>T.this[int i].get -&gt; byte</c> gives <c>T.this</c>,
	///     <c>static T.operator ==(...)</c> gives <c>T.operator</c> and <c>T.M = 4 -&gt; T</c> gives <c>T.M</c>.
	/// </summary>
	public static string KeyOf(string line)
	{
		string text = line.StartsWith(PublicApiLibrary.RemovedPrefix, StringComparison.Ordinal)
			? line[PublicApiLibrary.RemovedPrefix.Length..]
			: line;
		text = text.TrimStart('~');
		bool stripped;
		do
		{
			stripped = false;
			foreach (string modifier in s_modifiers)
			{
				if (text.StartsWith(modifier + " ", StringComparison.Ordinal))
				{
					text = text[(modifier.Length + 1)..];
					stripped = true;
				}
			}
		} while (stripped);

		StringBuilder key = new(text.Length);
		int depth = 0;
		foreach (char character in text)
		{
			if (character == '<')
			{
				depth++;
			}
			else if (character == '>')
			{
				depth--;
			}
			else if (depth == 0)
			{
				if (character is ' ' or '(' or '[')
				{
					break;
				}

				key.Append(character);
			}
		}

		return key.ToString();
	}

	/// <summary>Whether a PublicAPI line declares a type (a bare, possibly generic, type name).</summary>
	public static bool IsTypeLine(string line)
	{
		return !line.Contains(" -> ", StringComparison.Ordinal) && !line.Contains('(', StringComparison.Ordinal);
	}

	/// <summary>The PublicAPI keys (see <see cref="KeyOf" />) a DocId can name.</summary>
	public static IReadOnlySet<string> CandidateKeysOf(string docId)
	{
		(char kind, string type, string member) = Split(docId);
		HashSet<string> keys = new(StringComparer.Ordinal);
		if (kind == 'T')
		{
			keys.Add(type);
			return keys;
		}

		string accessorName = member.Length > 4 ? member[4..] : member;
		switch (kind)
		{
			case 'M' when string.Equals(member, "#ctor", StringComparison.Ordinal):
				keys.Add($"{type}.{SimpleName(type)}");
				break;
			case 'M' when member.StartsWith("get_", StringComparison.Ordinal)
						  || member.StartsWith("set_", StringComparison.Ordinal):
				AddPropertyKeys(keys, type, accessorName);
				break;
			case 'M' when member.StartsWith("add_", StringComparison.Ordinal):
				keys.Add($"{type}.{accessorName}");
				break;
			case 'M' when member.StartsWith("remove_", StringComparison.Ordinal):
				keys.Add($"{type}.{member["remove_".Length..]}");
				break;
			case 'M' when string.Equals(member, "op_Implicit", StringComparison.Ordinal):
				keys.Add($"{type}.implicit");
				break;
			case 'M' when string.Equals(member, "op_Explicit", StringComparison.Ordinal):
				keys.Add($"{type}.explicit");
				break;
			case 'M' when member.StartsWith("op_", StringComparison.Ordinal):
				keys.Add($"{type}.operator");
				break;
			case 'P':
				AddPropertyKeys(keys, type, member);
				break;
			default:
				keys.Add($"{type}.{member}");
				break;
		}

		return keys;
	}

	/// <summary>The full name of the type a DocId declares or belongs to, without generic arity.</summary>
	public static string ContainingTypeOf(string docId)
	{
		return Split(docId).Type;
	}

	/// <summary>
	///     Parses an enum member line (<c>T.M = 4 -&gt; T</c>). Constants (<c>const T.C = 6 -&gt; int</c>) do not match
	///     because their type differs from the declaring type.
	/// </summary>
	public static bool TryParseEnumMember(string line, out string enumType, out string member, out long value)
	{
		Match match = EnumMemberLine().Match(line);
		if (!match.Success)
		{
			enumType = member = "";
			value = 0;
			return false;
		}

		enumType = match.Groups["type"].Value;
		member = match.Groups["member"].Value;
		value = long.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
		return true;
	}

	private static void AddPropertyKeys(HashSet<string> keys, string type, string property)
	{
		keys.Add($"{type}.{property}.get");
		keys.Add($"{type}.{property}.set");
		keys.Add($"{type}.{property}.init");
		if (string.Equals(property, "Item", StringComparison.Ordinal))
		{
			keys.Add($"{type}.this");
		}
	}

	private static (char Kind, string Type, string Member) Split(string docId)
	{
		if (docId.Length < 3 || docId[1] != ':')
		{
			throw new FormatException($"'{docId}' is not a documentation id.");
		}

		char kind = docId[0];
		string body = docId[2..];
		int parameters = body.IndexOf('(', StringComparison.Ordinal);
		if (parameters >= 0)
		{
			body = body[..parameters];
		}

		body = GenericArity().Replace(body, "");
		if (kind == 'T')
		{
			return (kind, body, "");
		}

		int dot = body.LastIndexOf('.');
		return (kind, body[..dot], body[(dot + 1)..]);
	}

	private static string SimpleName(string type)
	{
		return type[(type.LastIndexOf('.') + 1)..];
	}

	[GeneratedRegex(@"`+\d+", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex GenericArity();

	[GeneratedRegex(@"^(?<type>[\w.]+)\.(?<member>\w+) = (?<value>-?\d+) -> \k<type>$", RegexOptions.CultureInvariant,
		1000)]
	private static partial Regex EnumMemberLine();
}
