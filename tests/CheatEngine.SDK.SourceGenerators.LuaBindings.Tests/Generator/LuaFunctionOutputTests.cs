using System.Text;

using CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Infrastructure;

using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Generator;

/// <summary>
///     The <c>[LuaFunction]</c> pipeline on its nominal input: exact text, hint name, clean compilation against the
///     real SDK.
/// </summary>
public sealed class LuaFunctionOutputTests(RoslynFixture roslyn) : IClassFixture<RoslynFixture>
{
	[Fact]
	public void Generator_two_functions_emit_exact_file()
	{
		GeneratorRun run = roslyn.Run(BindingSources.Functions);

		GeneratedSourceResult generated = Assert.Single(run.GeneratedSources);
		Assert.Equal(ExpectedFiles.FunctionsHintName, generated.HintName);
		Assert.Equal(ExpectedFiles.Functions(), generated.SourceText.ToString());
	}

	[Fact]
	public void Generator_two_functions_output_compiles_without_errors_or_warnings()
	{
		GeneratorRun run = roslyn.Run(BindingSources.Functions);

		Assert.Single(run.GeneratedSources);
		run.AssertCompilesClean();
	}

	[Fact]
	public void Generator_output_is_utf8_with_lf_line_endings()
	{
		GeneratorRun run = roslyn.Run(BindingSources.Functions);

		GeneratedSourceResult generated = Assert.Single(run.GeneratedSources);
		Assert.Equal(Encoding.UTF8, generated.SourceText.Encoding);
		Assert.DoesNotContain('\r', generated.SourceText.ToString());
	}

