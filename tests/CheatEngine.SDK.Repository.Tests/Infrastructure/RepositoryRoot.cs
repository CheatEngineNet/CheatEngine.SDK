namespace CheatEngine.SDK.Repository.Tests.Infrastructure;

/// <summary>Locates the repository from the test output directory.</summary>
internal static class RepositoryRoot
{
	private const string SolutionFileName = "CheatEngine.SDK.slnx";

	/// <summary>The directory that contains <c>CheatEngine.SDK.slnx</c>, found by walking up from the test binaries.</summary>
	public static string Path
	{
		get;
	} = FindRoot();

	/// <summary>The solution file.</summary>
	public static string SolutionPath => System.IO.Path.Combine(Path, SolutionFileName);

	/// <summary>Converts an absolute path below the repository root to a forward-slash relative path.</summary>
	public static string ToRelative(string absolutePath)
	{
		return System.IO.Path.GetRelativePath(Path, absolutePath).Replace('\\', '/');
	}

	/// <summary>Enumerates files below the repository root, skipping build output and tool state folders.</summary>
	public static IEnumerable<string> EnumerateSourceFiles(string searchPattern)
	{
		foreach (string file in Directory.EnumerateFiles(Path, searchPattern, SearchOption.AllDirectories))
		{
			string relative = ToRelative(file);
			if (IsExcluded(relative))
			{
				continue;
			}

			yield return relative;
		}
	}

	private static bool IsExcluded(string relativePath)
	{
		foreach (string segment in relativePath.Split('/'))
		{
			if (segment is "artifacts" or "bin" or "obj" or ".git" or ".idea" or ".vs" or "TestResults")
			{
				return true;
			}
		}

		return false;
	}

	private static string FindRoot()
	{
		for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
		     directory is not null;
		     directory = directory.Parent)
		{
			if (File.Exists(System.IO.Path.Combine(directory.FullName, SolutionFileName)))
			{
				return directory.FullName;
			}
		}

		throw new InvalidOperationException(
			$"'{SolutionFileName}' was not found above '{AppContext.BaseDirectory}': the tests expect to run from the repository's artifacts directory.");
	}
}
