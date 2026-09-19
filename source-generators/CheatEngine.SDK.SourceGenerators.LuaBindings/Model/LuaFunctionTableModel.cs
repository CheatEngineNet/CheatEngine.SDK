using CheatEngine.SDK.SourceGenerators.Shared;
using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Model;

/// <summary>
///     The unit of output for <c>[LuaFunction]</c>: one containing type and its exportable thunks, in emission order
///     (by Lua name, ordinal). The source output re-runs for a type exactly when this record changes.
/// </summary>
/// <param name="ContainingType">The type the file re-opens.</param>
/// <param name="Thunks">The thunks, at least one, sorted by Lua name.</param>
/// <param name="HintName">
///     The file's hint name, resolved by <see cref="LuaFunctionTables.Group" /> across every table of the pass so that
///     two types whose names differ only in ASCII case (which <see cref="HintNames.ForType" />
///     alone cannot tell apart) still get distinct names.
/// </param>
internal sealed record LuaFunctionTableModel(
    ContainingTypeModel ContainingType,
    EquatableArray<LuaThunkModel> Thunks,
    string HintName)
{
    /// <summary>
    ///     Suffix of the hint name: <c>Demo.Math.LuaFunctions.g.cs</c>. Owned here because
    ///     <see cref="LuaFunctionTables.Group" /> needs it to resolve <see cref="HintName" />;
    ///     <c>Emit/LuaFunctionFileEmitter.cs</c> reuses this constant.
    /// </summary>
    public const string HintSuffix = ".LuaFunctions.g.cs";
}
