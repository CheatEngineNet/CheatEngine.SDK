using CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.Infrastructure;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CheatEngine.SDK.SourceGenerators.EntryPoint.Tests.Generator;

/// <summary>
///     The cacheability gate of the pipeline: edits that cannot change the output must leave every tracked step
///     <c>Cached</c>/<c>Unchanged</c>, and edits that can must reach the source output.
/// </summary>
public sealed class IncrementalityTests(RoslynFixture roslyn) : IClassFixture<RoslynFixture>
{
	[Fact]
	public void Pipeline_first_run_tracks_every_named_step()
	{
		GeneratorRun run = roslyn.Run(PluginSources.Nominal);

		foreach (string stepName in EntryPointTrackingNames.All)
		{
			Assert.All(StepAssert.Reasons(run.Result, stepName),
				static reason => Assert.Equal(IncrementalStepRunReason.New, reason));
		}

		Assert.All(StepAssert.OutputReasons(run.Result),
			static reason => Assert.Equal(IncrementalStepRunReason.New, reason));
	}

	[Fact]
	public void Pipeline_unrelated_class_added_in_another_file_recomputes_nothing()
	{
		CSharpCompilation compilation = roslyn.CreateCompilation(PluginSources.Nominal);
		GeneratorRun first = RoslynFixture.Run(compilation);

		GeneratorRun second = GeneratorRun.Execute(
			first.Driver,
			compilation.AddSyntaxTrees(RoslynFixture.Parse("namespace Demo; public sealed class Unrelated { }",
				"Unrelated.cs")));

		StepAssert.NothingWasRecomputed(second.Result);
		Assert.Equal(first.SingleGeneratedText, second.SingleGeneratedText);
	}

	[Fact]
	public void Pipeline_comment_added_to_the_plugin_file_recomputes_nothing()
	{
		// The attributed node itself is re-parsed, so the transform runs again: the step must come out Unchanged
		// because the model compares by value.
		CSharpCompilation compilation = roslyn.CreateCompilation(PluginSources.Nominal);
		GeneratorRun first = RoslynFixture.Run(compilation);

		SyntaxTree original = compilation.SyntaxTrees.Single();
		SyntaxTree edited = RoslynFixture.Parse("// an unrelated comment\n" + PluginSources.Nominal + "\n// trailing",
			original.FilePath);
		GeneratorRun second = GeneratorRun.Execute(first.Driver, compilation.ReplaceSyntaxTree(original, edited));

		StepAssert.NothingWasRecomputed(second.Result);
		Assert.Contains(IncrementalStepRunReason.Unchanged,
			StepAssert.Reasons(second.Result, EntryPointTrackingNames.Plugin));
	}

	[Fact]
	public void Pipeline_member_added_to_the_plugin_class_recomputes_nothing()
	{
		CSharpCompilation compilation = roslyn.CreateCompilation(PluginSources.Nominal);
		GeneratorRun first = RoslynFixture.Run(compilation);

		SyntaxTree original = compilation.SyntaxTrees.Single();
		string editedSource = PluginSources.Nominal.Replace(
			"protected override void OnEnable() { }",
			"private int _counter;\n\n    protected override void OnEnable() { _counter++; }",
			StringComparison.Ordinal);
		Assert.NotEqual(PluginSources.Nominal, editedSource, StringComparer.Ordinal);
		GeneratorRun second = GeneratorRun.Execute(
			first.Driver,
			compilation.ReplaceSyntaxTree(original, RoslynFixture.Parse(editedSource, original.FilePath)));

		StepAssert.NothingWasRecomputed(second.Result);
	}

	[Fact]
	public void Pipeline_identical_compilation_recomputes_nothing()
	{
		CSharpCompilation compilation = roslyn.CreateCompilation(PluginSources.Nominal);
		GeneratorRun first = RoslynFixture.Run(compilation);

		GeneratorRun second = GeneratorRun.Execute(first.Driver, compilation);

		StepAssert.NothingWasRecomputed(second.Result);
	}

