using System.Globalization;
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

    [Fact]
    public void AllocateUnique_prepopulated_readable_name_uses_the_deterministic_hash_candidate()
    {
        const string TypeName = "Demo.Caf\u00E9";
        const string Suffix = ".g.cs";
        var readable = HintNames.ForType(TypeName, Suffix);
        var used = HintNames.CreateUsedNames();
        Assert.True(used.Add(readable));

        var hint = HintNames.AllocateUnique(TypeName, Suffix, used);

        Assert.NotEqual(readable, hint, StringComparer.Ordinal);
        Assert.Equal(HintNames.Disambiguated(TypeName, Suffix), hint, StringComparer.Ordinal);
        Assert.Contains(hint, used);
    }

    [Fact]
    public void AllocateUnique_third_collision_uses_the_first_available_ordinal_suffix()
    {
        const string TypeName = "Demo.Type";
        const string Suffix = ".g.cs";
        var readable = HintNames.ForType(TypeName, Suffix);
        var hashed = HintNames.Disambiguated(TypeName, Suffix);
        var second = WithOrdinal(hashed, Suffix, 2);
        var used = HintNames.CreateUsedNames();
        Assert.True(used.Add(readable));
        Assert.True(used.Add(hashed));
        Assert.True(used.Add(second));

        var hint = HintNames.AllocateUnique(TypeName, Suffix, used);

        Assert.Equal(WithOrdinal(hashed, Suffix, 3), hint, StringComparer.Ordinal);
        Assert.Contains(hint, used);
    }

    [Fact]
    public void AllocateUnique_rejects_a_case_sensitive_reservation_set()
    {
        Assert.Throws<ArgumentException>(() =>
            HintNames.AllocateUnique("Demo.Type", ".g.cs", new HashSet<string>(StringComparer.Ordinal)));
    }

    private static string WithOrdinal(string hintName, string suffix, int ordinal)
    {
        return hintName[..^suffix.Length] + "_" + ordinal.ToString(CultureInfo.InvariantCulture) +
               suffix;
    }
}
