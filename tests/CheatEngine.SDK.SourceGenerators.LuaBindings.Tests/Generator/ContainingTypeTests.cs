using CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Infrastructure;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Generator;

/// <summary>
///     Where the bindings live: nested types, the global namespace, structs and records, keyword and non-ASCII
///     identifiers, several types, both kinds in one type.
/// </summary>
public sealed class ContainingTypeTests(RoslynFixture roslyn) : IClassFixture<RoslynFixture>
{
	private const string Usings = "using CheatEngine.SDK.Annotations.Lua;\n";

	[Fact]
	public void Generator_nested_types_reopen_every_level()
	{
		const string Source = Usings + """
		                               namespace Demo.Inner
		                               {
		                                   public partial class Outer
		                                   {
		                                       internal static partial class Bindings
		                                       {
		                                           [LuaFunction("f")] private static int F(int a) => a;
		                                           [LuaGlobal("g")] internal static partial bool TryG(nuint a, out int v);
		                                       }
		                                   }
		                               }
		                               """;

		GeneratorRun run = roslyn.Run(Source);

		run.AssertCompilesClean();
		Assert.Equal(["Demo.Inner.Outer.Bindings.LuaFunctions.g.cs", "Demo.Inner.Outer.Bindings.LuaGlobals.g.cs"],
			run.HintNames.Order(StringComparer.Ordinal), StringComparer.Ordinal);
		string functions = run.GeneratedText("Demo.Inner.Outer.Bindings.LuaFunctions.g.cs");
		Assert.Contains(
			"namespace Demo.Inner\n{\n    partial class Outer\n    {\n        partial class Bindings\n        {\n",
			functions, StringComparison.Ordinal);
		Assert.Contains("global::Demo.Inner.Outer.Bindings.F(__arg0)", functions, StringComparison.Ordinal);
		Assert.Contains("internal static partial bool TryG(nuint a, out int v)",
			run.GeneratedText("Demo.Inner.Outer.Bindings.LuaGlobals.g.cs"), StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_global_namespace_has_no_namespace_block()
	{
		GeneratorRun run = roslyn.Run(Usings +
		                              "public static partial class Top { [LuaFunction(\"f\")] public static int F(int a) => a; }");

		run.AssertCompilesClean();
		string text = run.GeneratedText("Top.LuaFunctions.g.cs");
		Assert.DoesNotContain("namespace", text, StringComparison.Ordinal);
		Assert.Contains("\npartial class Top\n{\n", text, StringComparison.Ordinal);
		Assert.Contains("global::Top.F(__arg0)", text, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("public partial struct", "partial struct")]
	[InlineData("public readonly partial struct", "readonly partial struct")]
	[InlineData("public partial record", "partial record")]
	[InlineData("public partial record class", "partial record")]
	[InlineData("public partial record struct", "partial record struct")]
	[InlineData("public sealed partial class", "partial class")]
	[InlineData("public static partial class", "partial class")]
	[InlineData("internal static partial class", "partial class")]
	public void Generator_type_kinds_are_reopened_with_their_keyword(string declaration, string expectedPart)
	{
		GeneratorRun run = roslyn.Run(Usings + "namespace Demo; " + declaration +
		                              " Holder { [LuaFunction(\"f\")] public static int F(int a) => a; [LuaGlobal(\"g\")] public static partial bool TryG(nuint a, out int v); }");

		run.AssertCompilesClean();
		Assert.Contains("\n    " + expectedPart + " Holder\n    {\n",
			run.GeneratedText("Demo.Holder.LuaFunctions.g.cs"), StringComparison.Ordinal);
		Assert.Contains("\n    " + expectedPart + " Holder\n    {\n", run.GeneratedText("Demo.Holder.LuaGlobals.g.cs"),
			StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_keyword_identifiers_are_escaped()
	{
		const string Source = Usings + """
		                               namespace @class.Demo
		                               {
		                                   public static partial class @event
		                                   {
		                                       [LuaFunction("f")] public static int @string(int @int) => @int;
		                                       [LuaGlobal("g")] public static partial bool @object(nuint @base, out int @this);
		                                   }
		                               }
		                               """;

		GeneratorRun run = roslyn.Run(Source);

		run.AssertCompilesClean();
		string functions = run.GeneratedText("class.Demo.event.LuaFunctions.g.cs");
		Assert.Contains("namespace @class.Demo\n", functions, StringComparison.Ordinal);
		Assert.Contains("partial class @event\n", functions, StringComparison.Ordinal);
		Assert.Contains("global::@class.Demo.@event.@string(__arg0)", functions, StringComparison.Ordinal);
		Assert.Contains("public static partial bool @object(nuint @base, out int @this)",
			run.GeneratedText("class.Demo.event.LuaGlobals.g.cs"), StringComparison.Ordinal);
		Assert.Contains("LuaCallSupport.Fail(__L, __top, out @this)",
			run.GeneratedText("class.Demo.event.LuaGlobals.g.cs"), StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_types_differing_only_by_case_get_distinct_hint_names()
	{
		// Roslyn's AdditionalSourcesCollection compares hint names case-insensitively; HintNames.ForType alone
		// cannot tell "DemoType" and "demoType" apart (neither has a replaced character), so without disambiguation
		// AddSource throws ArgumentException on the second one and the whole generation pass crashes.
		const string Source = Usings +
		                      "namespace Demo; public static partial class DemoType { [LuaFunction(\"f1\")] public static int F(int a) => a; } public static partial class demoType { [LuaFunction(\"f2\")] public static int F(int a) => a; }";

		GeneratorRun run = roslyn.Run(Source);

		run.AssertCompilesClean();
		Assert.Equal(2, run.GeneratedSources.Length);
		Assert.Contains("Demo.DemoType.LuaFunctions.g.cs", run.HintNames, StringComparer.Ordinal);
		Assert.DoesNotContain("Demo.demoType.LuaFunctions.g.cs", run.HintNames, StringComparer.Ordinal);
		Assert.NotEqual(run.HintNames[0], run.HintNames[1], StringComparer.OrdinalIgnoreCase);
	}

	[Fact]
	public void Generator_non_ascii_type_name_gets_a_hashed_hint_name_and_an_unescaped_declaration()
	{
		GeneratorRun run = roslyn.Run(Usings +
		                              "namespace Demo; public static partial class Caf\u00E9 { [LuaFunction(\"f\")] public static int F(int a) => a; }");

		run.AssertCompilesClean();
		string hintName = Assert.Single(run.HintNames);
		Assert.StartsWith("Demo.Caf__", hintName, StringComparison.Ordinal);
		Assert.EndsWith(".LuaFunctions.g.cs", hintName, StringComparison.Ordinal);
		Assert.Contains("partial class Caf\u00E9\n", run.SingleGeneratedText, StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_several_types_get_one_file_each_in_name_order()
	{
		const string Source = Usings + """
		                               namespace Demo;
		                               public static partial class Zeta { [LuaFunction("z")] public static int Z(int a) => a; }
		                               public static partial class Alpha { [LuaFunction("a")] public static int A(int a) => a; }
		                               public static partial class Mid { [LuaGlobal("m")] public static partial bool TryM(nuint a, out int v); }
		                               """;

		GeneratorRun run = roslyn.Run(Source);

		run.AssertCompilesClean();
		Assert.Equal(["Demo.Alpha.LuaFunctions.g.cs", "Demo.Zeta.LuaFunctions.g.cs", "Demo.Mid.LuaGlobals.g.cs"],
			run.HintNames, StringComparer.Ordinal);
	}

	[Fact]
	public void Generator_both_kinds_in_one_type_give_two_files_that_compile_together()
	{
		GeneratorRun run = roslyn.Run(BindingSources.Mixed);

		run.AssertCompilesClean();
		Assert.Equal(["Demo.Mixed.LuaFunctions.g.cs", "Demo.Mixed.LuaGlobals.g.cs"],
			run.HintNames.Order(StringComparer.Ordinal), StringComparer.Ordinal);
	}

	[Fact]
	public void Generator_type_split_across_files_gets_one_file()
	{
		GeneratorRun run = roslyn.Run(
			Usings +
			"namespace Demo; public static partial class Split { [LuaFunction(\"a\")] public static int A(int a) => a; }",
			Usings +
			"namespace Demo; public static partial class Split { [LuaFunction(\"b\")] public static int B(int a) => a; }");

		run.AssertCompilesClean();
		string text = run.SingleGeneratedText;
		Assert.Contains("__LuaThunk_a", text, StringComparison.Ordinal);
		Assert.Contains("__LuaThunk_b", text, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("public static partial", "public static partial")]
	[InlineData("internal static partial", "internal static partial")]
	[InlineData("private static partial", "private static partial")]
	[InlineData("protected internal static partial", "protected internal static partial")]
	[InlineData("private protected static partial", "private protected static partial")]
	[InlineData("static public partial", "public static partial")]
	[InlineData("public static unsafe partial", "public static unsafe partial")]
	public void Generator_repeats_the_defining_declarations_modifiers(string declared, string emitted)
	{
		GeneratorRun run = roslyn.Run(Usings + "namespace Demo; public partial class Holder { [LuaGlobal(\"g\")] " +
		                              declared +
		                              " bool TryG(nuint a, out int v); }");

		run.AssertCompilesClean();
		Assert.Contains("\n        " + emitted + " bool TryG(nuint a, out int v)\n", run.SingleGeneratedText,
			StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_repeats_the_new_modifier_of_a_hiding_declaration()
	{
		const string Source = Usings + """
		                               namespace Demo;
		                               public class Base { public static bool TryG(nuint a, out int v) { v = 0; return false; } }
		                               public partial class Holder : Base { [LuaGlobal("g")] public new static partial bool TryG(nuint a, out int v); }
		                               """;

		GeneratorRun run = roslyn.Run(Source);

		run.AssertCompilesClean();
		Assert.Contains("\n        public new static partial bool TryG(nuint a, out int v)\n", run.SingleGeneratedText,
			StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_old_style_partial_void_without_accessibility_is_implemented_without_one()
	{
		GeneratorRun run = roslyn.Run(Usings +
		                              "namespace Demo; public static partial class Holder { [LuaGlobal(\"beep\")] static partial void Beep(); }");

		run.AssertCompilesClean();
		Assert.Contains("\n        static partial void Beep()\n", run.SingleGeneratedText, StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_private_target_is_reachable_from_the_thunk()
	{
		GeneratorRun run = roslyn.Run(Usings +
		                              "namespace Demo; internal static partial class Holder { [LuaFunction(\"f\")] private static int F(int a) => a; }");

		run.AssertCompilesClean();
		Assert.Contains("global::Demo.Holder.F(__arg0)", run.SingleGeneratedText, StringComparison.Ordinal);
	}
}
