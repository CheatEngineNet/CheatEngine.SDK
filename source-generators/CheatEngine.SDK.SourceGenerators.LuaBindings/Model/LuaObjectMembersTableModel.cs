using CheatEngine.SDK.SourceGenerators.Shared;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Model;

/// <summary>All valid object methods and properties generated into one partial type file.</summary>
internal sealed record LuaObjectMembersTableModel(
    ContainingTypeModel ContainingType,
    EquatableArray<LuaObjectMethodModel> Methods,
    EquatableArray<LuaObjectPropertyModel> Properties,
    string HintName);
