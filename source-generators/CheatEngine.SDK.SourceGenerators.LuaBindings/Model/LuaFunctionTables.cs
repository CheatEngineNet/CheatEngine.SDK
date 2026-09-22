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
		if (models.IsDefaultOrEmpty)
		{
			return EquatableArray<LuaFunctionTableModel>.Empty;
		}

		Dictionary<string, List<LuaFunctionModel>> groups = new(StringComparer.Ordinal);
		foreach (LuaFunctionModel model in models)
		{
			if (!model.IsValid)
			{
				continue;
			}

			string key = model.ContainingType.FullyQualifiedName;
			if (!groups.TryGetValue(key, out List<LuaFunctionModel>? members))
			{
				members = [];
				groups.Add(key, members);
			}

			members.Add(model);
		}

		List<LuaFunctionTableModel> tables = [];
		foreach (KeyValuePair<string, List<LuaFunctionModel>> group in groups)
		{
			EquatableArray<LuaThunkModel> thunks = SelectThunks(group.Value);
			if (!thunks.IsEmpty)
			{
				tables.Add(new LuaFunctionTableModel(group.Value[0].ContainingType, thunks, string.Empty));
			}
		}

		tables.Sort(static (left, right) =>
			string.CompareOrdinal(left.ContainingType.FullyQualifiedName, right.ContainingType.FullyQualifiedName));
		return new EquatableArray<LuaFunctionTableModel>([.. AssignHintNames(tables)]);
	}

	// Hint names are resolved across every table of the pass because Roslyn compares them case-insensitively. The
	// shared allocator reserves the readable candidate, then a deterministic hash candidate, then ordinal suffixes.
	private static List<LuaFunctionTableModel> AssignHintNames(List<LuaFunctionTableModel> tables)
	{
		HashSet<string> used = HintNames.CreateUsedNames();
		for (int i = 0; i < tables.Count; i++)
		{
			string baseName = tables[i].ContainingType.HintBaseName;
			string hintName = HintNames.AllocateUnique(baseName, LuaFunctionTableModel.HintSuffix, used);
			tables[i] = tables[i] with { HintName = hintName };
		}

		return tables;
	}

	// The thunks of one type without the duplicated names, sorted by Lua name.
	private static EquatableArray<LuaThunkModel> SelectThunks(List<LuaFunctionModel> members)
	{
		Dictionary<string, int> occurrences = new(StringComparer.Ordinal);
		foreach (LuaFunctionModel member in members)
		{
			occurrences.TryGetValue(member.LuaName, out int count);
			occurrences[member.LuaName] = count + 1;
		}

		List<LuaThunkModel> thunks = [];
		foreach (LuaFunctionModel member in members)
		{
			if (occurrences[member.LuaName] == 1)
			{
				thunks.Add(member.Thunk!);
			}
		}

		thunks.Sort(static (left, right) => string.CompareOrdinal(left.LuaName, right.LuaName));
		return new EquatableArray<LuaThunkModel>([.. thunks]);
	}
}
