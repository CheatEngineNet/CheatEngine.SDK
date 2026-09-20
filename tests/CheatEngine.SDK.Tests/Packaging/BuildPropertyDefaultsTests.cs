using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Packaging;

/// <summary>
///     <c>build/CheatEngine.SDK.props</c> defaults only the deployment setting, not unsafe compilation. A project that
///     has no <c>[LuaFunction]</c> remains at the SDK default (<see langword="false" />); a project that exports one opts
///     in
///     itself.
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

    [Fact]
    public void LuaFunction_consumer_with_explicit_unsafe_opt_in_builds()
    {
        Assert.True(fixture.LuaFunctionOptInConsumerBuildSucceeded,
            "The documented <AllowUnsafeBlocks>true</AllowUnsafeBlocks> opt-in did not allow a packed consumer to compile a Lua-function registration thunk.");
    }

    [Fact]
    public void LuaFunction_consumer_without_unsafe_opt_in_fails_with_CESDK2001()
    {
        Assert.False(fixture.LuaFunctionWithoutUnsafeConsumerBuildSucceeded,
            "A packed consumer that exports a Lua function unexpectedly built without enabling unsafe compilation.");
        Assert.Contains("CESDK2001", fixture.LuaFunctionWithoutUnsafeConsumerBuildOutput, StringComparison.Ordinal);
    }
}
