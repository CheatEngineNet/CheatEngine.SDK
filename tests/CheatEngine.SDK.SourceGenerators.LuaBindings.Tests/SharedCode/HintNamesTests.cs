using CheatEngine.SDK.SourceGenerators.Shared;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.SharedCode;

/// <summary>The per-type hint-name sanitiser of <c>Shared/HintNames.cs</c>.</summary>
public sealed class HintNamesTests
{
    [Theory]
    [InlineData("Demo.Functions", "Demo.Functions.LuaFunctions.g.cs")]
    [InlineData("Functions", "Functions.LuaFunctions.g.cs")]
    [InlineData("Demo.Outer.Inner_2", "Demo.Outer.Inner_2.LuaFunctions.g.cs")]
    public void ForType_plain_name_passes_through(string typeName, string expected)
    {
        Assert.Equal(expected, HintNames.ForType(typeName, ".LuaFunctions.g.cs"));
    }

    [Fact]
    public void ForType_replaced_characters_get_an_underscore_and_a_hash()
    {
        var hint = HintNames.ForType("Demo.Caf\u00E9", ".g.cs");

        Assert.StartsWith("Demo.Caf__", hint, StringComparison.Ordinal);
        Assert.EndsWith(".g.cs", hint, StringComparison.Ordinal);
        Assert.Equal("Demo.Caf__".Length + 8 + ".g.cs".Length, hint.Length);
        Assert.All(hint, c => Assert.True(c < 128, "non-ASCII character in a hint name"));
    }

    [Fact]
    public void ForType_names_differing_only_in_replaced_characters_stay_distinct()
    {
        var a = HintNames.ForType("Demo.Caf\u00E9", ".g.cs");
        var b = HintNames.ForType("Demo.Caf\u00E8", ".g.cs");
        var c = HintNames.ForType("Demo.Caf/", ".g.cs");

        Assert.NotEqual(a, b, StringComparer.Ordinal);
        Assert.NotEqual(a, c, StringComparer.Ordinal);
        Assert.Equal(a, HintNames.ForType("Demo.Caf\u00E9", ".g.cs"), StringComparer.Ordinal);
    }

    [Fact]
    public void ForType_rejects_null()
    {
        Assert.Throws<ArgumentNullException>(() => HintNames.ForType(null!, ".g.cs"));
        Assert.Throws<ArgumentNullException>(() => HintNames.ForType("A", null!));
    }
}
