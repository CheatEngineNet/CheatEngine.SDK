using System.Collections.Immutable;
using CheatEngine.SDK.SourceGenerators.Shared;
using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Infrastructure;

/// <summary>Assertions over <see cref="GeneratorRunResult.TrackedSteps" /> (the incrementality gate).</summary>
internal static class StepAssert
{
    /// <summary>Name Roslyn gives to the step of a <c>RegisterSourceOutput</c> callback.</summary>
    public const string SourceOutputStep = "SourceOutput";

    /// <summary>
    ///     Every CheatEngine.SDK-named step ran, produced something, and produced only <c>Cached</c>/<c>Unchanged</c> values;
    ///     no
    ///     source output was re-executed. Also fails when a step exists that the generator's name list does not know,
    ///     so a new step cannot slip past the gate. The compilation must therefore exercise both pipelines.
    /// </summary>
    public static void NothingWasRecomputed(GeneratorRunResult result)
    {
        string[] trackedCheatEngineSdkSteps =
            [.. result.TrackedSteps.Keys.Where(TrackingNames.IsCheatEngineSdkStep).Order(StringComparer.Ordinal)];
        Assert.All(trackedCheatEngineSdkSteps,
            stepName => Assert.Contains(stepName, LuaBindingsTrackingNames.All, StringComparer.Ordinal));

        foreach (var stepName in trackedCheatEngineSdkSteps)
            Assert.All(
                Reasons(result, stepName),
                reason => Assert.True(
                    reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
                    $"Step '{stepName}' was recomputed: {reason}."));

        Assert.All(OutputReasons(result), static reason => Assert.Equal(IncrementalStepRunReason.Cached, reason));
    }

    /// <summary>Reasons of every output of the step named <paramref name="stepName" />; fails when there is none.</summary>
    public static ImmutableArray<IncrementalStepRunReason> Reasons(GeneratorRunResult result, string stepName)
    {
        Assert.True(result.TrackedSteps.ContainsKey(stepName), $"Step '{stepName}' was not tracked.");

        ImmutableArray<IncrementalStepRunReason> reasons =
        [
            .. result.TrackedSteps[stepName].SelectMany(static step => step.Outputs)
                .Select(static output => output.Reason)
        ];

        Assert.False(reasons.IsEmpty, $"Step '{stepName}' produced no output: the assertion would be vacuous.");
        return reasons;
    }

    /// <summary>Reasons of every source output (both pipelines register one); fails when there is none.</summary>
    public static ImmutableArray<IncrementalStepRunReason> OutputReasons(GeneratorRunResult result)
    {
        Assert.True(result.TrackedOutputSteps.ContainsKey(SourceOutputStep), "The source output step was not tracked.");

        ImmutableArray<IncrementalStepRunReason> reasons =
        [
            .. result.TrackedOutputSteps[SourceOutputStep].SelectMany(static step => step.Outputs)
                .Select(static output => output.Reason)
        ];

        Assert.False(reasons.IsEmpty, "The source output step produced no output: the assertion would be vacuous.");
        return reasons;
    }
}
