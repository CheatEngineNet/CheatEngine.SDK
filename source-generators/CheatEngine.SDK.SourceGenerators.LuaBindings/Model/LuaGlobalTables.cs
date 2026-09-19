using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using CheatEngine.SDK.SourceGenerators.Shared;
using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Model;

/// <summary>
///     Turns the collected <c>[LuaGlobal]</c> models into one table per containing type: the grouping step of the
///     pipeline. Several methods may bind the same global (a Try form and a throwing form, overloads with different
///     result shapes): they share one cache field, which is why the table lists the distinct names separately.
/// </summary>
internal static class LuaGlobalTables
{
    /// <summary>
    ///     Groups the valid models by containing type and sorts tables by type name, bodies by their sort key and cached
    ///     globals by name (all ordinal), so that the output is deterministic and a table compares equal to its previous
    ///     value when nothing in that type changed.
    /// </summary>
    public static EquatableArray<LuaGlobalTableModel> Group(ImmutableArray<LuaGlobalModel> models)
    {
        if (models.IsDefaultOrEmpty) return EquatableArray<LuaGlobalTableModel>.Empty;

        Dictionary<string, List<LuaGlobalModel>> groups = new(StringComparer.Ordinal);
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

        List<LuaGlobalTableModel> tables = [];
        foreach (var group in groups) tables.Add(CreateTable(group.Value));

        tables.Sort(static (left, right) =>
            string.CompareOrdinal(left.ContainingType.FullyQualifiedName, right.ContainingType.FullyQualifiedName));
        return new EquatableArray<LuaGlobalTableModel>([.. AssignHintNames(tables)]);
    }

    private static LuaGlobalTableModel CreateTable(List<LuaGlobalModel> members)
    {
        members.Sort(static (left, right) => string.CompareOrdinal(left.SortKey, right.SortKey));

        // The sorted set is the list of distinct names in ordinal order, so nothing is sorted afterwards.
        SortedSet<string> globals = new(StringComparer.Ordinal);
        List<LuaGlobalCallModel> calls = new(members.Count);
        foreach (var member in members)
        {
            var call = member.Call!;
            calls.Add(call);
            globals.Add(call.GlobalName);
        }

        return new LuaGlobalTableModel(
            members[0].ContainingType,
            new EquatableArray<string>([.. globals]),
            new EquatableArray<LuaGlobalCallModel>([.. calls]),
            string.Empty);
    }

    // Hint names are resolved here, over every table of the pass at once, because Roslyn compares them
    // case-insensitively (AdditionalSourcesCollection.Add throws ArgumentException otherwise): two containing types
    // whose dotted names differ only in ASCII case both produce the plain HintNames.ForType result (neither has a
    // replaced character), so the second one in sort order falls back to a hash-suffixed name.
    private static List<LuaGlobalTableModel> AssignHintNames(List<LuaGlobalTableModel> tables)
    {
        HashSet<string> used = new(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < tables.Count; i++)
        {
            var baseName = tables[i].ContainingType.HintBaseName;
            var hintName = HintNames.ForType(baseName, LuaGlobalTableModel.HintSuffix);
            if (!used.Add(hintName))
            {
                hintName = HintNames.Disambiguated(baseName, LuaGlobalTableModel.HintSuffix);
                used.Add(hintName);
            }

            tables[i] = tables[i] with { HintName = hintName };
        }

        return tables;
    }
}
