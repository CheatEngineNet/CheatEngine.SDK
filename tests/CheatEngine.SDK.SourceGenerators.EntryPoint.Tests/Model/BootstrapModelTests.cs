using CheatEngine.SDK.SourceGenerators.EntryPoint.Model;
using CheatEngine.SDK.SourceGenerators.Shared;
using CheatEngine.SDK.SourceGenerators.Shared.Shapes;

namespace CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.Model;

/// <summary>The "exactly one valid plugin" decision and the value equality the pipeline relies on (no Roslyn needed).</summary>
public sealed class BootstrapModelTests
{
    private static readonly EntryPointOptions On = new(true);
    private static readonly EntryPointOptions Off = new(false);

    private static readonly PluginModel ValidA = new("global::A", "Plugin A", "", PluginShapeIssues.None);
    private static readonly PluginModel ValidB = new("global::B", "Plugin B", "EXP001", PluginShapeIssues.None);

    private static readonly PluginModel Invalid = new("global::C", "Plugin C", "",
        PluginShapeIssues.Abstract | PluginShapeIssues.Generic);

    [Fact]
    public void Select_single_valid_plugin_yields_its_model()
    {
        var model = BootstrapModel.Select(new EquatableArray<PluginModel>([ValidA]), On);

        Assert.Equal(new BootstrapModel("global::A", "Plugin A", ""), model);
    }

    [Fact]
    public void Select_no_plugin_yields_null()
    {
        Assert.Null(BootstrapModel.Select(EquatableArray<PluginModel>.Empty, On));
        Assert.Null(BootstrapModel.Select(default, On));
    }

    [Fact]
    public void Select_two_valid_plugins_yields_null()
    {
        Assert.Null(BootstrapModel.Select(new EquatableArray<PluginModel>([ValidA, ValidB]), On));
    }

    [Fact]
    public void Select_only_invalid_plugins_yields_null()
    {
        Assert.Null(BootstrapModel.Select(new EquatableArray<PluginModel>([Invalid]), On));
    }

    [Fact]
    public void Select_invalid_plugins_do_not_count()
    {
        var model = BootstrapModel.Select(new EquatableArray<PluginModel>([Invalid, ValidB, Invalid]), On);

        Assert.Equal(new BootstrapModel("global::B", "Plugin B", "EXP001"), model);
    }

    [Fact]
    public void Select_generation_switched_off_yields_null()
    {
        Assert.Null(BootstrapModel.Select(new EquatableArray<PluginModel>([ValidA]), Off));
    }

    [Fact]
    public void Select_user_entry_point_type_collision_yields_null()
    {
        Assert.Null(BootstrapModel.Select(new EquatableArray<PluginModel>([ValidA]), On, true));
    }

    [Fact]
    public void PluginModel_is_valid_only_without_issues()
    {
        Assert.True(ValidA.IsValid);
        Assert.False(Invalid.IsValid);
        Assert.False((ValidA with { Issues = PluginShapeIssues.InvalidName }).IsValid);
    }

    [Fact]
    public void Models_compare_by_value()
    {
        Assert.Equal(new PluginModel("global::A", "Plugin A", "", PluginShapeIssues.None), ValidA);
        Assert.NotEqual(ValidA with { DisplayName = "other" }, ValidA);
        Assert.NotEqual(ValidA with { FullyQualifiedTypeName = "global::Z" }, ValidA);
        Assert.NotEqual(ValidA with { DeclaredDiagnosticIds = "EXP001" }, ValidA);
        Assert.NotEqual(ValidA with { Issues = PluginShapeIssues.Static }, ValidA);
        Assert.Equal(new EntryPointOptions(true), On);
        Assert.NotEqual(Off, On);
        Assert.Equal(
            new EquatableArray<PluginModel>([ValidA, Invalid]),
            new EquatableArray<PluginModel>([ValidA with { }, Invalid with { }]));
    }

    [Fact]
    public void PluginShapeIssues_flags_are_distinct_single_bits()
    {
        PluginShapeIssues[] flags =
            [.. Enum.GetValues<PluginShapeIssues>().Where(static flag => flag != PluginShapeIssues.None)];

        Assert.All(flags, static flag => Assert.True(int.IsPow2((int)flag), $"{flag} is not a single bit."));
        Assert.Equal(flags.Length, flags.Distinct().Count());
    }
}
