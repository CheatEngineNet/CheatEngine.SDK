using System.Collections.Immutable;

using CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Infrastructure;
using CheatEngine.SDK.SourceGenerators.Shared;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Generator;

/// <summary>
///     The cacheability gate of every Lua-binding pipeline: edits that cannot change the output must leave every tracked
///     step
///     <c>Cached</c>/<c>Unchanged</c>, and edits that can must reach the source output of their pipeline only.
/// </summary>
public sealed class IncrementalityTests(RoslynFixture roslyn) : IClassFixture<RoslynFixture>
{
	private const string ObjectBindings = """
	                                      using CheatEngine.SDK.Annotations.Lua;

	                                      namespace Demo;

	                                      [LuaClass("Fixture")]
	                                      public readonly partial struct Fixture
	                                      {
	                                          [LuaMethod("getValue")]
	                                          public partial int GetValue();

	                                          [LuaProperty("Value")]
	                                          public partial int Value { get; }
	                                      }
	                                      """;

	[Fact]
	public void Pipeline_first_run_tracks_every_named_step()
	{
		GeneratorRun run = roslyn.Run(BindingSources.Functions, BindingSources.Globals, ObjectBindings);

		foreach (string stepName in run.Result.TrackedSteps.Keys
			         .Where(TrackingNames.IsCheatEngineSdkStep)
			         .Order(StringComparer.Ordinal))
		{
			Assert.All(StepAssert.Reasons(run.Result, stepName),
				static reason => Assert.Equal(IncrementalStepRunReason.New, reason));
		}

		Assert.Equal(4, StepAssert.OutputReasons(run.Result).Length);
		Assert.All(StepAssert.OutputReasons(run.Result),
			static reason => Assert.Equal(IncrementalStepRunReason.New, reason));
	}

	[Fact]
	public void Pipeline_unrelated_class_added_in_another_file_recomputes_nothing()
	{
		CSharpCompilation compilation = roslyn.CreateCompilation(BindingSources.Functions, BindingSources.Globals);
		GeneratorRun first = RoslynFixture.Run(compilation);

		GeneratorRun second = GeneratorRun.Execute(
			first.Driver,
			compilation.AddSyntaxTrees(RoslynFixture.Parse("namespace Demo; public sealed class Unrelated { }",
				"Unrelated.cs")));

		StepAssert.NothingWasRecomputed(second.Result);
		Assert.Equal(first.GeneratedText(ExpectedFiles.FunctionsHintName),
			second.GeneratedText(ExpectedFiles.FunctionsHintName));
		Assert.Equal(first.GeneratedText(ExpectedFiles.GlobalsHintName),
			second.GeneratedText(ExpectedFiles.GlobalsHintName));
	}

	[Fact]
	public void Pipeline_comment_added_to_a_binding_file_recomputes_nothing()
	{
		// The attributed nodes are re-parsed, so both transforms run again: the steps must come out Unchanged
		// because the models compare by value.
		CSharpCompilation compilation = roslyn.CreateCompilation(BindingSources.Functions, BindingSources.Globals);
		GeneratorRun first = RoslynFixture.Run(compilation);

		SyntaxTree original = compilation.SyntaxTrees.First();
		SyntaxTree edited = RoslynFixture.Parse(
			"// an unrelated comment\n" + BindingSources.Functions + "\n// trailing",
			original.FilePath);
		GeneratorRun second = GeneratorRun.Execute(first.Driver, compilation.ReplaceSyntaxTree(original, edited));

		StepAssert.NothingWasRecomputed(second.Result);
		Assert.Contains(IncrementalStepRunReason.Unchanged,
			StepAssert.Reasons(second.Result, LuaBindingsTrackingNames.LuaFunction));
	}

