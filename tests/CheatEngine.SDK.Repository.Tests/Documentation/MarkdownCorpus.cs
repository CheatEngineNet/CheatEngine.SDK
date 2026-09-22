using System.Collections.Concurrent;
using System.Text;

using CheatEngine.SDK.Repository.Tests.Infrastructure;

namespace CheatEngine.SDK.Repository.Tests.Documentation;

/// <summary>
///     Every Markdown file of the working tree, parsed once. The enumeration reads the file system rather than
///     <c>git ls-files</c> (this project never starts a process) and skips build output and tool state, so it matches the
///     tracked files in a clean checkout.
/// </summary>
internal static class MarkdownCorpus
{
	private static readonly Lazy<SortedDictionary<string, MarkdownDocument>> LazyDocuments = new(Load);

	private static readonly ConcurrentDictionary<string, MarkdownDocument?> OutsideCorpus = new(StringComparer.Ordinal);

	/// <summary>Documents keyed by repository-relative path, in ordinal order.</summary>
	public static IReadOnlyDictionary<string, MarkdownDocument> Documents => LazyDocuments.Value;

	/// <summary>The anchors of a Markdown file, or <see langword="null" /> when it does not exist.</summary>
	public static IReadOnlySet<string>? AnchorsOf(string repoRelativePath)
	{
		if (Documents.TryGetValue(repoRelativePath, out MarkdownDocument? document))
		{
			return document.Anchors;
		}

		MarkdownDocument? outside = OutsideCorpus.GetOrAdd(repoRelativePath, static path =>
			RepositoryPaths.ExistsWithExactCase(path) ? Read(path) : null);
		return outside?.Anchors;
	}

	/// <summary>Whether a repository-relative file is a Markdown document of the corpus.</summary>
	public static bool IsDocumentation(string repoRelativePath)
	{
		if (!repoRelativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		foreach (string segment in repoRelativePath.Split('/'))
		{
			foreach (string excluded in DocumentationConventions.ExtraExcludedSegments)
			{
				if (string.Equals(segment, excluded, StringComparison.OrdinalIgnoreCase))
				{
					return false;
				}
			}
		}

		return true;
	}

	private static SortedDictionary<string, MarkdownDocument> Load()
	{
		SortedDictionary<string, MarkdownDocument> documents = new(StringComparer.Ordinal);
		foreach (string file in RepositoryRoot.EnumerateSourceFiles("*.md"))
		{
			if (IsDocumentation(file))
			{
				documents.Add(file, Read(file));
			}
		}

		return documents;
	}

	private static MarkdownDocument Read(string repoRelativePath)
	{
		string text = File.ReadAllText(Path.Combine(RepositoryRoot.Path, repoRelativePath), Encoding.UTF8);
		return MarkdownDocument.Parse(text);
	}
}
