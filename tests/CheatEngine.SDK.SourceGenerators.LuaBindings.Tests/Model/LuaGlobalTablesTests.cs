using CheatEngine.SDK.SourceGenerators.LuaBindings.Model;
using CheatEngine.SDK.SourceGenerators.Shared;
using CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Model;
using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Model;

/// <summary>The grouping step of the <c>[LuaGlobal]</c> pipeline and the compilation facts.</summary>
public sealed class LuaGlobalTablesTests
{
	private static readonly ContainingTypeModel Memory =
		new("Demo", new EquatableArray<TypeDeclarationModel>([new TypeDeclarationModel("class", "Memory")]),
			"global::Demo.Memory", "Demo.Memory");

	[Fact]
	public void Group_lists_each_global_once_and_sorts_bodies_by_sort_key()
	{
		EquatableArray<LuaGlobalTableModel> tables = LuaGlobalTables.Group(
		[
			Global("readInteger", "TryReadInt32(nuint, out int)"),
			Global("readString", "TryReadString(nuint, int, out string)"),
			Global("readInteger", "ReadInt32(nuint)")
		]);

		LuaGlobalTableModel table = Assert.Single(tables);
		Assert.Equal(["readInteger", "readString"], table.CachedGlobals.AsImmutableArray(), StringComparer.Ordinal);
		Assert.Equal(
			["ReadInt32(nuint)", "TryReadInt32(nuint, out int)", "TryReadString(nuint, int, out string)"],
			table.Calls.AsImmutableArray().Select(static call => call.MethodName),
			StringComparer.Ordinal);
	}

	[Fact]
	public void Group_skips_invalid_members_and_empty_inputs()
	{
		LuaGlobalModel invalid = Global("g", "G()") with
		{
			Issues = LuaGlobalShapeIssues.NotPartialDefinition,
			Call = null
		};

		Assert.True(LuaGlobalTables.Group([invalid]).IsEmpty);
		Assert.True(LuaGlobalTables.Group([]).IsEmpty);
		Assert.True(LuaGlobalTables.Group(default).IsEmpty);
		Assert.Single(LuaGlobalTables.Group([invalid, Global("g", "G()")]));
	}

	[Fact]
	public void CompilationFacts_reads_allow_unsafe_from_csharp_options_only()
	{
		CSharpCompilation unsafeOn = CSharpCompilation.Create("a",
			options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
		CSharpCompilation unsafeOff = CSharpCompilation.Create("b",
			options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: false));

		Assert.True(CompilationFacts.From(unsafeOn).AllowUnsafeBlocks);
		Assert.False(CompilationFacts.From(unsafeOff).AllowUnsafeBlocks);
		Assert.False(CompilationFacts.From(null!).AllowUnsafeBlocks);
		Assert.Equal(CompilationFacts.From(unsafeOn), CompilationFacts.From(unsafeOn));
	}

	[Fact]
	public void Group_assigns_case_insensitive_collision_names_independent_of_input_order()
	{
		ContainingTypeModel upper = Type("global::Demo.Type", "Type");
		ContainingTypeModel lower = Type("global::Demo.type", "type");
		EquatableArray<LuaGlobalTableModel> forward =
			LuaGlobalTables.Group([Global(lower, "lower", "Lower()"), Global(upper, "upper", "Upper()")]);
		EquatableArray<LuaGlobalTableModel> reverse =
			LuaGlobalTables.Group([Global(upper, "upper", "Upper()"), Global(lower, "lower", "Lower()")]);

		AssertHintNames(forward,
			HintNames.ForType("Demo.Type", LuaGlobalTableModel.HintSuffix),
			HintNames.Disambiguated("Demo.type", LuaGlobalTableModel.HintSuffix));
		AssertHintNames(reverse,
			HintNames.ForType("Demo.Type", LuaGlobalTableModel.HintSuffix),
			HintNames.Disambiguated("Demo.type", LuaGlobalTableModel.HintSuffix));
	}

	[Fact]
	public void Optional_argument_model_is_value_equal_across_runs()
	{
		LuaGlobalCallModel first = OptionalCall();
		LuaGlobalCallModel second = OptionalCall();

		Assert.Equal(first, second);
		Assert.Equal(first.GetHashCode(), second.GetHashCode());
		Assert.NotEqual(first, first with
		{
			Arguments = new EquatableArray<LuaArgumentModel>([
				new LuaArgumentModel("address", LuaValueKind.Address, false),
				new LuaArgumentModel("count", LuaValueKind.Int32, false)
			])
		});
		Assert.NotEqual(first,
			first with
			{
				Results = new EquatableArray<LuaResultModel>([LuaResultModel.Value(LuaValueKind.Int64, "value")])
			});
		Assert.Equal(
			new LuaArgumentModel("count", LuaValueKind.Int32, false, false, null, null),
			new LuaArgumentModel("count", LuaValueKind.Int32, false));
		Assert.False(new LuaArgumentModel("count", LuaValueKind.Int32, false, false, null, null).IsOptional);
	}

	private static LuaGlobalCallModel OptionalCall()
	{
		return new LuaGlobalCallModel("readBytes", LuaGlobalCallModel.CacheFieldFor("readBytes"),
			"public static partial",
			"ReadBytes", string.Empty,
			new EquatableArray<LuaArgumentModel>([
				new LuaArgumentModel("address", LuaValueKind.Address, false),
				LuaArgumentModel.Optional("count", LuaValueKind.Int32)
			]),
			LuaCallForm.Outcome,
			new EquatableArray<LuaResultModel>([
				LuaResultModel.Optional(LuaValueKind.Int64, "value"),
				LuaResultModel.Variadic(LuaValueKind.Int32, "values", "count")
			]),
			null, false);
	}

	private static LuaGlobalModel Global(string name, string sortKey)
	{
		return Global(Memory, name, sortKey);
	}

	private static LuaGlobalModel Global(ContainingTypeModel type, string name, string sortKey)
	{
		return new LuaGlobalModel(
			type,
			ContainingTypeIssues.None,
			LuaGlobalShapeIssues.None,
			new LuaGlobalCallModel(name, LuaGlobalCallModel.CacheFieldFor(name), "public static partial", sortKey,
				string.Empty, EquatableArray<LuaArgumentModel>.Empty, LuaCallForm.Throwing,
				EquatableArray<LuaResultModel>.Empty, null, false),
			sortKey);
	}

	private static ContainingTypeModel Type(string fullyQualifiedName, string name)
	{
		return new ContainingTypeModel("Demo",
			new EquatableArray<TypeDeclarationModel>([new TypeDeclarationModel("class", name)]),
			fullyQualifiedName, "Demo." + name);
	}

	private static void AssertHintNames(EquatableArray<LuaGlobalTableModel> tables, string first, string second)
	{
		Assert.Equal(2, tables.Length);
		Assert.Equal(first, tables[0].HintName, StringComparer.Ordinal);
		Assert.Equal(second, tables[1].HintName, StringComparer.Ordinal);
	}
}
