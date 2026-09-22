using CheatEngine.SDK.Repository.Tests.Infrastructure;

namespace CheatEngine.SDK.Repository.Tests.Documentation;

/// <summary>
///     Every Markdown file of the repository stays readable and verifiable from the repository alone (audit F14, ADR-12):
///     links and anchors resolve with GitHub's exact case, no page depends on a local folder or on the retired
///     <c>documentations/</c> tree, the packed README only uses absolute links, the links of the published 1.0.0 README
///     keep resolving, and the rebuilt <c>docs/</c> pages carry their header and an honest status. Offline and fast: the
///     tests read files only and never start a process or reach the network.
/// </summary>
public sealed class DocumentationIntegrityTests
{
	[Fact]
	public void Every_relative_markdown_link_resolves_with_exact_casing()
	{
		List<string> offenders = [];
		int relativeTargets = 0;
		foreach ((string path, MarkdownDocument document) in Corpus())
		{
			foreach (MarkdownLink link in document.Links)
			{
				if (LinkTarget.Parse(link.Target).Kind == LinkTargetKind.Relative)
				{
					relativeTargets++;
				}
			}

			offenders.AddRange(DocumentationRules.BrokenRelativeLinks(path, document));
		}

		Assert.True(relativeTargets > 0, "No relative Markdown link was found: the corpus enumeration is broken.");
		AssertNone(offenders, "These relative targets do not exist with this exact case on GitHub");
	}

	[Fact]
	public void Every_markdown_anchor_matches_a_heading_of_its_target_page()
	{
		List<string> offenders = [];
		foreach ((string path, MarkdownDocument document) in Corpus())
		{
			offenders.AddRange(DocumentationRules.BrokenAnchors(path, document));
		}

		AssertNone(offenders, "These fragments match no heading anchor (GitHub slug rules) or explicit anchor");
	}

	[Fact]
	public void No_markdown_file_contains_an_absolute_local_path()
	{
		List<string> offenders = [];
		foreach ((string path, MarkdownDocument document) in Corpus())
		{
			offenders.AddRange(DocumentationRules.LocalPaths(path, document));
		}

		AssertNone(offenders, "Documentation must not need anyone's local folder to be understood");
	}

	[Fact]
	public void No_markdown_file_refers_to_the_retired_documentations_tree()
	{
		List<string> offenders = [];
		foreach ((string path, MarkdownDocument document) in Corpus())
		{
			offenders.AddRange(DocumentationRules.RetiredReferences(path, document));
		}

		AssertNone(offenders, "The documentations/ tree and the bridge AUDIT.md were retired and are not restored");
	}

	[Fact]
	public void Absolute_links_to_this_repository_on_main_resolve_on_the_current_tree()
	{
		List<string> offenders = [];
		int selfLinks = 0;
		foreach ((string path, MarkdownDocument document) in Corpus())
		{
			selfLinks += document.FindAll(DocumentationConventions.SelfLinkPattern).Count;
			offenders.AddRange(DocumentationRules.BrokenSelfLinks(path, document));
		}

		Assert.True(selfLinks > 0, "No blob/main or tree/main link to this repository was found: the pattern is broken.");
		AssertNone(offenders, "These absolute links to this repository on main do not resolve on the current tree");
	}

	[Fact]
	public void Links_of_the_published_1_0_0_package_readme_still_resolve_on_main()
	{
		List<string> offenders = [];
		foreach (string url in DocumentationConventions.PublishedPackageReadmeLinks)
		{
			bool checkedLink = DocumentationRules.TryCheckSelfLink(url, out string? problem);
			Assert.True(checkedLink, $"'{url}' is no longer recognised as a link to this repository on main.");
			if (problem is not null)
			{
				offenders.Add($"{url} ({problem})");
			}
		}

		AssertNone(offenders,
			"The README packed into CheatEngine.SDK 1.0.0 on nuget.org links these paths on main; keep them resolving");
	}

	[Fact]
	public void Packed_readmes_contain_only_absolute_links()
	{
		IReadOnlyList<string> readmes = RepositoryPaths.PackedReadmes();
		Assert.True(readmes.Count > 0, "No packable project was found, so no packed README was checked.");

		List<string> offenders = [];
		foreach (string readme in readmes)
		{
			if (!MarkdownCorpus.Documents.TryGetValue(readme, out MarkdownDocument? document))
			{
				offenders.Add(DocumentationRules.Offender(readme, 1, readme, "the packed README does not exist"));
				continue;
			}

			offenders.AddRange(DocumentationRules.NonAbsoluteLinksInPackedReadme(readme, document));
		}

		AssertNone(offenders, "Packed READMEs must use absolute https:// links");
	}

	[Fact]
	public void Every_rebuilt_docs_page_starts_with_the_recreated_header()
	{
		List<string> offenders = [];
		int pages = 0;
		foreach ((string path, MarkdownDocument document) in Corpus())
		{
			if (!path.StartsWith(DocumentationConventions.DocsFolder + "/", StringComparison.Ordinal))
			{
				continue;
			}

			pages++;
			offenders.AddRange(DocumentationRules.MissingRecreatedHeader(path, document));
		}

		Assert.True(pages > 0, $"No page was found under {DocumentationConventions.DocsFolder}/.");
		AssertNone(offenders, "Pages rebuilt after the audit say so, instead of passing for restored history");
	}

	[Fact]
	public void Placeholder_pages_name_their_owning_lot_and_wave()
	{
		List<string> offenders = [];
		foreach ((string path, MarkdownDocument document) in Corpus())
		{
			offenders.AddRange(DocumentationRules.MalformedPlaceholders(path, document));
		}

		AssertNone(offenders, "A placeholder names the work that replaces it");
	}

	[Fact]
	public void Docs_index_links_every_top_level_page_and_folder()
	{
		Assert.True(MarkdownCorpus.Documents.TryGetValue(DocumentationConventions.DocsIndex, out MarkdownDocument? index),
			$"{DocumentationConventions.DocsIndex} does not exist.");

		List<string> entries = [];
		foreach (string entry in Directory.EnumerateFileSystemEntries(
					 Path.Combine(RepositoryRoot.Path, DocumentationConventions.DocsFolder)))
		{
			string name = Path.GetFileName(entry);
			if (!string.Equals(name, "README.md", StringComparison.Ordinal) && !name.StartsWith('.'))
			{
				entries.Add(name);
			}
		}

		Assert.True(entries.Count > 0, $"{DocumentationConventions.DocsFolder}/ holds nothing besides its index.");
		AssertNone(DocumentationRules.UnlinkedDocsEntries(index, entries),
			"Every top-level page and folder of docs/ is reachable from its index");
	}

	[Fact]
	public void No_markdown_file_claims_complete_coverage_or_universal_support()
	{
		List<string> offenders = [];
		foreach ((string path, MarkdownDocument document) in Corpus())
		{
			offenders.AddRange(DocumentationRules.UniversalClaims(path, document));
		}

		AssertNone(offenders, "No page claims complete coverage or universal support without a measured denominator");
	}

	private static IReadOnlyDictionary<string, MarkdownDocument> Corpus()
	{
		IReadOnlyDictionary<string, MarkdownDocument> documents = MarkdownCorpus.Documents;
		Assert.True(documents.Count > 0, "No Markdown file was found below the repository root.");
		return documents;
	}

	private static void AssertNone(List<string> offenders, string rule)
	{
		Assert.True(offenders.Count == 0,
			$"{rule} ({offenders.Count}):{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
	}
}
