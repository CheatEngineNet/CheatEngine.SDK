using System.Text.RegularExpressions;

using CheatEngine.SDK.Repository.Tests.Infrastructure;

namespace CheatEngine.SDK.Repository.Tests.Diagnostics;

/// <summary>
///     The diagnostic identifiers that SDK product code declares on its own API through
///     <c>[Obsolete(DiagnosticId = "CESDKnnnn")]</c> and <c>[Experimental("CESDKnnnn")]</c>. The C# compiler raises them in
///     consumer code, so, like the analyzer descriptors that <c>DiagnosticCatalogTests</c> covers, each one needs a
///     documentation page, a row in <c>analyzers/docs/README.md</c>, the repository help-link format and an identifier
///     from its reserved range (shared-contracts section 3.2: <c>5xxx</c> experimental gates, <c>7xxx</c> obsoletions).
/// </summary>
/// <remarks>
///     This project has no ProjectReference by design, so the attributes are read from the committed sources of
///     <c>libs/</c> and <c>src/</c> rather than by reflection over built assemblies.
/// </remarks>
public sealed partial class ApiDiagnosticIdTests
{
	private const string HelpUrlFormat = "https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/analyzers/docs/{0}.md";

	/// <summary>
	///     Reviewed obsoletions outside the <c>7xxx</c> range: the legacy Lua registration pair shares its usage analyzer
	///     identifier (shared-contracts section 3.3, S-REG-ANALYZER, CESDK1006).
	/// </summary>
	private static readonly HashSet<string> s_reviewedObsoleteIdsOutsideTheRange = new(StringComparer.Ordinal)
	{
		"CESDK1006"
	};

	[Fact]
	public void the_source_scan_finds_the_declared_api_diagnostic_ids()
	{
		IReadOnlyList<ApiDiagnosticId> ids = ScanApiDiagnosticIds();

		Assert.Contains(ids, static id => id is { Kind: ApiDiagnosticKind.Obsolete, Id: "CESDK7001" } &&
			string.Equals(id.File, "libs/CheatEngine.SDK.Engine/Runtime/PointerSize.cs", StringComparison.Ordinal));
	}

