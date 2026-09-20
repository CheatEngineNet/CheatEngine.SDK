namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Model;

/// <summary>
///     Value-only description of one <c>[LuaClass]</c> declaration. The parser deliberately retains no Roslyn symbols so
///     an unchanged type remains unchanged to the incremental pipeline.
/// </summary>
internal sealed record LuaClassModel(
    ContainingTypeModel ContainingType,
    string LuaName,
    bool IsValid,
    string HintName = "");