	[Fact]
	public void Pipeline_unattributed_member_added_to_a_binding_type_recomputes_nothing()
	{
		CSharpCompilation compilation = roslyn.CreateCompilation(BindingSources.Functions, BindingSources.Globals);
		GeneratorRun first = RoslynFixture.Run(compilation);

		SyntaxTree original = compilation.SyntaxTrees.First();
		string edited = BindingSources.Functions.Replace(
			"[LuaFunction(\"add\")]",
			"public static int Unrelated;\n\n    [LuaFunction(\"add\")]",
			StringComparison.Ordinal);
		Assert.NotEqual(BindingSources.Functions, edited, StringComparer.Ordinal);
		GeneratorRun second = GeneratorRun.Execute(first.Driver,
			compilation.ReplaceSyntaxTree(original, RoslynFixture.Parse(edited, original.FilePath)));

		StepAssert.NothingWasRecomputed(second.Result);
	}

	[Fact]
	public void Pipeline_identical_compilation_recomputes_nothing()
	{
		CSharpCompilation compilation = roslyn.CreateCompilation(BindingSources.Functions, BindingSources.Globals);
		GeneratorRun first = RoslynFixture.Run(compilation);

		GeneratorRun second = GeneratorRun.Execute(first.Driver, compilation);

		StepAssert.NothingWasRecomputed(second.Result);
	}

	[Fact]
	public void Pipeline_function_attribute_argument_edited_reruns_the_function_output_only()
	{
		CSharpCompilation compilation = roslyn.CreateCompilation(BindingSources.Functions, BindingSources.Globals);
		GeneratorRun first = RoslynFixture.Run(compilation);

		SyntaxTree original = compilation.SyntaxTrees.First();
		string edited = BindingSources.Functions.Replace("[LuaFunction(\"add\")]", "[LuaFunction(\"plus\")]",
			StringComparison.Ordinal);
		GeneratorRun second = GeneratorRun.Execute(first.Driver,
			compilation.ReplaceSyntaxTree(original, RoslynFixture.Parse(edited, original.FilePath)));

		Assert.Contains(IncrementalStepRunReason.Modified,
			StepAssert.Reasons(second.Result, LuaBindingsTrackingNames.LuaFunction));
		Assert.Equal([IncrementalStepRunReason.Modified],
			StepAssert.Reasons(second.Result, LuaBindingsTrackingNames.LuaFunctionTables));
		Assert.Equal([IncrementalStepRunReason.Modified],
			StepAssert.Reasons(second.Result, LuaBindingsTrackingNames.LuaFunctionOutput));
		AssertUntouched(second.Result, LuaBindingsTrackingNames.LuaGlobal, LuaBindingsTrackingNames.LuaGlobalTables,
			LuaBindingsTrackingNames.LuaGlobalOutput);
		Assert.Contains("__LuaThunk_plus", second.GeneratedText(ExpectedFiles.FunctionsHintName),
			StringComparison.Ordinal);
		Assert.Equal(first.GeneratedText(ExpectedFiles.GlobalsHintName),
			second.GeneratedText(ExpectedFiles.GlobalsHintName));
	}

	[Fact]
	public void Pipeline_function_signature_edited_reruns_the_function_output_only()
	{
		CSharpCompilation compilation = roslyn.CreateCompilation(BindingSources.Functions, BindingSources.Globals);
		GeneratorRun first = RoslynFixture.Run(compilation);

		SyntaxTree original = compilation.SyntaxTrees.First();
		string edited = BindingSources.Functions.Replace("public static long Add(long a, long b)",
			"public static int Add(int a, int b)", StringComparison.Ordinal);
		GeneratorRun second = GeneratorRun.Execute(first.Driver,
			compilation.ReplaceSyntaxTree(original, RoslynFixture.Parse(edited, original.FilePath)));

		Assert.Equal([IncrementalStepRunReason.Modified],
			StepAssert.Reasons(second.Result, LuaBindingsTrackingNames.LuaFunctionOutput));
		AssertUntouched(second.Result, LuaBindingsTrackingNames.LuaGlobalOutput);
		Assert.Contains("global::CheatEngine.SDK.Lua.Marshalling.Int32Marshaller.TryRead(__L, 1, out int __arg0)",
			second.GeneratedText(ExpectedFiles.FunctionsHintName), StringComparison.Ordinal);
	}

