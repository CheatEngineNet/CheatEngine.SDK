using System.Collections.Concurrent;

using CheatEngine.SDK.Repository.Tests.Infrastructure;

namespace CheatEngine.SDK.Repository.Tests.Documentation;

/// <summary>
///     Resolves documentation link targets against the working tree the way GitHub does: case-sensitively. Windows, where
///     CI runs these tests, is case-insensitive, so <c>File.Exists</c> alone would accept <c>readme.md</c> for
///     <c>README.md</c>; every segment is matched against the real directory entries instead.
/// </summary>
internal static class RepositoryPaths
{
	private static readonly ConcurrentDictionary<string, string[]> DirectoryEntries = new(StringComparer.Ordinal);

	/// <summary>Whether a repository-relative path (<c>/</c>-separated) exists with exactly this case.</summary>
	public static bool ExistsWithExactCase(string repoRelativePath)
	{
		return Lookup(repoRelativePath).Exists;
	}

	/// <summary>Looks a repository-relative path up with exact case and explains a miss (absent, or differs in case).</summary>
	public static PathLookup Lookup(string repoRelativePath)
	{
		ArgumentNullException.ThrowIfNull(repoRelativePath);
		string current = RepositoryRoot.Path;
		string matched = string.Empty;
		foreach (string segment in repoRelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
		{
			if (!Directory.Exists(current))
			{
				return new PathLookup(false, false, $"'{matched}' is a file, not a folder");
			}

			string? exact = null;
			string? otherCase = null;
			foreach (string entry in Entries(current))
			{
				if (string.Equals(entry, segment, StringComparison.Ordinal))
				{
					exact = entry;
					break;
				}

				if (string.Equals(entry, segment, StringComparison.OrdinalIgnoreCase))
				{
					otherCase = entry;
				}
			}

			string prefix = matched.Length == 0 ? string.Empty : matched + "/";
			if (exact is null)
			{
				return new PathLookup(false, false,
					otherCase is null
						? $"'{prefix}{segment}' does not exist"
						: $"'{prefix}{segment}' differs in case from '{prefix}{otherCase}'; GitHub paths are case-sensitive");
			}

			matched = prefix + exact;
			current = System.IO.Path.Combine(current, exact);
		}

		return new PathLookup(true, Directory.Exists(current), null);
	}

	/// <summary>
	///     Resolves the path of a <see cref="LinkTargetKind.Relative" /> target written in <paramref name="documentPath" />
	///     to a normalized repository-relative path, without touching the disk. Fails for a target that uses <c>\</c>,
	///     climbs above the repository root, or points into a folder that is never committed.
	/// </summary>
	public static bool TryResolve(string documentPath, string targetPath, out string resolved, out string? problem)
	{
		ArgumentNullException.ThrowIfNull(documentPath);
		ArgumentNullException.ThrowIfNull(targetPath);
		resolved = string.Empty;
		if (targetPath.Contains('\\', StringComparison.Ordinal))
		{
			problem = "uses '\\' as a path separator; GitHub needs '/'";
			return false;
		}

		List<string> segments = [];
		if (!targetPath.StartsWith('/'))
		{
			string[] documentSegments = documentPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < documentSegments.Length - 1; i++)
			{
				segments.Add(documentSegments[i]);
			}
		}

		foreach (string segment in targetPath.Split('/'))
		{
			if (segment is "" or ".")
			{
				continue;
			}

			if (string.Equals(segment, "..", StringComparison.Ordinal))
			{
				if (segments.Count == 0)
				{
					problem = "climbs above the repository root";
					return false;
				}

				segments.RemoveAt(segments.Count - 1);
				continue;
			}

			segments.Add(segment);
		}

		foreach (string segment in segments)
		{
			foreach (string uncommitted in DocumentationConventions.UncommittedSegments)
			{
				if (string.Equals(segment, uncommitted, StringComparison.OrdinalIgnoreCase))
				{
					problem = $"points into '{segment}/', which only holds build output or tool state and is never committed";
					return false;
				}
			}
		}

		resolved = string.Join('/', segments);
		problem = null;
		return true;
	}

	/// <summary>
	///     The README of every packable project: a <c>*.csproj</c> that sets <c>IsPackable</c> to <c>true</c> packs the
	///     sibling <c>README.md</c> (<c>Directory.Build.targets</c> convention) unless it names another
	///     <c>PackageReadmeFile</c>. Repository-relative paths, sorted.
	/// </summary>
	public static IReadOnlyList<string> PackedReadmes()
	{
		List<string> readmes = [];
		foreach (string project in RepositoryRoot.EnumerateSourceFiles("*.csproj"))
		{
			XDocument document = XDocument.Load(System.IO.Path.Combine(RepositoryRoot.Path, project));
			bool packable = false;
			string readme = "README.md";
			foreach (XElement element in document.Descendants())
			{
				string value = element.Value.Trim();
				string name = element.Name.LocalName;
				if (string.Equals(name, "IsPackable", StringComparison.Ordinal)
					&& string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
				{
					packable = true;
				}
				else if (string.Equals(name, "PackageReadmeFile", StringComparison.Ordinal) && value.Length > 0)
				{
					readme = value.Replace('\\', '/');
				}
			}

			if (packable)
			{
				int slash = project.LastIndexOf('/');
				readmes.Add(slash < 0 ? readme : project[..(slash + 1)] + readme);
			}
		}

		readmes.Sort(StringComparer.Ordinal);
		return readmes;
	}

	private static string[] Entries(string directory)
	{
		return DirectoryEntries.GetOrAdd(directory, static path =>
		{
			List<string> names = [];
			foreach (string entry in Directory.EnumerateFileSystemEntries(path))
			{
				names.Add(System.IO.Path.GetFileName(entry));
			}

			return [.. names];
		});
	}
}
