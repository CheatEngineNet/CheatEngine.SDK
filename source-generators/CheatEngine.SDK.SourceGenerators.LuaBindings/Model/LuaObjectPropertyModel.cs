using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Model;

/// <summary>One generated partial property on a <c>[LuaClass]</c> borrowed handle.</summary>
internal sealed record LuaObjectPropertyModel(
    ContainingTypeModel ContainingType,
    string LuaName,
    string Modifiers,
    string PropertyName,
    LuaValueKind Kind,
    bool IsNullable,
    bool HasGetter,
    string GetterModifiers,
    bool HasSetter,
    string SetterModifiers,
    string SortKey,
    bool IsValid);