	[Fact]
	public void Pipeline_global_signature_edited_reruns_the_global_output_only()
	{
		CSharpCompilation compilation = roslyn.CreateCompilation(BindingSources.Functions, BindingSources.Globals);
		GeneratorRun first = RoslynFixture.Run(compilation);

		SyntaxTree original = compilation.SyntaxTrees.Last();
		string edited = BindingSources.Globals.Replace("out int value", "out long value", StringComparison.Ordinal);
		GeneratorRun second = GeneratorRun.Execute(first.Driver,
			compilation.ReplaceSyntaxTree(original, RoslynFixture.Parse(edited, original.FilePath)));

		Assert.Contains(IncrementalStepRunReason.Modified,
			StepAssert.Reasons(second.Result, LuaBindingsTrackingNames.LuaGlobal));
		Assert.Equal([IncrementalStepRunReason.Modified],
			StepAssert.Reasons(second.Result, LuaBindingsTrackingNames.LuaGlobalOutput));
		AssertUntouched(second.Result, LuaBindingsTrackingNames.LuaFunction, LuaBindingsTrackingNames.LuaFunctionTables,
			LuaBindingsTrackingNames.LuaFunctionOutput);
		Assert.Contains("TryReadInt32(nuint address, out long value)",
			second.GeneratedText(ExpectedFiles.GlobalsHintName), StringComparison.Ordinal);
	}

	[Fact]
	public void Pipeline_invalid_member_added_keeps_the_table_unchanged_and_the_output_cached()
	{
		// The collected models change, the table does not: this is what the grouping projection buys.
		CSharpCompilation compilation = roslyn.CreateCompilation(BindingSources.Functions, BindingSources.Globals);
		GeneratorRun first = RoslynFixture.Run(compilation);

		GeneratorRun second = GeneratorRun.Execute(
			first.Driver,
			compilation.AddSyntaxTrees(RoslynFixture.Parse(
				"namespace Demo; public static partial class Functions { [CheatEngine.SDK.Annotations.Lua.LuaFunction(\"bad\")] public static int Bad(object o) => 0; }",
				"Bad.cs")));

		Assert.Equal([IncrementalStepRunReason.Modified],
			StepAssert.Reasons(second.Result, LuaBindingsTrackingNames.CollectedLuaFunctions));
		Assert.Equal([IncrementalStepRunReason.Unchanged],
			StepAssert.Reasons(second.Result, LuaBindingsTrackingNames.LuaFunctionTables));
		Assert.All(StepAssert.Reasons(second.Result, LuaBindingsTrackingNames.LuaFunctionOutput),
			static reason =>
				Assert.True(reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged));
		Assert.All(StepAssert.OutputReasons(second.Result),
			static reason => Assert.Equal(IncrementalStepRunReason.Cached, reason));
		Assert.Equal(first.GeneratedText(ExpectedFiles.FunctionsHintName),
			second.GeneratedText(ExpectedFiles.FunctionsHintName));
	}

	[Fact]
	public void Pipeline_second_binding_type_added_emits_its_file_next_to_the_first()
	{
		CSharpCompilation compilation = roslyn.CreateCompilation(BindingSources.Functions, BindingSources.Globals);
		GeneratorRun first = RoslynFixture.Run(compilation);

		GeneratorRun second = GeneratorRun.Execute(
			first.Driver,
			compilation.AddSyntaxTrees(RoslynFixture.Parse(
				"namespace Demo; public static partial class More { [CheatEngine.SDK.Annotations.Lua.LuaFunction(\"more\")] public static int M(int a) => a; }",
				"More.cs")));

		Assert.Equal(3, second.GeneratedSources.Length);
		Assert.Equal(first.GeneratedText(ExpectedFiles.FunctionsHintName),
			second.GeneratedText(ExpectedFiles.FunctionsHintName));
		Assert.Contains("__LuaThunk_more", second.GeneratedText("Demo.More.LuaFunctions.g.cs"),
			StringComparison.Ordinal);
		AssertUntouched(second.Result, LuaBindingsTrackingNames.LuaGlobalOutput);
	}

