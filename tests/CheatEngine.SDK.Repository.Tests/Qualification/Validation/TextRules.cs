using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CheatEngine.SDK.Repository.Tests.Qualification.Validation;

/// <summary>Text rules shared by every qualification document: no local path, no raw debug output, no global score.</summary>
internal static class TextRules
{
	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

	/// <summary>
	///     A drive-rooted path (<c>C:\</c>, <c>d:/</c>, not the <c>s:/</c> of <c>https://</c>), a Windows or macOS user
	///     profile segment, a home directory or a <c>file://</c> URI. Environment placeholders such as
	///     <c>%ProgramFiles%</c> are allowed.
	/// </summary>
	private static readonly Regex AbsoluteLocalPath = new(
		@"(?<![A-Za-z0-9])[A-Za-z]:[\\/]|\\[Uu]sers\\|/Users/|(?<![A-Za-z0-9])/home/|[Ff][Ii][Ll][Ee]://",
		RegexOptions.CultureInvariant, RegexTimeout);

	/// <summary>A DebugView capture line (sequence, time, [pid]) or the SDK host log prefix.</summary>
	private static readonly Regex RawDebugOutput = new(
		@"(?:^|\n)\s*\d+\s+\d+\.\d+\s+\[\d+\]|\[CheatEngine\.SDK\.Hosting\]\s+(?:Information|Warning|Error)",
		RegexOptions.CultureInvariant, RegexTimeout);

	/// <summary>A number followed by a percent sign: the qualification documents never publish a global score.</summary>
	private static readonly Regex Percentage = new(@"\d\s?%", RegexOptions.CultureInvariant, RegexTimeout);

	internal static bool ContainsAbsoluteLocalPath(string text)
	{
		return AbsoluteLocalPath.IsMatch(text);
	}

	internal static bool ContainsRawDebugOutput(string text)
	{
		return RawDebugOutput.IsMatch(text);
	}

	internal static bool ContainsPercentage(string text)
	{
		return Percentage.IsMatch(text);
	}

	/// <summary>Every string value (and property name) of <paramref name="document" /> that holds a local path.</summary>
	internal static IReadOnlyList<string> AbsoluteLocalPaths(JsonElement document)
	{
		List<string> found = [];
		Collect(document, string.Empty, found);
		return found;
	}

	private static void Collect(JsonElement element, string pointer, List<string> found)
	{
		switch (element.ValueKind)
		{
			case JsonValueKind.Object:
				foreach (JsonProperty property in element.EnumerateObject())
				{
					string child = pointer + "/" + property.Name;
					if (ContainsAbsoluteLocalPath(property.Name))
					{
						found.Add(child + " (property name)");
					}

					Collect(property.Value, child, found);
				}

				break;
			case JsonValueKind.Array:
				int index = 0;
				foreach (JsonElement item in element.EnumerateArray())
				{
					Collect(item, pointer + "/" + index.ToString(CultureInfo.InvariantCulture), found);
					index++;
				}

				break;
			case JsonValueKind.String:
				string value = element.GetString() ?? string.Empty;
				if (ContainsAbsoluteLocalPath(value))
				{
					found.Add(pointer + ": " + value);
				}

				break;
		}
	}
}
