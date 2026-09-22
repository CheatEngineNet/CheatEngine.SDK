using System.Collections.Generic;
using System.Collections.Immutable;

using CheatEngine.SDK.SourceGenerators.LuaBindings.Emit;
using CheatEngine.SDK.SourceGenerators.Shared;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Model;

/// <summary>Filters and orders class handles while assigning deterministic, collision-free source hint names.</summary>
internal static class LuaClassTables
{
	/// <summary>Produces every valid class model, ordered by fully-qualified type name.</summary>
	public static EquatableArray<LuaClassModel> Select(ImmutableArray<LuaClassModel> models)
	{
		List<LuaClassModel> selected = [];
		foreach (LuaClassModel model in models)
		{
			if (model.IsValid)
			{
				selected.Add(model);
			}
		}

		selected.Sort(static (left, right) =>
			string.CompareOrdinal(left.ContainingType.FullyQualifiedName, right.ContainingType.FullyQualifiedName));

		return new EquatableArray<LuaClassModel>([.. AssignHintNames(selected)]);
	}

	// Source hints are case-insensitive to Roslyn even when type names are not. Resolve them after the stable type
	// order is known: the shared allocator reserves the readable candidate, then a deterministic hash and ordinal
	// suffixes, so every valid class keeps its output across repeated runs.
	private static List<LuaClassModel> AssignHintNames(List<LuaClassModel> models)
	{
		HashSet<string> used = HintNames.CreateUsedNames();
		for (int i = 0; i < models.Count; i++)
		{
			string baseName = models[i].ContainingType.HintBaseName;
			models[i] = models[i] with
			{
				HintName = HintNames.AllocateUnique(baseName, LuaClassFileEmitter.HintSuffix, used)
			};
		}

		return models;
	}
}
