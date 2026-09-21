namespace CheatEngine.SDK.NativeAotLibraryProbe;

/// <summary>Names exported only by the inert NativeAOT library-analysis fixture.</summary>
public static class NativeAotLibraryProbeExportNames
{
    /// <summary>A name that the loader harness may query after mapping the fixture.</summary>
    public const string NameQuery = "CheatEngineSdkNativeAotProbe_NameQuery";

    /// <summary>A name that records that the fixture has no plugin activation entry point.</summary>
    public const string LoadOnly = "CheatEngineSdkNativeAotProbe_LoadOnly";

    /// <summary>The complete set of names required by the loader harness.</summary>
    public static IReadOnlyList<string> Required { get; } = [NameQuery, LoadOnly];
}
