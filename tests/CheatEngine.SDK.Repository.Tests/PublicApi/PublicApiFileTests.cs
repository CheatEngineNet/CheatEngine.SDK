using CheatEngine.SDK.Repository.Tests.Infrastructure;

namespace CheatEngine.SDK.Repository.Tests.PublicApi;

/// <summary>
///     Shape of the PublicAPI files of the six shipping libraries. The build enforces their content (RS0016/RS0017, and
///     CESDK9003 when a file is missing); these tests keep the files mergeable by ordinal sort and union and keep
///     <c>PublicAPI.Shipped.txt</c> equal to the published 1.0.0 surface plus explicit <c>*REMOVED*</c> declarations.
/// </summary>
public sealed class PublicApiFileTests
{
	private static readonly string[] s_publicApiFileNames =
		[PublicApiLibrary.ShippedFileName, PublicApiLibrary.UnshippedFileName];

	[Fact]
	public void Every_shipping_library_has_both_public_api_files()
	{
		SortedSet<string> libraries = new(PublicApiLibrary.EnumerateLibraryDirectories(), StringComparer.Ordinal);
		SortedSet<string> tracked = new(StringComparer.Ordinal);
		foreach (PublicApiLibrary library in PublicApiLibrary.LoadAll())
		{
			tracked.Add(library.LibraryDirectory);
		}

		Assert.Equal(libraries, tracked);
		Assert.Equal(6, tracked.Count);
	}

	[Fact]
	public void Public_api_files_exist_only_next_to_shipping_libraries()
	{
		SortedSet<string> libraries = new(PublicApiLibrary.EnumerateLibraryDirectories(), StringComparer.Ordinal);
		List<string> strays = [];
		foreach (string pattern in s_publicApiFileNames)
		{
			foreach (string file in RepositoryRoot.EnumerateSourceFiles(pattern))
			{
				if (!libraries.Contains(file[..file.LastIndexOf('/')]))
				{
					strays.Add(file);
				}
			}
		}

		Assert.True(strays.Count == 0, $"PublicAPI files outside libs/<library>/: {string.Join(", ", strays)}");
	}

	[Fact]
	public void Every_public_api_file_starts_with_nullable_enable()
	{
		foreach (PublicApiLibrary library in PublicApiLibrary.LoadAll())
		{
			Assert.True(StartsWithHeader(library.ShippedFile),
				$"{library.ShippedPath} must start with '{PublicApiLibrary.Header}'.");
			Assert.True(StartsWithHeader(library.UnshippedFile),
				$"{library.UnshippedPath} must start with '{PublicApiLibrary.Header}'.");
		}
	}

	[Fact]
	public void Every_public_api_file_is_ordinally_sorted_after_its_header()
	{
		foreach (PublicApiLibrary library in PublicApiLibrary.LoadAll())
		{
			AssertSortedAndDistinct(library.ShippedPath, library.ShippedFile);
			AssertSortedAndDistinct(library.UnshippedPath, library.UnshippedFile);
		}
	}

	[Fact]
	public void Shipped_files_never_contain_removed_markers()
	{
		foreach (PublicApiLibrary library in PublicApiLibrary.LoadAll())
		{
			Assert.DoesNotContain(library.Shipped,
				static line => line.StartsWith(PublicApiLibrary.RemovedPrefix, StringComparison.Ordinal));
		}
	}

	[Fact]
	public void Every_removed_line_names_a_line_of_the_shipped_file()
	{
		foreach (PublicApiLibrary library in PublicApiLibrary.LoadAll())
		{
			HashSet<string> shipped = new(library.Shipped, StringComparer.Ordinal);
			foreach (string removed in library.Removed)
			{
				Assert.True(shipped.Contains(removed),
					$"{library.UnshippedPath}: '*REMOVED*{removed}' does not repeat a line of {PublicApiLibrary.ShippedFileName} exactly.");
			}
		}
	}

	[Fact]
	public void Unshipped_never_redeclares_a_live_shipped_line()
	{
		foreach (PublicApiLibrary library in PublicApiLibrary.LoadAll())
		{
			HashSet<string> shipped = new(library.Shipped, StringComparer.Ordinal);
			HashSet<string> removed = new(library.Removed, StringComparer.Ordinal);
			foreach (string added in library.Added)
			{
				Assert.False(shipped.Contains(added) && !removed.Contains(added),
					$"{library.UnshippedPath}: '{added}' is already shipped.");
			}
		}
	}

	private static bool StartsWithHeader(IReadOnlyList<string> lines)
	{
		return lines.Count > 0 && string.Equals(lines[0], PublicApiLibrary.Header, StringComparison.Ordinal);
	}

	private static void AssertSortedAndDistinct(string path, IReadOnlyList<string> lines)
	{
		for (int index = 1; index < lines.Count; index++)
		{
			Assert.False(string.IsNullOrWhiteSpace(lines[index]), $"{path}:{index + 1} is blank.");
			if (index > 1)
			{
				int order = string.CompareOrdinal(lines[index - 1], lines[index]);
				Assert.True(order < 0,
					$"{path}:{index + 1} is {(order == 0 ? "a duplicate" : "out of ordinal order")}: '{lines[index]}'.");
			}
		}
	}
}