	[Fact]
	public void every_obsolete_or_experimental_diagnostic_id_has_a_documentation_page_and_a_readme_row()
	{
		string readme = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "analyzers", "docs", "README.md"));
		List<string> offenders = [];
		foreach (ApiDiagnosticId id in ScanApiDiagnosticIds())
		{
			string page = Path.Combine(RepositoryRoot.Path, "analyzers", "docs", id.Id + ".md");
			if (!File.Exists(page))
			{
				offenders.Add($"{id.File}: {id.Id} has no page analyzers/docs/{id.Id}.md.");
			}
			else if (!File.ReadAllText(page).StartsWith("# " + id.Id + ":", StringComparison.Ordinal))
			{
				offenders.Add($"analyzers/docs/{id.Id}.md does not start with '# {id.Id}:'.");
			}

			if (!readme.Contains($"| [{id.Id}]({id.Id}.md) |", StringComparison.Ordinal))
			{
				offenders.Add($"analyzers/docs/README.md has no table row for {id.Id}.");
			}
		}

		Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
	}

	[Fact]
	public void every_obsolete_or_experimental_diagnostic_id_uses_the_repository_help_url_format()
	{
		List<string> offenders = [];
		foreach (ApiDiagnosticId id in ScanApiDiagnosticIds())
		{
			if (!string.Equals(id.UrlFormat, HelpUrlFormat, StringComparison.Ordinal))
			{
				offenders.Add($"{id.File}: {id.Id} has UrlFormat '{id.UrlFormat ?? "(none)"}', expected '{HelpUrlFormat}'.");
			}
		}

		Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
	}

	[Fact]
	public void obsolete_ids_are_in_the_7xxx_range_and_experimental_ids_in_the_5xxx_range()
	{
		List<string> offenders = [];
		foreach (ApiDiagnosticId id in ScanApiDiagnosticIds())
		{
			char rangeDigit = id.Id["CESDK".Length];
			bool inRange = id.Kind == ApiDiagnosticKind.Obsolete
				? rangeDigit == '7' || s_reviewedObsoleteIdsOutsideTheRange.Contains(id.Id)
				: rangeDigit == '5';
			if (!inRange)
			{
				offenders.Add($"{id.File}: {id.Kind} id {id.Id} is outside its reserved range.");
			}
		}

		Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
	}

	[Fact]
	public void the_scan_reads_both_attribute_shapes_and_ignores_obsoletions_without_an_identifier()
	{
		const string Source = """
		                      [Obsolete("old", DiagnosticId = "CESDK7999", UrlFormat = "https://example.invalid/{0}")]
		                      public static void A() { }
		                      [Obsolete("no identifier")]
		                      public static void B() { }
		                      [Experimental("CESDK5999", UrlFormat = "https://example.invalid/{0}.md")]
		                      public static void C() { }
		                      [System.Diagnostics.CodeAnalysis.Experimental("CESDK5998")]
		                      public static void D() { }
		                      """;

		List<ApiDiagnosticId> ids = [.. Scan("sample.cs", Source)];

		Assert.Equal(3, ids.Count);
		Assert.Equal(new ApiDiagnosticId("sample.cs", ApiDiagnosticKind.Obsolete, "CESDK7999", "https://example.invalid/{0}"),
			ids[0]);
		Assert.Equal(new ApiDiagnosticId("sample.cs", ApiDiagnosticKind.Experimental, "CESDK5999",
			"https://example.invalid/{0}.md"), ids[1]);
		Assert.Equal(new ApiDiagnosticId("sample.cs", ApiDiagnosticKind.Experimental, "CESDK5998", null), ids[2]);
	}

	private static List<ApiDiagnosticId> ScanApiDiagnosticIds()
	{
		List<ApiDiagnosticId> ids = [];
		foreach (string file in RepositoryRoot.EnumerateSourceFiles("*.cs"))
		{
			if (!file.StartsWith("libs/", StringComparison.Ordinal) && !file.StartsWith("src/", StringComparison.Ordinal))
			{
				continue;
			}

			ids.AddRange(Scan(file, File.ReadAllText(Path.Combine(RepositoryRoot.Path, file))));
		}

		return ids;
	}

	private static IEnumerable<ApiDiagnosticId> Scan(string file, string source)
	{
		foreach (Match attribute in AttributePattern().Matches(source))
		{
			string arguments = attribute.Groups["arguments"].Value;
			bool experimental = attribute.Groups["name"].Value.EndsWith("Experimental", StringComparison.Ordinal);
			Match id = experimental ? ExperimentalIdPattern().Match(arguments) : ObsoleteIdPattern().Match(arguments);
			if (!id.Success)
			{
				continue;
			}

			Match url = UrlFormatPattern().Match(arguments);
			yield return new ApiDiagnosticId(file,
				experimental ? ApiDiagnosticKind.Experimental : ApiDiagnosticKind.Obsolete, id.Groups["id"].Value,
				url.Success ? url.Groups["url"].Value : null);
		}
	}

	[GeneratedRegex(
		@"\[\s*(?<name>(?:global::)?(?:System\.)?Obsolete|(?:global::)?(?:System\.Diagnostics\.CodeAnalysis\.)?Experimental)(?:Attribute)?\s*\((?<arguments>.*?)\)\s*\]",
		RegexOptions.Singleline | RegexOptions.CultureInvariant, 1000)]
	private static partial Regex AttributePattern();

	[GeneratedRegex("""DiagnosticId\s*=\s*"(?<id>CESDK\d{4})"\s*""", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex ObsoleteIdPattern();

	[GeneratedRegex("""^\s*"(?<id>CESDK\d{4})"\s*""", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex ExperimentalIdPattern();

	[GeneratedRegex("""UrlFormat\s*=\s*"(?<url>[^"]*)"\s*""", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex UrlFormatPattern();

	private enum ApiDiagnosticKind
	{
		Unknown = 0,
		Obsolete = 1,
		Experimental = 2
	}

	private sealed record ApiDiagnosticId(string File, ApiDiagnosticKind Kind, string Id, string? UrlFormat);
}
