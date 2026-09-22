using CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Infrastructure;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Generator;

/// <summary>
///     The <c>[LuaGlobal]</c> pipeline: exact text for the worked example (<c>BindingSources.Globals</c>), every
///     supported shape compiling clean.
/// </summary>
public sealed class LuaGlobalOutputTests(RoslynFixture roslyn) : IClassFixture<RoslynFixture>
{
	[Fact]
	public void Generator_try_and_throwing_forms_emit_exact_file_with_one_shared_cache()
	{
		GeneratorRun run = roslyn.Run(BindingSources.Globals);

		GeneratedSourceResult generated = Assert.Single(run.GeneratedSources);
		Assert.Equal(ExpectedFiles.GlobalsHintName, generated.HintName);
		Assert.Equal(ExpectedFiles.Globals(), generated.SourceText.ToString());
	}

	[Fact]
	public void Generator_try_and_throwing_forms_compile_without_errors_or_warnings()
	{
		GeneratorRun run = roslyn.Run(BindingSources.Globals);

		Assert.Single(run.GeneratedSources);
		run.AssertCompilesClean();
	}

	[Fact]
	public void Generator_global_suite_compiles_clean()
	{
		GeneratorRun run = roslyn.Run(BindingSources.GlobalSuite);

		Assert.Single(run.GeneratedSources);
		run.AssertCompilesClean();
	}

