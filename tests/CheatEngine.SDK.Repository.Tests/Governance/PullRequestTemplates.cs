using CheatEngine.SDK.Repository.Tests.Infrastructure;

namespace CheatEngine.SDK.Repository.Tests.Governance;

/// <summary>
///     The pull request templates GitHub prefills a description with: <c>PULL_REQUEST_TEMPLATE</c> (any case, any
///     extension) in the repository root, <c>.github/</c> or <c>docs/</c>, and every file of a
///     <c>PULL_REQUEST_TEMPLATE/</c> folder there.
///     https://docs.github.com/en/communities/using-templates-to-encourage-useful-issues-and-pull-requests/creating-a-pull-request-template-for-your-repository
/// </summary>
internal static class PullRequestTemplates
{
	private const string TemplateName = "PULL_REQUEST_TEMPLATE";

	/// <summary>Repository-relative paths (forward slashes) of every template file, sorted ordinally.</summary>
	public static List<string> Find()
	{
		List<string> templates = [];
		foreach (string folder in (string[]) ["", ".github", "docs"])
		{
			string directory = folder.Length == 0 ? RepositoryRoot.Path : Path.Combine(RepositoryRoot.Path, folder);
			if (!Directory.Exists(directory))
			{
				continue;
			}

			foreach (string file in Directory.EnumerateFiles(directory))
			{
				if (string.Equals(Path.GetFileNameWithoutExtension(file), TemplateName, StringComparison.OrdinalIgnoreCase))
				{
					templates.Add(RepositoryRoot.ToRelative(file));
				}
			}

			foreach (string subdirectory in Directory.EnumerateDirectories(directory))
			{
				if (string.Equals(Path.GetFileName(subdirectory), TemplateName, StringComparison.OrdinalIgnoreCase))
				{
					foreach (string file in Directory.EnumerateFiles(subdirectory))
					{
						templates.Add(RepositoryRoot.ToRelative(file));
					}
				}
			}
		}

		templates.Sort(StringComparer.Ordinal);
		return templates;
	}
}
