using CheatEngine.SDK.SourceGenerators.Shared;

namespace CheatEngine.SDK.SourceGenerators.EngineApi.Model;

/// <summary>
///     The unit of output for one <c>*.cheatengine-sdk-api.txt</c> spec file: the type it declares, the distinct globals it binds
///     (one cache field each), the wrappers to emit (in emission order), and every localized issue. The source output
///     re-runs for a file exactly when this record changes.
/// </summary>
/// <param name="SourcePath">
///     The additional file's path, as <c>AdditionalText.Path</c> reports it; the basis of
///     <see cref="HintName" /> and never emitted into generated text.
/// </param>
/// <param name="Namespace">
///     The declared namespace, empty for the global namespace. Empty (with <see cref="Issues" />
///     non-empty) when the header could not be parsed.
/// </param>
/// <param name="TypeName">The declared type name. Empty when the header could not be parsed.</param>
/// <param name="TypeLine">1-based line of the <c>type</c> declaration, or 1 for an invalid header.</param>
/// <param name="TypeColumn">1-based column of the type value, or 1 for an invalid header.</param>
/// <param name="Contract">
///     Validated CE 7.7 evidence shared by the file's entries, or <see langword="null" /> for a legacy fixture while
///     the repository migrates it to <c>contract: ce77</c>.
/// </param>
/// <param name="HintName">
///     Resolved by <c>Model/SpecFiles.AssignHintNames</c> across the whole pass, collision-safe the
///     same way <c>CheatEngine.SDK.SourceGenerators.LuaBindings</c> resolves its per-type hint names.
/// </param>
/// <param name="CachedGlobals">
///     The distinct Lua global names used by <see cref="Calls" />, sorted (ordinal): one
///     <c>LuaRef</c> field each.
/// </param>
/// <param name="Calls">
///     The wrappers to emit, sorted by C# method name (ordinal). Empty when the file is a shell, is
///     entirely invalid, every entry was dropped, or <see cref="IsSuppressed" />: the generator then emits nothing for it.
/// </param>
/// <param name="Issues">
///     Every invalid entry, header, or cross-spec conflict. The source output reports each one as a diagnostic.
/// </param>
/// <param name="IsSuppressed">Whether a cross-file identity conflict makes the complete file ineligible for emission.</param>
internal sealed record SpecFileModel(
    string SourcePath,
    string Namespace,
    string TypeName,
    int TypeLine,
    int TypeColumn,
    SpecFileContract? Contract,
    string HintName,
    EquatableArray<string> CachedGlobals,
    EquatableArray<SpecCallModel> Calls,
    EquatableArray<SpecIssue> Issues,
    bool IsSuppressed)
{
    /// <summary>Suffix of the hint name: <c>memory-scalars.cheatengine-sdk-api.txt.EngineApi.g.cs</c>.</summary>
    public const string HintSuffix = ".EngineApi.g.cs";
}
