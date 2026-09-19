using System.IO.Compression;

namespace CESDK.Tests.Infrastructure;

/// <summary>Reads a packed <c>.nupkg</c> (a zip archive) without extracting it: entry names and the nuspec.</summary>
internal static class NupkgInspector
{
    /// <summary>Every entry path inside the package, and its parsed <c>.nuspec</c>.</summary>
    public static (IReadOnlyList<string> Entries, XDocument Nuspec) Read(string nupkgPath)
    {
        using var archive = ZipFile.OpenRead(nupkgPath);
        List<string> entries = [.. archive.Entries.Select(static e => e.FullName)];

        var nuspecEntry =
            archive.Entries.Single(static e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        using var nuspecStream = nuspecEntry.Open();
        var nuspec = XDocument.Load(nuspecStream);

        return (entries, nuspec);
    }

    /// <summary>Every <c>id</c> attribute of every <c>&lt;dependency&gt;</c> element, across every target-framework group.</summary>
    public static IReadOnlyList<string> GetDependencyIds(XDocument nuspec)
    {
        var ns = nuspec.Root!.GetDefaultNamespace();
        return
            [.. nuspec.Descendants(ns + "dependency").Select(static e => (string?)e.Attribute("id") ?? string.Empty)];
    }
}
