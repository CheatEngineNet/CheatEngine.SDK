namespace CheatEngine.SDK.Tests.Infrastructure;

/// <summary>
///     Identity of the package these tests pack and restore, kept in one place. The id is the prefix of the packed
///     <c>.nupkg</c> file name, the <c>PackageReference</c> of every throwaway consumer and, lower-cased, the folder
///     NuGet extracts the package into, so the tests follow a change of <c>PackageId</c> by editing one line.
/// </summary>
internal static class UmbrellaPackage
{
    /// <summary>The <c>PackageId</c> of <c>src/CheatEngine.SDK/CheatEngine.SDK.csproj</c>.</summary>
    public const string Id = "CheatEngine.SDK";

    /// <summary>Repository-relative path, with forward slashes, of the project that packs <see cref="Id" />.</summary>
    public const string ProjectPath = "src/CheatEngine.SDK/CheatEngine.SDK.csproj";

    /// <summary>
    ///     The folder name NuGet extracts <see cref="Id" /> into inside a global-packages folder
    ///     (<c>&lt;packages&gt;/cheatengine.sdk/&lt;version&gt;</c>): NuGet lower-cases the id there.
    /// </summary>
    public static string ExtractionFolderName { get; } = Id.ToLowerInvariant();
}
