using System.Globalization;
using System.Text.RegularExpressions;

namespace CheatEngine.SDK.Repository.Tests.Documentation;

/// <summary>
///     The documentation rules. Each one is a function of a parsed page (and, for links, of the working tree) that returns
///     every offender as <c>path:line -&gt; target (reason)</c>. <see cref="DocumentationIntegrityTests" /> applies them to
///     every Markdown file of the repository; <see cref="MarkdownDocumentTests" /> applies them to in-memory pages, which
///     proves that each gate fails on the regression it exists for.
/// </summary>
internal static class DocumentationRules
{
	private const string Arrow = " \u2192 ";

	private const string RetiredTreeReason =
		"the retired documentations/ tree is not restored; re-point the link as docs/README.md#retired-documentation lists";

	/// <summary>Trailing characters that GitHub does not include in a bare URL.</summary>
	private static readonly char[] AutolinkTrailingPunctuation = ['.', ',', ':', ';', '!', '?', '*', '_', '~'];

	/// <summary>Relative targets (links, images, reference definitions, HTML attributes) that do not exist with exact case.</summary>
	public static List<string> BrokenRelativeLinks(string documentPath, MarkdownDocument document)
	{
		ArgumentNullException.ThrowIfNull(document);
		List<string> offenders = [];
		foreach (MarkdownLink link in document.Links)
		{
			LinkTarget target = LinkTarget.Parse(link.Target);
			if (target.Kind != LinkTargetKind.Relative)
			{
				continue;
			}

			if (!RepositoryPaths.TryResolve(documentPath, target.Path, out string resolved, out string? problem))
			{
				offenders.Add(Offender(documentPath, link.Line, link.Target, problem!));
				continue;
			}

			PathLookup lookup = RepositoryPaths.Lookup(resolved);
			if (!lookup.Exists)
			{
				offenders.Add(Offender(documentPath, link.Line, link.Target, lookup.Problem!));
			}
		}

		return offenders;
	}

