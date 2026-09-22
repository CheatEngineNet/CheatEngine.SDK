using CheatEngine.SDK.Repository.Tests.Infrastructure;

namespace CheatEngine.SDK.Repository.Tests.PublicApi;

/// <summary>Readers for the committed API contract files: the suppression file and the <c>eng/api/*.txt</c> lists.</summary>
internal static class ApiContractFiles
{
	public const string SuppressionFile = "src/CheatEngine.SDK/CompatibilitySuppressions.xml";
	public const string InvisibleChangesFile = "eng/api/apicompat-invisible-changes.txt";
	public const string ClientConsumedTypesFile = "eng/api/client-consumed-sdk-types.txt";
	public const string ClientInducedBreaksFile = "eng/api/client-induced-breaks.txt";

	/// <summary>Assemblies embedded under <c>lib/net10.0</c>: the umbrella plus the six shipping libraries.</summary>
	public static IReadOnlySet<string> PackageAssemblies
	{
		get
		{
			HashSet<string> assemblies = new(StringComparer.Ordinal) { "CheatEngine.SDK" };
			foreach (PublicApiLibrary library in PublicApiLibrary.LoadAll())
			{
				assemblies.Add(library.Name);
			}

			return assemblies;
		}
	}

	public static IReadOnlyList<CompatibilitySuppression> ReadSuppressions()
	{
		XDocument document = XDocument.Load(FullPath(SuppressionFile));
		List<CompatibilitySuppression> suppressions = [];
		foreach (XElement suppression in document.Root!.Elements("Suppression"))
		{
			suppressions.Add(new CompatibilitySuppression(
				Value(suppression, "DiagnosticId"),
				Value(suppression, "Target"),
				Value(suppression, "Left"),
				Value(suppression, "Right"),
				string.Equals(Value(suppression, "IsBaselineSuppression"), "true", StringComparison.Ordinal)));
		}

		return suppressions;
	}

	/// <summary>Non-blank lines of a list file that are not whole-line <c>#</c> comments, trimmed.</summary>
	public static IReadOnlyList<string> ReadEntries(string relativePath)
	{
		List<string> entries = [];
		foreach (string line in File.ReadAllLines(FullPath(relativePath)))
		{
			string trimmed = line.Trim();
			if (trimmed.Length != 0 && !trimmed.StartsWith('#'))
			{
				entries.Add(trimmed);
			}
		}

		return entries;
	}

	/// <summary><c>eng/api/client-consumed-sdk-types.txt</c>: type name, and whether it is marked unresolved.</summary>
	public static IReadOnlyList<(string TypeName, bool MarkedUnresolved)> ReadClientConsumedTypes()
	{
		List<(string, bool)> types = [];
		foreach (string entry in ReadEntries(ClientConsumedTypesFile))
		{
			int comment = entry.IndexOf(" #", StringComparison.Ordinal);
			string name = comment < 0 ? entry : entry[..comment].TrimEnd();
			bool unresolved = comment >= 0 && entry[comment..].Contains("# unresolved (", StringComparison.Ordinal);
			types.Add((name, unresolved));
		}

		return types;
	}

	/// <summary><c>eng/api/apicompat-invisible-changes.txt</c>: the exact <c>*REMOVED*</c> line and its reason.</summary>
	public static IReadOnlyList<(string RemovedLine, string Reason)> ReadInvisibleChanges()
	{
		List<(string, string)> changes = [];
		foreach (string entry in ReadEntries(InvisibleChangesFile))
		{
			int separator = entry.LastIndexOf(" | ", StringComparison.Ordinal);
			changes.Add(separator < 0 ? (entry, "") : (entry[..separator], entry[(separator + 3)..].Trim()));
		}

		return changes;
	}

	private static string Value(XElement parent, string name)
	{
		return parent.Element(name)?.Value.Trim() ?? "";
	}

	private static string FullPath(string relativePath)
	{
		return Path.Combine(RepositoryRoot.Path, relativePath.Replace('/', Path.DirectorySeparatorChar));
	}
}
