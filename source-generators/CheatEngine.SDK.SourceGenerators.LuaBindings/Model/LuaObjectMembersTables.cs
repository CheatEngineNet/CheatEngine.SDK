using System;
using System.Collections.Generic;
using System.Collections.Immutable;

using CheatEngine.SDK.SourceGenerators.Shared;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Model;

/// <summary>Groups valid object methods and properties by their borrowed-handle type in deterministic emission order.</summary>
internal static class LuaObjectMembersTables
{
	/// <summary>Stable suffix for one type's generated object-member file.</summary>
	public const string HintSuffix = ".LuaObjectMembers.g.cs";

	/// <summary>Creates tables without allowing an invalid member to suppress unrelated valid declarations.</summary>
	public static EquatableArray<LuaObjectMembersTableModel> Group(ImmutableArray<LuaObjectMethodModel> methods,
		ImmutableArray<LuaObjectPropertyModel> properties)
	{
		Dictionary<string, Members> groups = new(StringComparer.Ordinal);
		AddMethods(groups, methods);
		AddProperties(groups, properties);

		List<LuaObjectMembersTableModel> tables = [];
		foreach (KeyValuePair<string, Members> pair in groups)
		{
			pair.Value.Methods.Sort(static (left, right) => string.CompareOrdinal(left.SortKey, right.SortKey));
			pair.Value.Properties.Sort(static (left, right) => string.CompareOrdinal(left.SortKey, right.SortKey));
			tables.Add(new LuaObjectMembersTableModel(
				pair.Value.ContainingType,
				new EquatableArray<LuaObjectMethodModel>([.. pair.Value.Methods]),
				new EquatableArray<LuaObjectPropertyModel>([.. pair.Value.Properties]),
				string.Empty));
		}

		tables.Sort(static (left, right) =>
			string.CompareOrdinal(left.ContainingType.FullyQualifiedName, right.ContainingType.FullyQualifiedName));
		AssignHintNames(tables);
		return new EquatableArray<LuaObjectMembersTableModel>([.. tables]);
	}

	private static void AddMethods(Dictionary<string, Members> groups, ImmutableArray<LuaObjectMethodModel> methods)
	{
		foreach (LuaObjectMethodModel method in methods)
		{
			if (!method.IsValid)
			{
				continue;
			}

			GetOrCreate(groups, method.ContainingType).Methods.Add(method);
		}
	}

	private static void AddProperties(Dictionary<string, Members> groups,
		ImmutableArray<LuaObjectPropertyModel> properties)
	{
		foreach (LuaObjectPropertyModel property in properties)
		{
			if (!property.IsValid)
			{
				continue;
			}

			GetOrCreate(groups, property.ContainingType).Properties.Add(property);
		}
	}

	private static Members GetOrCreate(Dictionary<string, Members> groups, ContainingTypeModel type)
	{
		if (groups.TryGetValue(type.FullyQualifiedName, out Members? members))
		{
			return members;
		}

		members = new Members(type);
		groups.Add(type.FullyQualifiedName, members);
		return members;
	}

	private static void AssignHintNames(List<LuaObjectMembersTableModel> tables)
	{
		HashSet<string> used = HintNames.CreateUsedNames();
		for (int i = 0; i < tables.Count; i++)
		{
			string baseName = tables[i].ContainingType.HintBaseName;
			tables[i] = tables[i] with { HintName = HintNames.AllocateUnique(baseName, HintSuffix, used) };
		}
	}

	private sealed class Members(ContainingTypeModel containingType)
	{
		public ContainingTypeModel ContainingType
		{
			get;
		} = containingType;

		public List<LuaObjectMethodModel> Methods
		{
			get;
		} = [];

		public List<LuaObjectPropertyModel> Properties
		{
			get;
		} = [];
	}
}