	/// <summary>
	///     Fragments that match no heading anchor or explicit anchor of their page: <c>#frag</c> on the same page,
	///     <c>page.md#frag</c>, or <c>folder/#frag</c> (the folder's rendered README). Targets that do not resolve are left to
	///     <see cref="BrokenRelativeLinks" />; fragments of non-Markdown files (source line anchors) are not checked.
	/// </summary>
	public static List<string> BrokenAnchors(string documentPath, MarkdownDocument document)
	{
		ArgumentNullException.ThrowIfNull(document);
		List<string> offenders = [];
		foreach (MarkdownLink link in document.Links)
		{
			LinkTarget target = LinkTarget.Parse(link.Target);
			if (string.IsNullOrEmpty(target.Fragment))
			{
				continue;
			}

			string page;
			IReadOnlySet<string>? anchors;
			if (target.Kind == LinkTargetKind.SameDocument)
			{
				page = documentPath;
				anchors = document.Anchors;
			}
			else if (target.Kind == LinkTargetKind.Relative
					 && RepositoryPaths.TryResolve(documentPath, target.Path, out string resolved, out _)
					 && RepositoryPaths.Lookup(resolved) is { Exists: true } lookup)
			{
				page = lookup.IsDirectory ? Join(resolved, "README.md") : resolved;
				if (!page.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				anchors = MarkdownCorpus.AnchorsOf(page);
			}
			else
			{
				continue;
			}

			if (anchors is null)
			{
				offenders.Add(Offender(documentPath, link.Line, link.Target,
					$"the folder has no README.md to hold '#{target.Fragment}'"));
			}
			else if (!anchors.Contains(target.Fragment))
			{
				offenders.Add(Offender(documentPath, link.Line, link.Target,
					$"no heading or explicit anchor '#{target.Fragment}' in '{page}'"));
			}
		}

		return offenders;
	}

	/// <summary>Absolute local paths anywhere in the page (audit F14: no reader needs the author's local folder).</summary>
	public static List<string> LocalPaths(string documentPath, MarkdownDocument document)
	{
		ArgumentNullException.ThrowIfNull(document);
		List<string> offenders = [];
		foreach (MarkdownTextMatch match in document.FindLocalPaths())
		{
			offenders.Add(Offender(documentPath, match.Line, match.Value,
				"absolute local path; write a repository-relative path or a placeholder such as %APPDATA%"));
		}

		return offenders;
	}

	/// <summary>
	///     Links into the retired <c>documentations/</c> tree (never allowed), and, outside
	///     <see cref="DocumentationConventions.RetiredReferenceExemptions" />, any text naming a retired page.
	/// </summary>
	public static List<string> RetiredReferences(string documentPath, MarkdownDocument document)
	{
		ArgumentNullException.ThrowIfNull(document);
		List<string> offenders = [];
		foreach (MarkdownLink link in document.Links)
		{
			if (link.Target.Contains(DocumentationConventions.RetiredTreePrefix, StringComparison.Ordinal))
			{
				offenders.Add(Offender(documentPath, link.Line, link.Target, RetiredTreeReason));
			}
		}

		if (Array.IndexOf(DocumentationConventions.RetiredReferenceExemptions, documentPath) >= 0)
		{
			return offenders;
		}

		foreach (MarkdownTextMatch match in document.FindAll(DocumentationConventions.RetiredReferencePattern))
		{
			offenders.Add(Offender(documentPath, match.Line, match.Value,
				"names a retired page; only docs/README.md lists retired paths"));
		}

		return offenders;
	}

	/// <summary>
	///     Absolute <c>blob/main</c> and <c>tree/main</c> links to this repository that do not resolve on the current tree
	///     (exact case, and the anchor when the target is Markdown). Templates such as <c>&lt;ID&gt;</c> are skipped.
	/// </summary>
	public static List<string> BrokenSelfLinks(string documentPath, MarkdownDocument document)
	{
		ArgumentNullException.ThrowIfNull(document);
		List<string> offenders = [];
		foreach (MarkdownTextMatch match in document.FindAll(DocumentationConventions.SelfLinkPattern))
		{
			if (TryCheckSelfLink(match.Value, out string? problem) && problem is not null)
			{
				offenders.Add(Offender(documentPath, match.Line, match.Value, problem));
			}
		}

		return offenders;
	}

	/// <summary>
	///     Checks one absolute link to this repository on the main branch. Returns <see langword="false" /> when the URL is
	///     not such a link or is a template; otherwise <paramref name="problem" /> is <see langword="null" /> when the path
	///     (and its anchor, for Markdown) resolves on the current tree.
	/// </summary>
	public static bool TryCheckSelfLink(string url, out string? problem)
	{
		ArgumentNullException.ThrowIfNull(url);
		problem = null;
		Match match = DocumentationConventions.SelfLinkPattern.Match(url);
		if (!match.Success)
		{
			return false;
		}

		string rest = match.Groups["rest"].Value.TrimEnd(AutolinkTrailingPunctuation);
		if (rest.AsSpan().IndexOfAny("<{*") >= 0)
		{
			return false;
		}

		int hash = rest.IndexOf('#', StringComparison.Ordinal);
		string pathPart = hash < 0 ? rest : rest[..hash];
		string? fragment = hash < 0 ? null : Uri.UnescapeDataString(rest[(hash + 1)..]);
		int query = pathPart.IndexOf('?', StringComparison.Ordinal);
		if (query >= 0)
		{
			pathPart = pathPart[..query];
		}

		string path = Uri.UnescapeDataString(pathPart).Trim('/');
		PathLookup lookup = RepositoryPaths.Lookup(path);
		if (!lookup.Exists)
		{
			problem = lookup.Problem;
			return true;
		}

		if (string.IsNullOrEmpty(fragment))
		{
			return true;
		}

		string page = lookup.IsDirectory ? Join(path, "README.md") : path;
		if (!page.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		IReadOnlySet<string>? anchors = MarkdownCorpus.AnchorsOf(page);
		if (anchors is null || !anchors.Contains(fragment))
		{
			problem = $"no heading or explicit anchor '#{fragment}' in '{page}'";
		}

		return true;
	}

	/// <summary>
	///     Targets of a packed README that are not absolute <c>https://</c> URLs. nuget.org renders the README outside the
	///     repository: relative links and images do not resolve there
	///     (https://learn.microsoft.com/nuget/nuget-org/package-readme-on-nuget-org#allowed-domains-for-images-and-badges).
	/// </summary>
	public static List<string> NonAbsoluteLinksInPackedReadme(string documentPath, MarkdownDocument document)
	{
		ArgumentNullException.ThrowIfNull(document);
		List<string> offenders = [];
		foreach (MarkdownLink link in document.Links)
		{
			if (!link.Target.StartsWith("https://", StringComparison.Ordinal))
			{
				offenders.Add(Offender(documentPath, link.Line, link.Target,
					"a packed README is rendered on nuget.org, where only absolute https:// targets resolve"));
			}
		}

		return offenders;
	}

	/// <summary>A page under <c>docs/</c> whose first two non-blank lines do not include the recreated header.</summary>
	public static List<string> MissingRecreatedHeader(string documentPath, MarkdownDocument document)
	{
		ArgumentNullException.ThrowIfNull(document);
		int seen = 0;
		for (int n = 0; n < document.Lines.Count && seen < 2; n++)
		{
			string line = document.Lines[n].TrimEnd();
			if (line.Length == 0)
			{
				continue;
			}

			if (string.Equals(line, DocumentationConventions.RecreatedHeader, StringComparison.Ordinal))
			{
				return [];
			}

			seen++;
		}

		return
		[
			Offender(documentPath, 1, "header",
				$"one of the first two non-blank lines must be exactly '{DocumentationConventions.RecreatedHeader}'")
		];
	}

	/// <summary>Placeholder status lines, outside fenced code, that do not name their owning lot and wave.</summary>
	public static List<string> MalformedPlaceholders(string documentPath, MarkdownDocument document)
	{
		ArgumentNullException.ThrowIfNull(document);
		List<string> offenders = [];
		for (int n = 0; n < document.Lines.Count; n++)
		{
			string line = document.Lines[n].Trim();
			if (document.IsInFencedCode(n + 1)
				|| !line.StartsWith(DocumentationConventions.PlaceholderPrefix, StringComparison.OrdinalIgnoreCase)
				|| DocumentationConventions.PlaceholderPattern.IsMatch(line))
			{
				continue;
			}

			offenders.Add(Offender(documentPath, n + 1, line,
				"write 'Status: placeholder \u2014 content arrives with <lot> (<wave>)', for example 'S-QUAL (V1)'"));
		}

		return offenders;
	}

	/// <summary>
	///     Claims of complete coverage or universal support outside fenced code, unless a negation comes earlier on the
	///     same line (audit A00-03, A20-14).
	/// </summary>
	public static List<string> UniversalClaims(string documentPath, MarkdownDocument document)
	{
		ArgumentNullException.ThrowIfNull(document);
		List<string> offenders = [];
		for (int n = 0; n < document.Lines.Count; n++)
		{
			if (document.IsInFencedCode(n + 1))
			{
				continue;
			}

			string line = document.Lines[n];
			foreach (Match claim in DocumentationConventions.UniversalClaimPattern.Matches(line))
			{
				if (!DocumentationConventions.NegationPattern.IsMatch(line[..claim.Index]))
				{
					offenders.Add(Offender(documentPath, n + 1, claim.Value,
						"claims complete coverage or universal support; state the measured scope and profile instead"));
				}
			}
		}

		return offenders;
	}

	/// <summary>
	///     Top-level entries of <c>docs/</c> (other than the index) that no link of the index targets, directly or through
	///     a path below them.
	/// </summary>
	public static List<string> UnlinkedDocsEntries(MarkdownDocument index, IEnumerable<string> topLevelEntries)
	{
		ArgumentNullException.ThrowIfNull(index);
		ArgumentNullException.ThrowIfNull(topLevelEntries);
		List<string> targets = [];
		foreach (MarkdownLink link in index.Links)
		{
			LinkTarget target = LinkTarget.Parse(link.Target);
			if (target.Kind == LinkTargetKind.Relative
				&& RepositoryPaths.TryResolve(DocumentationConventions.DocsIndex, target.Path, out string resolved, out _))
			{
				targets.Add(resolved);
			}
		}

		List<string> offenders = [];
		foreach (string name in topLevelEntries)
		{
			string entry = DocumentationConventions.DocsFolder + "/" + name;
			bool linked = false;
			foreach (string target in targets)
			{
				if (string.Equals(target, entry, StringComparison.Ordinal)
					|| target.StartsWith(entry + "/", StringComparison.Ordinal))
				{
					linked = true;
					break;
				}
			}

			if (!linked)
			{
				offenders.Add(Offender(DocumentationConventions.DocsIndex, 1, entry,
					"add a row for it to the Contents table of the docs index"));
			}
		}

		return offenders;
	}

	/// <summary>Formats one offender as <c>path:line -&gt; target (reason)</c>.</summary>
	public static string Offender(string documentPath, int line, string target, string reason)
	{
		return string.Create(CultureInfo.InvariantCulture, $"{documentPath}:{line}{Arrow}{target} ({reason})");
	}

	private static string Join(string folder, string file)
	{
		return folder.Length == 0 ? file : folder + "/" + file;
	}
}