	[Fact]
	public void Generator_outcome_form_preserves_resolution_call_and_result_categories()
	{
		GeneratorRun run = roslyn.Run(BindingSources.GlobalSuite);

		string body = Section(run.SingleGeneratedText,
			"public static partial global::CheatEngine.SDK.Lua.Calls.LuaOperationStatus TryReadInt32Detailed(nuint address, out int value)",
			"\n        }\n");
		Assert.Contains("LuaGlobalFunctions.TryPushWithOutcome", body, StringComparison.Ordinal);
		Assert.Contains("__resolution.ToOperationStatus()", body, StringComparison.Ordinal);
		Assert.Contains("LuaOperationStatus.LuaFailure(__status)", body, StringComparison.Ordinal);
		Assert.Contains(
			"LuaOperationStatus.NilResult : global::CheatEngine.SDK.Lua.Calls.LuaOperationStatus.InvalidResult",
			body, StringComparison.Ordinal);
		Assert.Contains("return global::CheatEngine.SDK.Lua.Calls.LuaOperationStatus.Success;", body,
			StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_copy_out_result_copies_before_restoring_the_stack()
	{
		GeneratorRun run = roslyn.Run(BindingSources.GlobalSuite);

		string text = run.SingleGeneratedText;
		Assert.Contains(
			"public static partial bool TryReadString(nuint address, int maxLength, global::System.Span<byte> destination, out int written)",
			text,
			StringComparison.Ordinal);
		Assert.Contains("bool __ok = __L.TryCopyUtf8(-1, destination, out written);\n                return __ok;",
			text,
			StringComparison.Ordinal);
		Assert.Contains("finally\n            {\n                __L.SetTop(__top);", text, StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_string_result_reads_through_the_string_marshaller()
	{
		GeneratorRun run = roslyn.Run(BindingSources.GlobalSuite);

		string text = run.SingleGeneratedText;
		Assert.Contains("public static partial bool TryReadString(nuint address, int maxLength, out string value)",
			text, StringComparison.Ordinal);
		Assert.Contains(
			"bool __ok = global::CheatEngine.SDK.Lua.Marshalling.StringMarshaller.TryRead(__L, -1, out value);", text,
			StringComparison.Ordinal);
		Assert.Contains("public static partial string ReadString(nuint address, int maxLength)", text,
			StringComparison.Ordinal);
		Assert.Contains(
			"global::CheatEngine.SDK.Lua.CompilerServices.LuaCallSupport.ThrowUnexpectedResult(__L, __top, -1, \"readString\", \"a string\");",
			text, StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_void_throwing_form_keeps_no_result_and_restores_the_stack_in_finally()
	{
		GeneratorRun run = roslyn.Run(BindingSources.GlobalSuite);

		string text = run.SingleGeneratedText;
		string body = Section(text, "public static partial void Beep()", "\n        }\n");
		Assert.Contains("global::CheatEngine.SDK.Lua.Calls.LuaStatus __status = __L.TryCall(0, 0);", body,
			StringComparison.Ordinal);
		Assert.Contains("finally\n            {\n                __L.SetTop(__top);", body, StringComparison.Ordinal);
		Assert.DoesNotContain("return", body, StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_bool_return_without_results_is_the_throwing_form()
	{
		GeneratorRun run = roslyn.Run(BindingSources.GlobalSuite);

		string body = Section(run.SingleGeneratedText, "public static partial bool IsKeyPressed(int key)",
			"\n        }\n");
		Assert.Contains("global::CheatEngine.SDK.Lua.Marshalling.BooleanMarshaller.TryRead(__L, -1, out bool __result)",
			body,
			StringComparison.Ordinal);
		Assert.Contains("ThrowUnexpectedResult(__L, __top, -1, \"isKeyPressed\", \"a boolean\")", body,
			StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_two_results_are_read_from_the_bottom_up_and_default_each_other_on_failure()
	{
		GeneratorRun run = roslyn.Run(BindingSources.GlobalSuite);

		string body = Section(run.SingleGeneratedText,
			"public static partial bool TryDivide(long dividend, long divisor, out long quotient, out long remainder)",
			"\n        }\n");
		Assert.Contains("__L.TryCall(2, 2)", body, StringComparison.Ordinal);
		Assert.Contains(
			"remainder = default;\n                    return global::CheatEngine.SDK.Lua.CompilerServices.LuaCallSupport.Fail(__L, __top, out quotient);",
			body, StringComparison.Ordinal);
		Assert.Contains("if (!global::CheatEngine.SDK.Lua.Marshalling.Int64Marshaller.TryRead(__L, -2, out quotient))",
			body,
			StringComparison.Ordinal);
		Assert.Contains("if (!global::CheatEngine.SDK.Lua.Marshalling.Int64Marshaller.TryRead(__L, -1, out remainder))",
			body,
			StringComparison.Ordinal);
		Assert.Contains(
			"quotient = default;\n                    return global::CheatEngine.SDK.Lua.CompilerServices.LuaCallSupport.Fail(__L, __top, out remainder);",
			body, StringComparison.Ordinal);
		Assert.Contains(
			"return true;\n            }\n            catch (global::CheatEngine.SDK.Lua.Calls.LuaException)", body,
			StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_string_result_among_several_is_defaulted_with_the_null_forgiving_operator()
	{
		GeneratorRun run = roslyn.Run(BindingSources.GlobalSuite);

		string body = Section(run.SingleGeneratedText,
			"public static partial bool TryDescribe(double value, bool flag, out string text, out double doubled)",
			"\n        }\n");
		Assert.Contains("text = default!;", body, StringComparison.Ordinal);
		Assert.Contains("doubled = default;", body, StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_leading_state_parameter_acquires_an_atomic_operation_lease()
	{
		GeneratorRun run = roslyn.Run(BindingSources.GlobalSuite);

		string text = run.SingleGeneratedText;
		string throwing = Section(text,
			"public static partial long AddOn(global::CheatEngine.SDK.Lua.State.LuaState state, long a, long b)",
			"\n        }\n");
		Assert.Contains(
			"using global::CheatEngine.SDK.Lua.Runtime.LuaRuntimeOperation __operation = global::CheatEngine.SDK.Lua.Runtime.LuaRuntime.AcquireOperation(state);",
			throwing,
			StringComparison.Ordinal);
		Assert.Contains("global::CheatEngine.SDK.Lua.State.LuaState __L = __operation.State;", throwing,
			StringComparison.Ordinal);
		Assert.DoesNotContain("AcquireState", throwing, StringComparison.Ordinal);
		string tryForm = Section(text,
			"public static partial bool TryAddOn(global::CheatEngine.SDK.Lua.State.LuaState state, long a, long b, out long sum)",
			"\n        }\n");
		Assert.Contains(
			"using global::CheatEngine.SDK.Lua.Runtime.LuaRuntimeOperation __operation = global::CheatEngine.SDK.Lua.Runtime.LuaRuntime.AcquireOperation(state);",
			tryForm,
			StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_nullable_string_annotations_are_repeated_in_the_signature()
	{
		GeneratorRun run = roslyn.Run(BindingSources.GlobalSuite);

		string text = run.SingleGeneratedText;
		Assert.Contains("public static partial string Upper(global::System.ReadOnlySpan<byte> text)", text,
			StringComparison.Ordinal);
		Assert.Contains("public static partial string? UpperOrNull(string? text)", text, StringComparison.Ordinal);
		Assert.Contains("global::CheatEngine.SDK.Lua.Marshalling.Utf8Marshaller.Push(__L, text);", text,
			StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_wide_signature_checks_the_stack_first_and_compiles_clean()
	{
		GeneratorRun run = roslyn.Run(BindingSources.ManyArguments);

		run.AssertCompilesClean();
		string text = run.SingleGeneratedText;
		Assert.Contains(
			"if (!__L.TryEnsureStack(17))\n                {\n                    return global::CheatEngine.SDK.Lua.CompilerServices.LuaCallSupport.Fail(__L, __top, out sum);",
			text, StringComparison.Ordinal);
		Assert.Contains(
			"if (!__L.TryEnsureStack(17))\n                {\n                    throw new global::CheatEngine.SDK.Lua.Calls.LuaException(\"The Lua stack could not grow by 17 slots to call 'sum16'.\");",
			text, StringComparison.Ordinal);
		Assert.Contains("__L.TryCall(16, 1)", text, StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_narrow_signature_does_not_check_the_stack()
	{
		GeneratorRun run = roslyn.Run(BindingSources.Globals);

		Assert.DoesNotContain("TryEnsureStack", run.SingleGeneratedText, StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_unannotated_out_string_result_gets_exactly_cs8601_the_documented_exception()
	{
		// Every supported shape compiles clean, with one known exception. An 'out string' Try-form result without
		// '[MaybeNullWhen(false)]' or 'string?' is valid input (LuaGlobalShape accepts it; StringMarshaller.TryRead's
		// own out parameter carries the annotation, the declaration does not), and the generated body assigns it on
		// the failure path exactly like every other result - so nullable analysis reports CS8601 in the generated
		// file. The fix is on the declaration, not in this generator.
		const string Source =
			"using CheatEngine.SDK.Annotations.Lua;\nnamespace Demo; public static partial class Holder { [LuaGlobal(\"g\")] public static partial bool TryG(nuint a, out string value); }";

		GeneratorRun run = roslyn.Run(Source);

		Assert.Single(run.GeneratedSources);
		Assert.Empty(run.GeneratorDiagnostics);
		Diagnostic[] problems =
		[
			.. run.OutputCompilation
				.GetDiagnostics(TestContext.Current.CancellationToken)
				.Where(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning
											&& !(string.Equals(diagnostic.Id, "CS1591", StringComparison.Ordinal)
												 && diagnostic.Location.SourceTree is { FilePath: string path } &&
												 !path.EndsWith(".g.cs", StringComparison.Ordinal)))
		];
		Diagnostic problem = Assert.Single(problems);
		Assert.Equal("CS8601", problem.Id);
		Assert.Contains("LuaGlobals.g.cs", problem.Location.SourceTree?.FilePath, StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_compiles_clean_for_a_consumer_with_nullable_disabled()
	{
		// The generated file always opens with its own '#nullable enable' (GeneratedCodeText.WriteFileHeader), so a
		// non-nullable shape compiles clean regardless of the consumer project's own <Nullable> setting: nothing
		// here depends on the project-wide nullable context. (A defining declaration that writes 'string?' in a file
		// without its own '#nullable enable' gets CS8632 either way - that is plain C#, unrelated to this generator,
		// and not exercised here.)
		CSharpCompilationOptions options =
			RoslynEnvironment.CompilationOptions.WithNullableContextOptions(NullableContextOptions.Disable);
		GeneratorRun run = RoslynFixture.Run(roslyn.CreateCompilation(options, BindingSources.Globals));

		Assert.Single(run.GeneratedSources);
		run.AssertCompilesClean();
	}

	// The text from the first occurrence of 'start' to the first 'end' after it (the closing brace of the method).
	private static string Section(string text, string start, string end)
	{
		int from = text.IndexOf(start, StringComparison.Ordinal);
		Assert.True(from >= 0, "Not found in the generated text: " + start);
		int to = text.IndexOf(end, from, StringComparison.Ordinal);
		Assert.True(to >= 0, "No end marker after: " + start);
		return text[from..to];
	}
}
