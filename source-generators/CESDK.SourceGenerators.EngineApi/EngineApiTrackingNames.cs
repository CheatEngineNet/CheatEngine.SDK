using CESDK.SourceGenerators.Shared;

namespace CESDK.SourceGenerators.EngineApi;

/// <summary>
///     <c>WithTrackingName</c> constants for every step of <see cref="EngineApiGenerator" />, and the list the
///     cacheability tests walk (<see cref="TrackingNames.IsCesdkStep" />).
/// </summary>
/// <remarks>
///     The pipeline is shorter than <c>CESDK.SourceGenerators.LuaBindings</c>'s: there is no <c>CompilationProvider</c>
///     fact and no <c>AllowUnsafeBlocks</c> gate, because an emitted wrapper body contains no unsafe code (it calls
///     managed members only; unlike a <c>[LuaFunction]</c> registration table it never takes the address of a thunk).
/// </remarks>
internal static class EngineApiTrackingNames
{
    /// <summary>The additional text, after the <c>*.cesdk-api.txt</c> filter.</summary>
    public const string SpecTextFile = TrackingNames.Prefix + "EngineApi.SpecTextFile";

    /// <summary>One file, parsed to a value-equatable model (hint name not yet assigned).</summary>
    public const string ParsedSpec = TrackingNames.Prefix + "EngineApi.ParsedSpec";

    /// <summary>Every parsed file of the pass, collected.</summary>
    public const string CollectedSpecs = TrackingNames.Prefix + "EngineApi.CollectedSpecs";

    /// <summary>Every file with its hint name resolved (collision-safe across the whole pass).</summary>
    public const string SpecFiles = TrackingNames.Prefix + "EngineApi.SpecFiles";

    /// <summary>One file, hint name included: the per-file output unit.</summary>
    public const string SpecFile = TrackingNames.Prefix + "EngineApi.SpecFile";

    /// <summary>Files with at least one valid entry: what actually reaches <c>RegisterSourceOutput</c>.</summary>
    public const string SpecFileOutput = TrackingNames.Prefix + "EngineApi.SpecFileOutput";

    /// <summary>Every step name above, for the cacheability test.</summary>
    public static readonly string[] All =
    [
        SpecTextFile, ParsedSpec, CollectedSpecs, SpecFiles, SpecFile, SpecFileOutput
    ];
}
