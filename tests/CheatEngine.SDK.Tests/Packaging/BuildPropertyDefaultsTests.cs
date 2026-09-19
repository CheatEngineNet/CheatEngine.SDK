using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Packaging;

/// <summary>
///     <c>build/CheatEngine.SDK.props</c> defaults <c>AllowUnsafeBlocks</c> and <c>EnableDynamicLoading</c> to <c>true</c> with a
///     <c>Condition="'$(Prop)' == ''"</c> guard, relying on NuGet importing that file before the consumer's own
///     <c>PropertyGroup</c>. These tests verify that ordering: a default consumer
///     really gets <c>true</c> without asking, and a consumer that sets <c>AllowUnsafeBlocks=false</c> itself keeps
///     <c>false</c> - the package default never overrides an explicit choice.
/// </summary>
[Collection(PackagedUmbrellaSuite.Name)]
public sealed class BuildPropertyDefaultsTests(PackagedUmbrellaFixture fixture)
{
    [Fact]
    public void AllowUnsafeBlocks_defaults_to_true_for_a_consumer_that_does_not_set_it()
    {
        Assert.Equal("true", fixture.DefaultProperties["AllowUnsafeBlocks"], true);
    }

    [Fact]
    public void EnableDynamicLoading_defaults_to_true_for_a_consumer_that_does_not_set_it()
    {
        Assert.Equal("true", fixture.DefaultProperties["EnableDynamicLoading"], true);
    }

    [Fact]
    public void CheatEngineSdkGenerateEntryPoint_defaults_to_true_for_a_consumer_that_does_not_set_it()
    {
        Assert.Equal("true", fixture.DefaultProperties["CheatEngineSdkGenerateEntryPoint"], true);
    }

    [Fact]
    public void A_consumer_that_explicitly_sets_AllowUnsafeBlocks_false_is_not_overridden_by_the_package_default()
    {
        Assert.Equal("false", fixture.ExplicitUnsafeFalseProperties["AllowUnsafeBlocks"], true);
    }
}
