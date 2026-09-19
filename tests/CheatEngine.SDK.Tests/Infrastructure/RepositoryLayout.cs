namespace CheatEngine.SDK.Tests.Infrastructure;

/// <summary>Locates files of the repository from the test output directory, for packing <c>src/CheatEngine.SDK</c>.</summary>
internal static class RepositoryLayout
{
    private const string SolutionFileName = "CheatEngine.SDK.slnx";

    /// <summary>The directory that contains <c>CheatEngine.SDK.slnx</c>, found by walking up from the test binaries.</summary>
    public static string Root { get; } = FindRoot();

    /// <summary>Absolute path of a repository-relative path written with forward slashes.</summary>
    public static string PathOf(string relativePath)
    {
        return Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string FindRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
                return directory.FullName;

        throw new InvalidOperationException(
            $"'{SolutionFileName}' was not found above '{AppContext.BaseDirectory}': the tests expect to run from the repository's artifacts directory.");
    }
}
