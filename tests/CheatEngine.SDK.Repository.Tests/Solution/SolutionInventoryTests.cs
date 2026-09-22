using CheatEngine.SDK.Repository.Tests.Infrastructure;

namespace CheatEngine.SDK.Repository.Tests.Solution;

/// <summary>The solution is the single inventory CI builds and tests; a project outside it is never compiled.</summary>
public sealed class SolutionInventoryTests
{
	/// <summary>Projects deliberately kept out of the solution, with the reason. Adding one is a review decision.</summary>
	private static readonly Dictionary<string, string> s_outOfSolution = new(StringComparer.Ordinal)
	{
		["tests/CheatEngine.SDK.AotProbe/CheatEngine.SDK.AotProbe.csproj"] =
			"Native AOT executable probe, restored and published on its own by the CI aot job.",
		["tests/CheatEngine.SDK.LiveProbe/CheatEngine.SDK.LiveProbe.csproj"] =
			"Manually loaded CE 7.7 evidence harness; the qualification work brings it into the solution."
	};

	[Fact]
	public void Every_project_on_disk_is_in_the_solution_or_explicitly_excluded()
	{
		HashSet<string> listed = ReadSolutionProjects();
		List<string> missing = [];
		foreach (string project in RepositoryRoot.EnumerateSourceFiles("*.csproj"))
		{
			if (!listed.Contains(project) && !s_outOfSolution.ContainsKey(project))
			{
				missing.Add(project);
			}
		}

		Assert.True(missing.Count == 0,
			$"Add these projects to CheatEngine.SDK.slnx (dotnet sln add) or justify them in {nameof(s_outOfSolution)}: {string.Join(", ", missing)}");
	}

	[Fact]
	public void Every_project_in_the_solution_exists_and_no_exclusion_is_stale()
	{
		HashSet<string> listed = ReadSolutionProjects();
		foreach (string project in listed)
		{
			Assert.True(File.Exists(Path.Combine(RepositoryRoot.Path, project)),
				$"CheatEngine.SDK.slnx lists '{project}', which does not exist.");
		}

		foreach (string excluded in s_outOfSolution.Keys)
		{
			Assert.True(File.Exists(Path.Combine(RepositoryRoot.Path, excluded)),
				$"The exclusion '{excluded}' names a project that no longer exists.");
			Assert.False(listed.Contains(excluded),
				$"'{excluded}' is in the solution now; remove it from {nameof(s_outOfSolution)}.");
		}
	}

	private static HashSet<string> ReadSolutionProjects()
	{
		XDocument solution = XDocument.Load(RepositoryRoot.SolutionPath);
		HashSet<string> projects = new(StringComparer.Ordinal);
		foreach (XElement project in solution.Descendants("Project"))
		{
			string? path = (string?) project.Attribute("Path");
			if (path is not null)
			{
				projects.Add(path.Replace('\\', '/'));
			}
		}

		return projects;
	}
}
