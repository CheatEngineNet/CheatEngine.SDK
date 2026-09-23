using CheatEngine.SDK.Repository.Tests.Infrastructure;

namespace CheatEngine.SDK.Repository.Tests.PublicApi;

/// <summary>
///     The committed <c>PublicAPI.Shipped.txt</c> / <c>PublicAPI.Unshipped.txt</c> pair of one shipping library, in the
///     Microsoft.CodeAnalysis.PublicApiAnalyzers format: a <c>#nullable enable</c> header, then one declaration per line;
///     <c>Unshipped</c> may prefix a line of <c>Shipped</c> with <c>*REMOVED*</c>.
/// </summary>
internal sealed class PublicApiLibrary
{
	public const string Header = "#nullable enable";
	public const string RemovedPrefix = "*REMOVED*";
	public const string ShippedFileName = "PublicAPI.Shipped.txt";
	public const string UnshippedFileName = "PublicAPI.Unshipped.txt";

	private PublicApiLibrary(string libraryDirectory)
	{
		LibraryDirectory = libraryDirectory;
		ShippedFile = ReadLines(ShippedPath);
		UnshippedFile = ReadLines(UnshippedPath);
	}

	/// <summary>Repository-relative directory of the library, for example <c>libs/CheatEngine.SDK.Engine</c>.</summary>
	public string LibraryDirectory
	{
		get;
	}

	/// <summary>The assembly name, which is also the directory name.</summary>
	public string Name => LibraryDirectory[(LibraryDirectory.LastIndexOf('/') + 1)..];

	public string ShippedPath => $"{LibraryDirectory}/{ShippedFileName}";

	public string UnshippedPath => $"{LibraryDirectory}/{UnshippedFileName}";

	/// <summary>Every line of <c>PublicAPI.Shipped.txt</c>, header included.</summary>
	public IReadOnlyList<string> ShippedFile
	{
		get;
	}

	/// <summary>Every line of <c>PublicAPI.Unshipped.txt</c>, header included.</summary>
	public IReadOnlyList<string> UnshippedFile
	{
		get;
	}

	/// <summary>The declarations of the published CheatEngine.SDK 1.0.0 surface (Shipped without its header).</summary>
	public IEnumerable<string> Shipped => WithoutHeader(ShippedFile);

	/// <summary>Declarations added since 1.0.0 (Unshipped lines without the removal marker).</summary>
	public IEnumerable<string> Added
	{
		get
		{
			foreach (string line in WithoutHeader(UnshippedFile))
			{
				if (!line.StartsWith(RemovedPrefix, StringComparison.Ordinal))
				{
					yield return line;
				}
			}
		}
	}

	/// <summary>Shipped declarations that no longer exist, with the <c>*REMOVED*</c> marker stripped.</summary>
	public IEnumerable<string> Removed
	{
		get
		{
			foreach (string line in WithoutHeader(UnshippedFile))
			{
				if (line.StartsWith(RemovedPrefix, StringComparison.Ordinal))
				{
					yield return line[RemovedPrefix.Length..];
				}
			}
		}
	}

	/// <summary>The current declared surface: Shipped without the removed declarations, plus the added ones.</summary>
	public IReadOnlySet<string> Declared
	{
		get
		{
			HashSet<string> declared = new(Shipped, StringComparer.Ordinal);
			declared.ExceptWith(Removed);
			declared.UnionWith(Added);
			return declared;
		}
	}

	/// <summary>Every repository-relative <c>libs/&lt;name&gt;</c> directory that holds a project file.</summary>
	public static IReadOnlyList<string> EnumerateLibraryDirectories()
	{
		SortedSet<string> directories = new(StringComparer.Ordinal);
		foreach (string project in RepositoryRoot.EnumerateSourceFiles("*.csproj"))
		{
			if (project.StartsWith("libs/", StringComparison.Ordinal))
			{
				directories.Add(project[..project.LastIndexOf('/')]);
			}
		}

		return [.. directories];
	}

	/// <summary>Every shipping library that has both files, in ordinal order of directory.</summary>
	public static IReadOnlyList<PublicApiLibrary> LoadAll()
	{
		List<PublicApiLibrary> libraries = [];
		foreach (string directory in EnumerateLibraryDirectories())
		{
			if (File.Exists(FullPath($"{directory}/{ShippedFileName}"))
			    && File.Exists(FullPath($"{directory}/{UnshippedFileName}")))
			{
				libraries.Add(new PublicApiLibrary(directory));
			}
		}

		return libraries;
	}

	private static IEnumerable<string> WithoutHeader(IReadOnlyList<string> lines)
	{
		for (int index = 0; index < lines.Count; index++)
		{
			if (index == 0 && string.Equals(lines[index], Header, StringComparison.Ordinal))
			{
				continue;
			}

			yield return lines[index];
		}
	}

	private static string[] ReadLines(string relativePath)
	{
		return File.ReadAllLines(FullPath(relativePath));
	}

	private static string FullPath(string relativePath)
	{
		return Path.Combine(RepositoryRoot.Path, relativePath.Replace('/', Path.DirectorySeparatorChar));
	}
}
