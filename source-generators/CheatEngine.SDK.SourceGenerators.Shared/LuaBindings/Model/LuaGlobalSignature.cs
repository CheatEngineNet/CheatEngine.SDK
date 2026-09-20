using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

namespace CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Model;

/// <summary>The classified signature of a <c>[LuaGlobal]</c> method, as far as the shape inspection got.</summary>
/// <param name="StateParameterName">The name of a leading <c>LuaState</c> parameter, or empty.</param>
/// <param name="Arguments">The values pushed after the function, in order.</param>
/// <param name="Form">Try (<see langword="bool" /> + <see langword="out" /> results) or throwing.</param>
/// <param name="Results">The Try form's results; empty for the throwing form.</param>
/// <param name="ReturnKind">The throwing form's result kind, or <see langword="null" /> for <see langword="void" />.</param>
/// <param name="ReturnIsNullable">The throwing form returns <c>string?</c>.</param>
internal readonly record struct LuaGlobalSignature(
    string StateParameterName,
    EquatableArray<LuaArgumentModel> Arguments,
    LuaCallForm Form,
    EquatableArray<LuaResultModel> Results,
    LuaValueKind? ReturnKind,
    bool ReturnIsNullable);
