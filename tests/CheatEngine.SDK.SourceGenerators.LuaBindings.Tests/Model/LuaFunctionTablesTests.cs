using System.Collections.Immutable;
using CheatEngine.SDK.SourceGenerators.LuaBindings.Model;
using CheatEngine.SDK.SourceGenerators.Shared;
using CheatEngine.SDK.SourceGenerators.Shared.LuaBindings.Model;
using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Model;

/// <summary>
///     The grouping step of the <c>[LuaFunction]</c> pipeline: per-type tables, duplicate names, ordering, invalid
///     members.
/// </summary>
public sealed class LuaFunctionTablesTests
{
    private static readonly ContainingTypeModel Alpha = Type("global::Demo.Alpha", "Alpha");
    private static readonly ContainingTypeModel Zeta = Type("global::Demo.Zeta", "Zeta");

    [Fact]
    public void Group_sorts_tables_by_type_and_thunks_by_lua_name()
    {
        var tables = LuaFunctionTables.Group(
        [
            Function(Zeta, "z"),
            Function(Alpha, "b"),
            Function(Alpha, "a")
        ]);

        Assert.Equal(2, tables.Length);
        Assert.Same(Alpha, tables[0].ContainingType);
        Assert.Equal(["a", "b"], tables[0].Thunks.AsImmutableArray().Select(static thunk => thunk.LuaName),
            StringComparer.Ordinal);
        Assert.Same(Zeta, tables[1].ContainingType);
        Assert.Equal(["z"], tables[1].Thunks.AsImmutableArray().Select(static thunk => thunk.LuaName),
            StringComparer.Ordinal);
    }

    [Fact]
    public void Group_drops_every_member_of_a_duplicated_name_within_a_type_only()
    {
        var tables = LuaFunctionTables.Group(
        [
            Function(Alpha, "twin"),
            Function(Alpha, "twin"),
            Function(Alpha, "single"),
            Function(Zeta, "twin")
        ]);

        Assert.Equal(2, tables.Length);
        Assert.Equal(["single"], tables[0].Thunks.AsImmutableArray().Select(static thunk => thunk.LuaName),
            StringComparer.Ordinal);
        Assert.Equal(["twin"], tables[1].Thunks.AsImmutableArray().Select(static thunk => thunk.LuaName),
            StringComparer.Ordinal);
    }

    [Fact]
    public void Group_skips_invalid_members_and_types_left_without_a_valid_one()
    {
        var invalid = Function(Alpha, "bad") with { Issues = LuaFunctionShapeIssues.NotStatic, Thunk = null };
        var invalidType = Function(Zeta, "ok") with { ContainingTypeIssues = ContainingTypeIssues.NotPartial };

        var tables = LuaFunctionTables.Group([invalid, invalidType, Function(Alpha, "good")]);

        var table = Assert.Single(tables);
        Assert.Equal("good", Assert.Single(table.Thunks).LuaName);
        Assert.True(LuaFunctionTables.Group([invalid]).IsEmpty);
        Assert.True(LuaFunctionTables.Group([]).IsEmpty);
        Assert.True(LuaFunctionTables.Group(default).IsEmpty);
    }

    [Fact]
    public void Group_is_a_pure_function_of_its_input()
    {
        ImmutableArray<LuaFunctionModel> models = [Function(Alpha, "a"), Function(Zeta, "z")];

        Assert.Equal(LuaFunctionTables.Group(models), LuaFunctionTables.Group(models));
        Assert.Equal(LuaFunctionTables.Group(models).GetHashCode(), LuaFunctionTables.Group(models).GetHashCode());
    }

    [Fact]
    public void Group_assigns_case_insensitive_collision_names_independent_of_input_order()
    {
        var upper = Type("global::Demo.Type", "Type");
        var lower = Type("global::Demo.type", "type");
        var forward = LuaFunctionTables.Group([Function(lower, "lower"), Function(upper, "upper")]);
        var reverse = LuaFunctionTables.Group([Function(upper, "upper"), Function(lower, "lower")]);

        AssertHintNames(forward,
            HintNames.ForType("Demo.Type", LuaFunctionTableModel.HintSuffix),
            HintNames.Disambiguated("Demo.type", LuaFunctionTableModel.HintSuffix));
        AssertHintNames(reverse,
            HintNames.ForType("Demo.Type", LuaFunctionTableModel.HintSuffix),
            HintNames.Disambiguated("Demo.type", LuaFunctionTableModel.HintSuffix));
    }

    private static ContainingTypeModel Type(string fullyQualifiedName, string name)
    {
        return new ContainingTypeModel("Demo",
            new EquatableArray<TypeDeclarationModel>([new TypeDeclarationModel("class", name)]),
            fullyQualifiedName, "Demo." + name);
    }

    private static LuaFunctionModel Function(ContainingTypeModel type, string luaName)
    {
        return new LuaFunctionModel(
            type,
            ContainingTypeIssues.None,
            luaName,
            LuaFunctionShapeIssues.None,
            new LuaThunkModel(luaName, LuaThunkModel.ThunkNameFor(luaName), type.FullyQualifiedName + ".M", false,
                EquatableArray<LuaArgumentModel>.Empty, null));
    }

    private static void AssertHintNames(EquatableArray<LuaFunctionTableModel> tables, string first, string second)
    {
        Assert.Equal(2, tables.Length);
        Assert.Equal(first, tables[0].HintName, StringComparer.Ordinal);
        Assert.Equal(second, tables[1].HintName, StringComparer.Ordinal);
    }
}
