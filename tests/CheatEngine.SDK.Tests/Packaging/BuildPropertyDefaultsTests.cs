using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Packaging;

/// <summary>
///     <c>build/CheatEngine.SDK.props</c> defaults only the deployment setting, not unsafe compilation. A project that
///     has no <c>[LuaFunction]</c> remains at the SDK default (<c>false</c>); a project that exports one opts in itself.
/// </summary>
[Collection(PackagedUmbrellaSuite.Name)]
public sealed class BuildPropertyDefaultsTests(PackagedUmbrellaFixture fixture)
{
    [Fact]
    public void AllowUnsafeBlocks_remains_false_for_a_consumer_that_does_not_set_it()
    {
        Assert.Equal("false", fixture.DefaultProperties["AllowUnsafeBlocks"], true);
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
    public void A_consumer_that_explicitly_sets_AllowUnsafeBlocks_false_remains_false()
    {
        Assert.Equal("false", fixture.ExplicitUnsafeFalseProperties["AllowUnsafeBlocks"], true);
    }
}
