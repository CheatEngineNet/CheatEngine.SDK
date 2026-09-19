using CESDK.SourceGenerators.Shared;

namespace CESDK.SourceGenerators.EntryPoint.Tests.SharedCode;

public sealed class TrackingNamesTests
{
    [Theory]
    [InlineData("CESDK.EntryPoint.Plugin", true)]
    [InlineData("CESDK.", true)]
    [InlineData("cesdk.EntryPoint.Plugin", false)]
    [InlineData("SourceOutput", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsCesdkStep_recognises_the_prefix_ordinally(string? stepName, bool expected)
    {
        Assert.Equal(expected, TrackingNames.IsCesdkStep(stepName));
    }

    [Fact]
    public void Entry_point_step_names_follow_the_convention_and_are_unique()
    {
        Assert.Equal(6, EntryPointTrackingNames.All.Length);
        Assert.Equal(EntryPointTrackingNames.All.Length,
            EntryPointTrackingNames.All.Distinct(StringComparer.Ordinal).Count());
        Assert.All(
            EntryPointTrackingNames.All,
            static name => Assert.StartsWith(TrackingNames.Prefix + "EntryPoint.", name, StringComparison.Ordinal));
    }
}