	[Fact]
	public void Pipeline_attribute_argument_edited_reruns_the_output()
	{
		CSharpCompilation compilation = roslyn.CreateCompilation(PluginSources.Nominal);
		GeneratorRun first = RoslynFixture.Run(compilation);

		SyntaxTree original = compilation.SyntaxTrees.Single();
		string editedSource =
			PluginSources.Nominal.Replace("\"Demo Plugin\"", "\"Renamed Plugin\"", StringComparison.Ordinal);
		GeneratorRun second = GeneratorRun.Execute(
			first.Driver,
			compilation.ReplaceSyntaxTree(original, RoslynFixture.Parse(editedSource, original.FilePath)));

		Assert.Equal([IncrementalStepRunReason.Modified],
			StepAssert.Reasons(second.Result, EntryPointTrackingNames.Plugin));
		Assert.Equal([IncrementalStepRunReason.Modified],
			StepAssert.Reasons(second.Result, EntryPointTrackingNames.Plugins));
		Assert.Equal([IncrementalStepRunReason.Modified],
			StepAssert.Reasons(second.Result, EntryPointTrackingNames.Bootstrap));
		Assert.Equal([IncrementalStepRunReason.Modified], StepAssert.OutputReasons(second.Result));
		Assert.All(
			StepAssert.Reasons(second.Result, EntryPointTrackingNames.Options),
			static reason =>
				Assert.True(reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged));
		Assert.Equal(ExpectedBootstrap.Text("global::Demo.DemoPlugin", "\"Renamed Plugin\"u8"),
			second.SingleGeneratedText);
	}

	[Fact]
	public void Pipeline_plugin_class_renamed_reruns_the_output()
	{
		CSharpCompilation compilation = roslyn.CreateCompilation(PluginSources.Nominal);
		GeneratorRun first = RoslynFixture.Run(compilation);

		SyntaxTree original = compilation.SyntaxTrees.Single();
		string editedSource =
			PluginSources.Nominal.Replace("class DemoPlugin", "class RenamedPlugin", StringComparison.Ordinal);
		GeneratorRun second = GeneratorRun.Execute(
			first.Driver,
			compilation.ReplaceSyntaxTree(original, RoslynFixture.Parse(editedSource, original.FilePath)));

		Assert.Equal([IncrementalStepRunReason.Modified], StepAssert.OutputReasons(second.Result));
		Assert.Equal(ExpectedBootstrap.Text("global::Demo.RenamedPlugin", "\"Demo Plugin\"u8"),
			second.SingleGeneratedText);
	}

	[Fact]
	public void Pipeline_plugin_class_marked_experimental_reruns_the_output()
	{
		// The third string of the final model: the diagnostic IDs the class declares.
		CSharpCompilation compilation = roslyn.CreateCompilation(PluginSources.Nominal);
		GeneratorRun first = RoslynFixture.Run(compilation);

		SyntaxTree original = compilation.SyntaxTrees.Single();
		string editedSource = PluginSources.Nominal.Replace(
			"public sealed class DemoPlugin",
			"[System.Diagnostics.CodeAnalysis.Experimental(\"EXP001\")] public sealed class DemoPlugin",
			StringComparison.Ordinal);
		GeneratorRun second = GeneratorRun.Execute(
			first.Driver,
			compilation.ReplaceSyntaxTree(original, RoslynFixture.Parse(editedSource, original.FilePath)));

		Assert.Equal([IncrementalStepRunReason.Modified],
			StepAssert.Reasons(second.Result, EntryPointTrackingNames.Bootstrap));
		Assert.Equal([IncrementalStepRunReason.Modified], StepAssert.OutputReasons(second.Result));
		Assert.Equal(
			ExpectedBootstrap.Text("global::Demo.DemoPlugin", "\"Demo Plugin\"u8", declaredDiagnosticIds: "EXP001"),
			second.SingleGeneratedText);
	}

	[Fact]
	public void Pipeline_invalid_second_plugin_added_keeps_the_output_cached()
	{
		// The plugin list changes, the final model does not: this is what the extra Bootstrap projection buys.
		CSharpCompilation compilation = roslyn.CreateCompilation(PluginSources.Nominal);
		GeneratorRun first = RoslynFixture.Run(compilation);

		const string Invalid =
			"[CheatEngine.SDK.Annotations.Plugin.CheatEnginePlugin(\"Broken\")] public abstract class Broken : CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin { }";
		GeneratorRun second = GeneratorRun.Execute(first.Driver,
			compilation.AddSyntaxTrees(RoslynFixture.Parse(Invalid, "Broken.cs")));

		Assert.Equal([IncrementalStepRunReason.Modified],
			StepAssert.Reasons(second.Result, EntryPointTrackingNames.Plugins));
		Assert.Equal([IncrementalStepRunReason.Unchanged],
			StepAssert.Reasons(second.Result, EntryPointTrackingNames.Bootstrap));
		Assert.Equal([IncrementalStepRunReason.Cached], StepAssert.OutputReasons(second.Result));
		Assert.Equal(first.SingleGeneratedText, second.SingleGeneratedText);
	}