	[Fact]
	public void Pipeline_editing_one_of_two_containing_types_leaves_the_others_output_cached()
	{
		// Other tests either exercise one [LuaFunction] type at a time, or add the second type between runs (which
		// Roslyn's SelectMany reports as a new output for every table, by design). This one covers two [LuaFunction]
		// types already stable in the compilation, then an edit to only one of them.
		const string Source =
			"using CheatEngine.SDK.Annotations.Lua;\nnamespace Demo;\npublic static partial class Alpha { [LuaFunction(\"a\")] public static int A(int x) => x; }\npublic static partial class Beta { [LuaFunction(\"b\")] public static int B(int x) => x; }\n";
		CSharpCompilation compilation = roslyn.CreateCompilation(Source);
		GeneratorRun first = RoslynFixture.Run(compilation);
		Assert.Equal(2, first.GeneratedSources.Length);

		SyntaxTree original = compilation.SyntaxTrees.First();
		string edited = Source.Replace("[LuaFunction(\"a\")]", "[LuaFunction(\"aa\")]", StringComparison.Ordinal);
		GeneratorRun second = GeneratorRun.Execute(first.Driver,
			compilation.ReplaceSyntaxTree(original, RoslynFixture.Parse(edited, original.FilePath)));

		Assert.Contains("__LuaThunk_aa", second.GeneratedText("Demo.Alpha.LuaFunctions.g.cs"),
			StringComparison.Ordinal);
		Assert.Equal(first.GeneratedText("Demo.Beta.LuaFunctions.g.cs"),
			second.GeneratedText("Demo.Beta.LuaFunctions.g.cs"), StringComparer.Ordinal);

		ImmutableArray<IncrementalStepRunReason> outputReasons =
			StepAssert.Reasons(second.Result, LuaBindingsTrackingNames.LuaFunctionOutput);
		Assert.Equal(2, outputReasons.Length);
		Assert.Contains(IncrementalStepRunReason.Modified, outputReasons);
		Assert.Contains(outputReasons,
			static reason => reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged);
	}

	[Fact]
	public void Pipeline_unsafe_switched_off_removes_only_function_output()
	{
		CSharpCompilation compilation = roslyn.CreateCompilation(BindingSources.Functions, BindingSources.Globals);
		GeneratorRun first = RoslynFixture.Run(compilation);
		Assert.Equal(2, first.GeneratedSources.Length);

		GeneratorRun second = GeneratorRun.Execute(first.Driver,
			compilation.WithOptions(RoslynEnvironment.SafeCompilationOptions));

		Assert.Single(second.GeneratedSources);
		Assert.Equal(ExpectedFiles.GlobalsHintName, second.HintNames[0]);
		Assert.Equal([IncrementalStepRunReason.Modified],
			StepAssert.Reasons(second.Result, LuaBindingsTrackingNames.Facts));
		AssertUntouched(second.Result, LuaBindingsTrackingNames.LuaFunctionTables,
			LuaBindingsTrackingNames.LuaGlobalTables);
	}

	[Fact]
	public void Pipeline_optional_signature_edited_reruns_the_global_output_only()
	{
		CSharpCompilation compilation =
			roslyn.CreateCompilation(BindingSources.Functions, OptionalBindingSources.GlobalSuite);
		GeneratorRun first = RoslynFixture.Run(compilation);

		SyntaxTree original = compilation.SyntaxTrees.Last();
		string edited = OptionalBindingSources.GlobalSuite.Replace(
			"Kinds(long first, LuaOptional<long> second)", "Kinds(long first, LuaOptional<int> second)",
			StringComparison.Ordinal);
		Assert.NotEqual(OptionalBindingSources.GlobalSuite, edited, StringComparer.Ordinal);
		GeneratorRun second = GeneratorRun.Execute(first.Driver,
			compilation.ReplaceSyntaxTree(original, RoslynFixture.Parse(edited, original.FilePath)));

		Assert.Contains(IncrementalStepRunReason.Modified,
			StepAssert.Reasons(second.Result, LuaBindingsTrackingNames.LuaGlobal));
		Assert.Equal([IncrementalStepRunReason.Modified],
			StepAssert.Reasons(second.Result, LuaBindingsTrackingNames.LuaGlobalOutput));
		AssertUntouched(second.Result, LuaBindingsTrackingNames.LuaFunction, LuaBindingsTrackingNames.LuaFunctionTables,
			LuaBindingsTrackingNames.LuaFunctionOutput);
		Assert.Contains("Kinds(long first, global::CheatEngine.SDK.Lua.Marshalling.LuaOptional<int> second)",
			second.GeneratedText("Demo.Optionals.LuaGlobals.g.cs"), StringComparison.Ordinal);
	}

