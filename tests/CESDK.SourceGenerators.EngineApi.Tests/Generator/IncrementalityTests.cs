using CESDK.SourceGenerators.EngineApi.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CESDK.SourceGenerators.EngineApi.Tests.Generator;

/// <summary>
///     The cacheability gate: editing the spec file re-runs only the affected output; editing an unrelated file in the
///     compilation does not. Unlike <c>CESDK.SourceGenerators.LuaBindings</c>, this pipeline has no
///     <c>CompilationProvider</c> input at all, so an unrelated compilation edit cannot touch it even in principle;
///     the interesting edits are on the <c>AdditionalTextsProvider</c> side (<c>ReplaceAdditionalText</c>,
///     <c>AddAdditionalTexts</c>, <c>RemoveAdditionalTexts</c>).
/// </summary>
public sealed class IncrementalityTests(RoslynFixture roslyn) : IClassFixture<RoslynFixture>
{
    [Fact]
    public void Pipeline_first_run_tracks_every_named_step_as_new()
    {
        var run = roslyn.Run("a.cesdk-api.txt", SpecSources.SingleTry);

        foreach (var stepName in EngineApiTrackingNames.All)
            Assert.All(StepAssert.Reasons(run.Result, stepName),
                static reason => Assert.Equal(IncrementalStepRunReason.New, reason));

        Assert.All(StepAssert.OutputReasons(run.Result),
            static reason => Assert.Equal(IncrementalStepRunReason.New, reason));
    }

    [Fact]
    public void Pipeline_identical_rerun_recomputes_nothing()
    {
        InMemoryAdditionalText text = new("a.cesdk-api.txt", SpecSources.SingleTry);
        var compilation = roslyn.CreateCompilation();
        var first = GeneratorRun.Execute(RoslynFixture.CreateDriver(text), compilation);

        var second = GeneratorRun.Execute(first.Driver, compilation);

        StepAssert.NothingWasRecomputed(second.Result);
        Assert.Equal(first.SingleGeneratedText, second.SingleGeneratedText, StringComparer.Ordinal);
    }

    [Fact]
    public void Pipeline_unrelated_compilation_edit_recomputes_nothing()
    {
        InMemoryAdditionalText text = new("a.cesdk-api.txt", SpecSources.SingleTry);
        var compilation = roslyn.CreateCompilation();
        var first = GeneratorRun.Execute(RoslynFixture.CreateDriver(text), compilation);

        var unrelated = CSharpSyntaxTree.ParseText(
            "namespace Demo; public sealed class Unrelated { }",
            RoslynEnvironment.ParseOptions,
            "Unrelated.cs",
            cancellationToken: TestContext.Current.CancellationToken);
        var second = GeneratorRun.Execute(first.Driver, compilation.AddSyntaxTrees(unrelated));

        StepAssert.NothingWasRecomputed(second.Result);
        Assert.Equal(first.SingleGeneratedText, second.SingleGeneratedText, StringComparer.Ordinal);
    }

    [Fact]
    public void Pipeline_editing_one_spec_files_text_reruns_only_that_files_output()
    {
        InMemoryAdditionalText original = new("a.cesdk-api.txt", SpecSources.SingleTry);
        InMemoryAdditionalText other = new("b.cesdk-api.txt", SpecSources.BeepOnly);
        var compilation = roslyn.CreateCompilation();
        var first = GeneratorRun.Execute(RoslynFixture.CreateDriver(original, other), compilation);
        Assert.Equal(2, first.GeneratedSources.Length);

        InMemoryAdditionalText edited = new("a.cesdk-api.txt",
            SpecSources.SingleTry.Replace("TryReadInt32", "TryReadRenamed", StringComparison.Ordinal));
        var updatedDriver = first.Driver.ReplaceAdditionalText(original, edited);
        var second = GeneratorRun.Execute(updatedDriver, compilation);

        Assert.Equal(2, second.GeneratedSources.Length);
        Assert.Contains(IncrementalStepRunReason.Modified,
            StepAssert.Reasons(second.Result, EngineApiTrackingNames.ParsedSpec));
        Assert.Contains(IncrementalStepRunReason.Modified,
            StepAssert.Reasons(second.Result, EngineApiTrackingNames.SpecFileOutput));
        Assert.Contains("TryReadRenamed", second.GeneratedTextByContent("TryReadRenamed"), StringComparison.Ordinal);
        Assert.Equal(first.GeneratedTextByContent("Beep"), second.GeneratedTextByContent("Beep"),
            StringComparer.Ordinal);
    }

    [Fact]
    public void Pipeline_adding_a_second_spec_file_leaves_the_first_files_output_cached()
    {
        InMemoryAdditionalText original = new("a.cesdk-api.txt", SpecSources.SingleTry);
        var compilation = roslyn.CreateCompilation();
        var first = GeneratorRun.Execute(RoslynFixture.CreateDriver(original), compilation);
        Assert.Single(first.GeneratedSources.AsEnumerable());

        InMemoryAdditionalText added = new("b.cesdk-api.txt", SpecSources.BeepOnly);
        var updatedDriver = first.Driver.AddAdditionalTexts([added]);
        var second = GeneratorRun.Execute(updatedDriver, compilation);

        Assert.Equal(2, second.GeneratedSources.Length);
        Assert.Equal(first.SingleGeneratedText, second.GeneratedTextByContent("TryReadInt32"), StringComparer.Ordinal);
        Assert.Contains("Beep", second.GeneratedTextByContent("Beep"), StringComparison.Ordinal);
    }

    [Fact]
    public void Pipeline_removing_a_spec_file_removes_only_its_output()
    {
        InMemoryAdditionalText a = new("a.cesdk-api.txt", SpecSources.SingleTry);
        InMemoryAdditionalText b = new("b.cesdk-api.txt", SpecSources.BeepOnly);
        var compilation = roslyn.CreateCompilation();
        var first = GeneratorRun.Execute(RoslynFixture.CreateDriver(a, b), compilation);
        Assert.Equal(2, first.GeneratedSources.Length);

        var updatedDriver = first.Driver.RemoveAdditionalTexts([b]);
        var second = GeneratorRun.Execute(updatedDriver, compilation);

        Assert.Single(second.GeneratedSources.AsEnumerable());
        Assert.Equal(first.GeneratedTextByContent("TryReadInt32"), second.SingleGeneratedText, StringComparer.Ordinal);
    }

    [Fact]
    public void Pipeline_step_values_hold_no_roslyn_objects()
    {
        var run = roslyn.Run("a.cesdk-api.txt", SpecSources.Memory);

        var visited = 0;
        foreach (var stepName in EngineApiTrackingNames.All)
        {
            if (string.Equals(stepName, EngineApiTrackingNames.SpecTextFile, StringComparison.Ordinal))
                // Legitimately holds the raw AdditionalText: that is the point of this filter step.
                continue;

            foreach (var step in run.Result.TrackedSteps[stepName])
            foreach (var (value, _) in step.Outputs)
                visited += ModelGraph.AssertFreeOfRoslynObjects(value, stepName);
        }

        Assert.True(visited > 0, "No model object was visited: the assertion would be vacuous.");
    }
}
