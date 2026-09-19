using CESDK.SourceGenerators.Shared;
using CESDK.SourceGenerators.Shared.LuaEmit;

namespace CESDK.SourceGenerators.LuaBindings.Model;

/// <summary>
///     The unit of output for <c>[LuaGlobal]</c>: one containing type, the distinct globals it binds (one cache field
///     each) and the bodies to emit, in emission order. The source output re-runs for a type exactly when this record
///     changes.
/// </summary>
/// <param name="ContainingType">The type the file re-opens.</param>
/// <param name="CachedGlobals">The distinct global names, sorted (ordinal): one <c>LuaRef</c> field per entry.</param>
/// <param name="Calls">The bodies, at least one, sorted by <see cref="LuaGlobalModel.SortKey" />.</param>
/// <param name="HintName">
///     The file's hint name, resolved by <see cref="LuaGlobalTables.Group" /> across every table of the pass so that
///     two types whose names differ only in ASCII case (which <see cref="HintNames.ForType" />
///     alone cannot tell apart) still get distinct names.
/// </param>
internal sealed record LuaGlobalTableModel(
    ContainingTypeModel ContainingType,
    EquatableArray<string> CachedGlobals,
    EquatableArray<LuaGlobalCallModel> Calls,
    string HintName)
{
    /// <summary>
    ///     Suffix of the hint name: <c>Demo.Memory.LuaGlobals.g.cs</c>. Owned here because
    ///     <see cref="LuaGlobalTables.Group" /> needs it to resolve <see cref="HintName" />;
    ///     <c>Emit/LuaGlobalFileEmitter.cs</c> reuses this constant.
    /// </summary>
    public const string HintSuffix = ".LuaGlobals.g.cs";
}
