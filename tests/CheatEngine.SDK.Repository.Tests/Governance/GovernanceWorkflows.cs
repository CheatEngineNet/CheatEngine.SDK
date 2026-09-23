namespace CheatEngine.SDK.Repository.Tests.Governance;

/// <summary>The advisory governance workflows of the repository, outside <c>CI / Gate</c>.</summary>
internal static class GovernanceWorkflows
{
	public const string CodeQl = ".github/workflows/codeql.yml";
	public const string Scorecard = ".github/workflows/scorecard.yml";
	public const string ZizmorOnline = ".github/workflows/zizmor-online.yml";
	public const string DependencySubmission = ".github/workflows/dependency-submission.yml";

	/// <summary>The governance workflows, which never join <c>gate.needs</c>.</summary>
	public static readonly string[] All = [CodeQl, Scorecard, ZizmorOnline, DependencySubmission];

	/// <summary>Every workflow file, repository-relative, sorted.</summary>
	public static List<string> AllWorkflowPaths()
	{
		List<string> paths = [];
		foreach (string pattern in (string[]) ["*.yml", "*.yaml"])
		{
			foreach (string file in Directory.EnumerateFiles(RepositoryFile.FullPath(".github/workflows"), pattern))
			{
				paths.Add(".github/workflows/" + Path.GetFileName(file));
			}
		}

		paths.Sort(StringComparer.Ordinal);
		return paths;
	}

	/// <summary>The governance workflows that exist in the working tree.</summary>
	public static List<string> Existing()
	{
		List<string> existing = [];
		foreach (string path in All)
		{
			if (File.Exists(RepositoryFile.FullPath(path)))
			{
				existing.Add(path);
			}
		}

		return existing;
	}
}