	[Fact]
	public void Pipeline_reordering_optional_bindings_keeps_hint_names_and_member_names()
	{
		string[] members = OptionalMembers();
		string forward = OptionalType(members);
		string reversed = OptionalType([.. members.Reverse()]);
		Assert.NotEqual(forward, reversed, StringComparer.Ordinal);

		GeneratorRun first = roslyn.Run(forward);
		GeneratorRun second = roslyn.Run(reversed);

		Assert.Equal(first.HintNames, second.HintNames);
		Assert.Equal(first.SingleGeneratedText, second.SingleGeneratedText);
		first.AssertCompilesClean();
	}

	[Fact]
	public void Pipeline_step_values_hold_no_roslyn_objects()
	{
		GeneratorRun run = roslyn.Run(
			BindingSources.FunctionSuite,
			BindingSources.GlobalSuite,
			OptionalBindingSources.GlobalSuite,
			OptionalBindingSources.FunctionSuite,
			"namespace Demo; public static partial class Broken { [CheatEngine.SDK.Annotations.Lua.LuaFunction(\"bad\")] public static int Bad(object o) => 0; [CheatEngine.SDK.Annotations.Lua.LuaGlobal(\"bad\")] public static partial bool TryBad(out object o); }",
			ObjectBindings);

		int visited = 0;
		foreach (string stepName in run.Result.TrackedSteps.Keys
			         .Where(TrackingNames.IsCheatEngineSdkStep)
			         .Order(StringComparer.Ordinal))
		{
			Assert.True(
				run.Result.TrackedSteps.TryGetValue(stepName, out ImmutableArray<IncrementalGeneratorRunStep> steps),
				$"Tracked step '{stepName}' was not present.");

			foreach (IncrementalGeneratorRunStep step in steps)
			{
				foreach ((object value, IncrementalStepRunReason _) in step.Outputs)
				{
					visited += ModelGraph.AssertFreeOfRoslynObjects(value, stepName);
				}
			}
		}

		Assert.True(visited > 0, "No model object was visited: the assertion would be vacuous.");
	}

	// The attributed members of OptionalBindingSources.GlobalSuite, one declaration each.
	private static string[] OptionalMembers()
	{
		string body = OptionalBindingSources.GlobalSuite.ReplaceLineEndings("\n");
		int open = body.IndexOf("{\n", body.IndexOf("class Optionals", StringComparison.Ordinal),
			StringComparison.Ordinal);
		int close = body.LastIndexOf('}');
		return
		[
			.. body[(open + 2)..close]
				.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
		];
	}

	private static string OptionalType(string[] members)
	{
		string suite = OptionalBindingSources.GlobalSuite.ReplaceLineEndings("\n");
		string header = suite[..suite.IndexOf("public static partial class Optionals", StringComparison.Ordinal)];
		return header + "public static partial class Optionals\n{\n" + string.Join("\n\n", members) + "\n}\n";
	}

	private static void AssertUntouched(GeneratorRunResult result, params string[] stepNames)
	{
		foreach (string stepName in stepNames)
		{
			Assert.All(
				StepAssert.Reasons(result, stepName),
				reason => Assert.True(
					reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
					$"Step '{stepName}' was recomputed: {reason}."));
		}
	}
}