	[Fact]
	public void Pipeline_second_valid_plugin_added_removes_the_output()
	{
		CSharpCompilation compilation = roslyn.CreateCompilation(PluginSources.Nominal);
		GeneratorRun first = RoslynFixture.Run(compilation);
		Assert.Single(first.GeneratedSources);

		GeneratorRun second = GeneratorRun.Execute(
			first.Driver,
			compilation.AddSyntaxTrees(RoslynFixture.Parse(PluginSources.WithNameExpression("\"Other\"", "OtherPlugin"),
				"Other.cs")));

		second.AssertNoOutput();
		Assert.Equal([IncrementalStepRunReason.Modified],
			StepAssert.Reasons(second.Result, EntryPointTrackingNames.Bootstrap));
	}

	[Fact]
	public void Pipeline_user_declared_entry_point_type_added_removes_the_output()
	{
		CSharpCompilation compilation = roslyn.CreateCompilation(PluginSources.Nominal);
		GeneratorRun first = RoslynFixture.Run(compilation);
		Assert.Single(first.GeneratedSources);

		GeneratorRun second = GeneratorRun.Execute(
			first.Driver,
			compilation.AddSyntaxTrees(RoslynFixture.Parse(
				"namespace CESDK { public static class CESDK { } }",
				"UserEntryPoint.cs")));

		second.AssertNoOutput();
		Assert.Equal([IncrementalStepRunReason.Modified],
			StepAssert.Reasons(second.Result, EntryPointTrackingNames.EntryPointTypeCollision));
		Assert.Equal([IncrementalStepRunReason.Modified],
			StepAssert.Reasons(second.Result, EntryPointTrackingNames.Bootstrap));
	}

	[Fact]
	public void Pipeline_build_property_switched_off_removes_the_output()
	{
		CSharpCompilation compilation = roslyn.CreateCompilation(PluginSources.Nominal);
		GeneratorRun first = RoslynFixture.Run(compilation);
		Assert.Single(first.GeneratedSources);

		GeneratorDriver switchedOff = first.Driver.WithUpdatedAnalyzerConfigOptions(
			TestAnalyzerConfigOptionsProvider.WithBuildProperty("CheatEngineSdkGenerateEntryPoint", "false"));
		GeneratorRun second = GeneratorRun.Execute(switchedOff, compilation);

		second.AssertNoOutput();
		Assert.Equal([IncrementalStepRunReason.Modified],
			StepAssert.Reasons(second.Result, EntryPointTrackingNames.Options));
		Assert.All(
			StepAssert.Reasons(second.Result, EntryPointTrackingNames.Plugin),
			static reason =>
				Assert.True(reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged));
	}

	[Fact]
	public void Pipeline_equivalent_options_object_recomputes_nothing()
	{
		// A new provider instance with the same values: the parsed options compare equal, nothing flows further.
		CSharpCompilation compilation = roslyn.CreateCompilation(PluginSources.Nominal);
		GeneratorRun first = RoslynFixture.Run(compilation,
			TestAnalyzerConfigOptionsProvider.WithBuildProperty("CheatEngineSdkGenerateEntryPoint", "true"));

		GeneratorDriver sameValues = first.Driver.WithUpdatedAnalyzerConfigOptions(
			TestAnalyzerConfigOptionsProvider.WithBuildProperty("CheatEngineSdkGenerateEntryPoint", "TRUE"));
		GeneratorRun second = GeneratorRun.Execute(sameValues, compilation);

		StepAssert.NothingWasRecomputed(second.Result);
	}

	[Fact]
	public void Pipeline_step_values_hold_no_roslyn_objects()
	{
		GeneratorRun run = roslyn.Run(
			PluginSources.Nominal,
			"[CheatEngine.SDK.Annotations.Plugin.CheatEnginePlugin(\"Broken\")] public abstract class Broken : CheatEngine.SDK.Hosting.Plugin.CheatEnginePlugin { }");

		int visited = 0;
		foreach (string stepName in EntryPointTrackingNames.All)
		foreach (IncrementalGeneratorRunStep step in run.Result.TrackedSteps[stepName])
		foreach ((object value, IncrementalStepRunReason _) in step.Outputs)
		{
			visited += ModelGraph.AssertFreeOfRoslynObjects(value, stepName);
		}

		Assert.True(visited > 0, "No model object was visited: the assertion would be vacuous.");
	}
}
