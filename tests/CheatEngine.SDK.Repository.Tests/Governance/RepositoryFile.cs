using CheatEngine.SDK.Repository.Tests.Infrastructure;

namespace CheatEngine.SDK.Repository.Tests.Governance;

/// <summary>Reads committed files by repository-relative path (forward slashes).</summary>
internal static class RepositoryFile
{
	/// <summary>The absolute path of a repository-relative path.</summary>
	public static string FullPath(string relativePath)
	{
		return Path.Combine(RepositoryRoot.Path, relativePath.Replace('/', Path.DirectorySeparatorChar));
	}

	/// <summary>The text of a committed file; fails the test when it does not exist.</summary>
	public static string ReadText(string relativePath)
	{
		string fullPath = FullPath(relativePath);
		Assert.True(File.Exists(fullPath), $"'{relativePath}' does not exist.");
		return File.ReadAllText(fullPath);
	}

	/// <summary>The lines of a committed file, without line terminators.</summary>
	public static string[] ReadLines(string relativePath)
	{
		return ReadText(relativePath).ReplaceLineEndings("\n").Split('\n');
	}

	/// <summary>
	///     True when the repository-relative path exists with exactly this case for every segment. Windows file systems
	///     ignore case, GitHub does not, so <see cref="File.Exists" /> alone would accept a wrong spelling.
	/// </summary>
	public static bool ExistsWithExactCase(string relativePath, out bool isDirectory)
	{
		isDirectory = false;
		string current = RepositoryRoot.Path;
		string[] segments = relativePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < segments.Length; i++)
		{
			string? match = null;
			foreach (string entry in Directory.EnumerateFileSystemEntries(current))
			{
				if (string.Equals(Path.GetFileName(entry), segments[i], StringComparison.Ordinal))
				{
					match = entry;
				}
			}

			if (match is null)
			{
				return false;
			}

			current = match;
		}

		isDirectory = Directory.Exists(current);
		return true;
	}
}
