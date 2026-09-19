using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using CheatEngine.SDK.SourceGenerators.Shared;
using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Model;

/// <summary>
///     Turns the collected <c>[LuaFunction]</c> models into one table per containing type: the grouping step of the
///     pipeline, and the only place where a rule spans several members (a duplicated name).
/// </summary>
internal static class LuaFunctionTables
{
    /// <summary>
    ///     Groups the valid models by containing type, drops every member of a name that appears twice in a type, and
    ///     sorts tables by type name and thunks by Lua name (ordinal) so that the output is deterministic and a table
    ///     compares equal to its previous value when nothing in that type changed.
    /// </summary>
    public static EquatableArray<LuaFunctionTableModel> Group(ImmutableArray<LuaFunctionModel> models)
    {
        if (models.IsDefaultOrEmpty) return EquatableArray<LuaFunctionTableModel>.Empty;

        Dictionary<string, List<LuaFunctionModel>> groups = new(StringComparer.Ordinal);
        foreach (var model in models)
        {
            if (!model.IsValid) continue;

            var key = model.ContainingType.FullyQualifiedName;
            if (!groups.TryGetValue(key, out var members))
            {
                members = [];
                groups.Add(key, members);
            }

            members.Add(model);
        }

        List<LuaFunctionTableModel> tables = [];
        foreach (var group in groups)
        {
            var thunks = SelectThunks(group.Value);
            if (!thunks.IsEmpty)
                tables.Add(new LuaFunctionTableModel(group.Value[0].ContainingType, thunks, string.Empty));
        }

        tables.Sort(static (left, right) =>
            string.CompareOrdinal(left.ContainingType.FullyQualifiedName, right.ContainingType.FullyQualifiedName));
        return new EquatableArray<LuaFunctionTableModel>([.. AssignHintNames(tables)]);
    }

    // Hint names are resolved here, over every table of the pass at once, because Roslyn compares them
    // case-insensitively (AdditionalSourcesCollection.Add throws ArgumentException otherwise): two containing types
    // whose dotted names differ only in ASCII case both produce the plain HintNames.ForType result (neither has a
    // replaced character), so the second one in sort order falls back to a hash-suffixed name.
    private static List<LuaFunctionTableModel> AssignHintNames(List<LuaFunctionTableModel> tables)
    {
        HashSet<string> used = new(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < tables.Count; i++)
        {
            var baseName = tables[i].ContainingType.HintBaseName;
            var hintName = HintNames.ForType(baseName, LuaFunctionTableModel.HintSuffix);
            if (!used.Add(hintName))
            {
                hintName = HintNames.Disambiguated(baseName, LuaFunctionTableModel.HintSuffix);
                used.Add(hintName);
            }

            tables[i] = tables[i] with { HintName = hintName };
        }

        return tables;
    }

    // The thunks of one type without the duplicated names, sorted by Lua name.
    private static EquatableArray<LuaThunkModel> SelectThunks(List<LuaFunctionModel> members)
    {
        Dictionary<string, int> occurrences = new(StringComparer.Ordinal);
        foreach (var member in members)
        {
            occurrences.TryGetValue(member.LuaName, out var count);
            occurrences[member.LuaName] = count + 1;
        }

        List<LuaThunkModel> thunks = [];
        foreach (var member in members)
            if (occurrences[member.LuaName] == 1)
                thunks.Add(member.Thunk!);

        thunks.Sort(static (left, right) => string.CompareOrdinal(left.LuaName, right.LuaName));
        return new EquatableArray<LuaThunkModel>([.. thunks]);
    }
}
