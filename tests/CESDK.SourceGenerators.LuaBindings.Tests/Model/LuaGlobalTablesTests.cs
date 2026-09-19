using CESDK.SourceGenerators.LuaBindings.Model;
using CESDK.SourceGenerators.Shared;
using CESDK.SourceGenerators.Shared.LuaBindings.Model;
using CESDK.SourceGenerators.Shared.LuaEmit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CESDK.SourceGenerators.LuaBindings.Tests.Model;

/// <summary>The grouping step of the <c>[LuaGlobal]</c> pipeline and the compilation facts.</summary>
public sealed class LuaGlobalTablesTests
{
    private static readonly ContainingTypeModel Memory =
        new("Demo", new EquatableArray<TypeDeclarationModel>([new TypeDeclarationModel("class", "Memory")]),
            "global::Demo.Memory", "Demo.Memory");

    [Fact]
    public void Group_lists_each_global_once_and_sorts_bodies_by_sort_key()
    {
        var tables = LuaGlobalTables.Group(
        [
            Global("readInteger", "TryReadInt32(nuint, out int)"),
            Global("readString", "TryReadString(nuint, int, out string)"),
            Global("readInteger", "ReadInt32(nuint)")
        ]);

        var table = Assert.Single(tables);
        Assert.Equal(["readInteger", "readString"], table.CachedGlobals.AsImmutableArray(), StringComparer.Ordinal);
        Assert.Equal(
            ["ReadInt32(nuint)", "TryReadInt32(nuint, out int)", "TryReadString(nuint, int, out string)"],
            table.Calls.AsImmutableArray().Select(static call => call.MethodName),
            StringComparer.Ordinal);
    }

    [Fact]
    public void Group_skips_invalid_members_and_empty_inputs()
    {
        var invalid = Global("g", "G()") with { Issues = LuaGlobalShapeIssues.NotPartialDefinition, Call = null };

        Assert.True(LuaGlobalTables.Group([invalid]).IsEmpty);
        Assert.True(LuaGlobalTables.Group([]).IsEmpty);
        Assert.True(LuaGlobalTables.Group(default).IsEmpty);
        Assert.Single(LuaGlobalTables.Group([invalid, Global("g", "G()")]));
    }

    [Fact]
    public void CompilationFacts_reads_allow_unsafe_from_csharp_options_only()
    {
        var unsafeOn = CSharpCompilation.Create("a",
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        var unsafeOff = CSharpCompilation.Create("b",
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: false));

        Assert.True(CompilationFacts.From(unsafeOn).AllowUnsafeBlocks);
        Assert.False(CompilationFacts.From(unsafeOff).AllowUnsafeBlocks);
        Assert.False(CompilationFacts.From(null!).AllowUnsafeBlocks);
        Assert.Equal(CompilationFacts.From(unsafeOn), CompilationFacts.From(unsafeOn));
    }

    private static LuaGlobalModel Global(string name, string sortKey)
    {
        return new LuaGlobalModel(
            Memory,
            ContainingTypeIssues.None,
            LuaGlobalShapeIssues.None,
            new LuaGlobalCallModel(name, LuaGlobalCallModel.CacheFieldFor(name), "public static partial", sortKey,
                string.Empty, EquatableArray<LuaArgumentModel>.Empty, LuaCallForm.Throwing,
                EquatableArray<LuaResultModel>.Empty, null, false),
            sortKey);
    }
}
