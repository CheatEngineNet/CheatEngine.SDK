using System.IO.Compression;

namespace CheatEngine.SDK.Tests.Infrastructure;

/// <summary>Reads a packed <c>.nupkg</c> (a zip archive) without extracting it: entry names and the nuspec.</summary>
internal static class NupkgInspector
{
	/// <summary>Every entry path inside the package, and its parsed <c>.nuspec</c>.</summary>
	public static (IReadOnlyList<string> Entries, XDocument Nuspec) Read(string nupkgPath)
	{
		using ZipArchive archive = ZipFile.OpenRead(nupkgPath);
		List<string> entries = [.. archive.Entries.Select(static e => e.FullName)];

		ZipArchiveEntry nuspecEntry =
			archive.Entries.Single(static e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
		using Stream nuspecStream = nuspecEntry.Open();
		XDocument nuspec = XDocument.Load(nuspecStream);

		return (entries, nuspec);
	}

	/// <summary>The exact bytes of one entry, matched by its full name (ordinal, forward slashes as the zip stores them).</summary>
	/// <exception cref="InvalidOperationException">The package has no such entry.</exception>
	public static byte[] ReadEntryBytes(string nupkgPath, string entryPath)
	{
		using ZipArchive archive = ZipFile.OpenRead(nupkgPath);
		foreach (ZipArchiveEntry entry in archive.Entries)
		{
			if (string.Equals(entry.FullName, entryPath, StringComparison.Ordinal))
			{
				using Stream stream = entry.Open();
				using MemoryStream copy = new();
				stream.CopyTo(copy);
				return copy.ToArray();
			}
		}

		throw new InvalidOperationException($"'{Path.GetFileName(nupkgPath)}' has no '{entryPath}' entry.");
	}

	/// <summary>Every <c>id</c> attribute of every <c>&lt;dependency&gt;</c> element, across every target-framework group.</summary>
	public static IReadOnlyList<string> GetDependencyIds(XDocument nuspec)
	{
		XNamespace ns = nuspec.Root!.GetDefaultNamespace();
		return
			[.. nuspec.Descendants(ns + "dependency").Select(static e => (string?) e.Attribute("id") ?? string.Empty)];
	}
}
