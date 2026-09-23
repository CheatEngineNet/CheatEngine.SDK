namespace CheatEngine.SDK.Repository.Tests.Governance;

/// <summary>The workflow files of the repository and the advisory governance workflows outside <c>CI / Gate</c>.</summary>
internal static class GovernanceWorkflows
{
	public const string PrPolicy = ".github/workflows/pr-policy.yml";
	public const string CodeQl = ".github/workflows/codeql.yml";
	public const string Scorecard = ".github/workflows/scorecard.yml";
	public const string ZizmorOnline = ".github/workflows/zizmor-online.yml";
	public const string DependencySubmission = ".github/workflows/dependency-submission.yml";
	public const string ScheduledHealth = ".github/workflows/scheduled-health.yml";

	/// <summary>The governance workflows, which never join <c>gate.needs</c> (PR policy is its own required check).</summary>
	public static readonly string[] All = [PrPolicy, CodeQl, Scorecard, ZizmorOnline, DependencySubmission, ScheduledHealth];

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

	/// <summary>The governance workflows that exist in the working tree (each later commit adds one).</summary>
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
