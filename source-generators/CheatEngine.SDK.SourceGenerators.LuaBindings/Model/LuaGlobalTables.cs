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
		if (models.IsDefaultOrEmpty)
		{
			return EquatableArray<LuaGlobalTableModel>.Empty;
		}

		Dictionary<string, List<LuaGlobalModel>> groups = new(StringComparer.Ordinal);
		foreach (LuaGlobalModel model in models)
		{
			if (!model.IsValid)
			{
				continue;
			}

			string key = model.ContainingType.FullyQualifiedName;
			if (!groups.TryGetValue(key, out List<LuaGlobalModel>? members))
			{
				members = [];
				groups.Add(key, members);
			}

			members.Add(model);
		}

		List<LuaGlobalTableModel> tables = [];
		foreach (KeyValuePair<string, List<LuaGlobalModel>> group in groups)
		{
			tables.Add(CreateTable(group.Value));
		}

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
		foreach (LuaGlobalModel member in members)
		{
			LuaGlobalCallModel call = member.Call!;
			calls.Add(call);
			globals.Add(call.GlobalName);
		}

		return new LuaGlobalTableModel(
			members[0].ContainingType,
			new EquatableArray<string>([.. globals]),
			new EquatableArray<LuaGlobalCallModel>([.. calls]),
			string.Empty);
	}

	// Hint names are resolved across every table of the pass because Roslyn compares them case-insensitively. The
	// shared allocator reserves the readable candidate, then a deterministic hash candidate, then ordinal suffixes.
	private static List<LuaGlobalTableModel> AssignHintNames(List<LuaGlobalTableModel> tables)
	{
		HashSet<string> used = HintNames.CreateUsedNames();
		for (int i = 0; i < tables.Count; i++)
		{
			string baseName = tables[i].ContainingType.HintBaseName;
			string hintName = HintNames.AllocateUnique(baseName, LuaGlobalTableModel.HintSuffix, used);
			tables[i] = tables[i] with
			{
				HintName = hintName
			};
		}

		return tables;
	}
}