	[Fact]
	public void Generator_function_suite_compiles_clean_and_declares_one_thunk_per_function()
	{
		GeneratorRun run = roslyn.Run(BindingSources.FunctionSuite);

		run.AssertCompilesClean();
		string text = run.SingleGeneratedText;
		foreach (string name in new[]
				 {
					 "add", "greet", "ping", "isint", "boom", "echo", "half", "negate", "step", "small", "scale",
					 "maybe"
				 })
		{
			Assert.Contains("private static int __LuaThunk_" + name + "(nint __handle)", text,
				StringComparison.Ordinal);
			Assert.Contains("state.TrySetGlobal(\"" + name + "\"u8);", text, StringComparison.Ordinal);
		}

		// The state parameter is passed first and not counted as a Lua argument.
		Assert.Contains("if (__L.Top != 1)", text, StringComparison.Ordinal);
		Assert.Contains("global::Demo.Suite.IsInteger(__L, __arg0)", text, StringComparison.Ordinal);

		// void: no result pushed; ReadOnlySpan<byte>: read and pushed through the UTF-8 marshaller.
		Assert.Contains("global::Demo.Suite.Ping();\n                return 0;", text, StringComparison.Ordinal);
		Assert.Contains(
			"global::CheatEngine.SDK.Lua.Marshalling.Utf8Marshaller.TryRead(__L, 1, out global::System.ReadOnlySpan<byte> __arg0)",
			text,
			StringComparison.Ordinal);
		Assert.Contains("global::CheatEngine.SDK.Lua.Marshalling.Utf8Marshaller.Push(__L, __result);", text,
			StringComparison.Ordinal);
		Assert.Contains("global::CheatEngine.SDK.Lua.Marshalling.AddressMarshaller.TryRead(__L, 1, out nuint __arg0)",
			text,
			StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_registration_is_ordered_by_lua_name()
	{
		GeneratorRun run = roslyn.Run(BindingSources.FunctionSuite);

		string text = run.SingleGeneratedText;
		int add = text.IndexOf("TrySetGlobal(\"add\"u8)", StringComparison.Ordinal);
		int boom = text.IndexOf("TrySetGlobal(\"boom\"u8)", StringComparison.Ordinal);
		int step = text.IndexOf("TrySetGlobal(\"step\"u8)", StringComparison.Ordinal);
		Assert.True(add >= 0 && add < boom && boom < step, "Registrations are not sorted by Lua name.");
	}

	[Fact]
	public void Generator_obsolete_target_compiles_clean()
	{
		const string Source =
			"using System;\nusing CheatEngine.SDK.Annotations.Lua;\nnamespace Demo;\npublic static partial class Holder\n{\n    [Obsolete] [LuaFunction(\"f\")] public static int F(int a) => a;\n}\n";

		GeneratorRun run = roslyn.Run(Source);

		Assert.Single(run.GeneratedSources);
		run.AssertCompilesClean();
		Assert.Contains("#pragma warning disable CS0612, CS0618", run.SingleGeneratedText, StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_experimental_target_compiles_clean()
	{
		const string Source =
			"using System.Diagnostics.CodeAnalysis;\nusing CheatEngine.SDK.Annotations.Lua;\nnamespace Demo;\npublic static partial class Holder\n{\n    [Experimental(\"DEMO001\")] [LuaFunction(\"f\")] public static int F(int a) => a;\n}\n";

		GeneratorRun run = roslyn.Run(Source);

		Assert.Single(run.GeneratedSources);
		run.AssertCompilesClean();
		Assert.Contains("#pragma warning disable DEMO001", run.SingleGeneratedText, StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_experimental_containing_type_compiles_clean()
	{
		const string Source =
			"using System.Diagnostics.CodeAnalysis;\nusing CheatEngine.SDK.Annotations.Lua;\nnamespace Demo;\n[Experimental(\"DEMO002\")]\npublic static partial class Holder\n{\n    [LuaFunction(\"f\")] public static int F(int a) => a;\n}\n";

		GeneratorRun run = roslyn.Run(Source);

		Assert.Single(run.GeneratedSources);
		run.AssertCompilesClean();
		Assert.Contains("#pragma warning disable DEMO002", run.SingleGeneratedText, StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_target_parameters_named_like_generated_thunk_locals_do_not_collide()
	{
		// Unlike a [LuaGlobal] implementing declaration (PartialMethodSignatureTests), a thunk never spells the
		// target's own parameter names: it calls positionally (__arg0, __arg1, ...), so the target is free to name
		// its parameters anything, including names that look like the thunk's own locals.
		const string Source =
			"using CheatEngine.SDK.Annotations.Lua;\nusing CheatEngine.SDK.Lua.State;\nnamespace Demo;\npublic static partial class Holder\n{\n    [LuaFunction(\"f\")] public static int F(LuaState L, int top, int result, int __arg0, int __handle) => L.IsInteger(1) ? top + result + __arg0 + __handle : 0;\n}\n";

		GeneratorRun run = roslyn.Run(Source);

		Assert.Single(run.GeneratedSources);
		run.AssertCompilesClean();
		Assert.Contains("global::Demo.Holder.F(__L, __arg0, __arg1, __arg2, __arg3)", run.SingleGeneratedText,
			StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_optional_thunk_accepts_the_declared_argument_range()
	{
		GeneratorRun run = roslyn.Run(OptionalBindingSources.FunctionSuite);

		run.AssertCompilesClean();
		string text = run.SingleGeneratedText;
		Assert.Contains(
			"if (__L.Top < 1 || __L.Top > 3)\n                {\n                    return global::CheatEngine.SDK.Lua.Callbacks.LuaThunk.Fail(__L, \"wrong number of arguments to 'optdescribe' (1 to 3 expected)\"u8);",
			text, StringComparison.Ordinal);
		Assert.Contains(
			"if (!global::CheatEngine.SDK.Lua.CompilerServices.LuaCallSupport.TryReadOptional<long, global::CheatEngine.SDK.Lua.Marshalling.Int64Marshaller>(__L, 2, out global::CheatEngine.SDK.Lua.Marshalling.LuaOptional<long> __arg1))",
			text, StringComparison.Ordinal);
		Assert.Contains("return global::CheatEngine.SDK.Lua.Callbacks.LuaThunk.FailBadArgument(__L, 3, \"string\"u8);",
			text, StringComparison.Ordinal);
		Assert.Contains("if (__L.Top < 0 || __L.Top > 1)", text, StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_any_input_reports_no_diagnostics()
	{
		GeneratorRun valid = roslyn.Run(BindingSources.Functions);
		GeneratorRun invalid =
			roslyn.Run(BindingSources.Functions.Replace("public static long Add", "public long Add",
				StringComparison.Ordinal));

		Assert.Empty(valid.GeneratorDiagnostics);
		Assert.Empty(valid.Result.Diagnostics);
		Assert.Empty(invalid.GeneratorDiagnostics);
		Assert.Empty(invalid.Result.Diagnostics);
	}
}
