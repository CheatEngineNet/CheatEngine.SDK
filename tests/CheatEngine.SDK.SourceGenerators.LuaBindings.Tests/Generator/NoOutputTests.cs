using System.Collections.Immutable;

using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Infrastructure;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Generator;

/// <summary>
///     Every input the generator must stay silent on: no file, no diagnostic, no exception. Each row is one shape the
///     CESDK2xxx analyzer rules are expected to report instead.
/// </summary>
public sealed class NoOutputTests(RoslynFixture roslyn) : IClassFixture<RoslynFixture>
{
	private const string Usings =
		"using System; using CheatEngine.SDK.Annotations.Lua; using CheatEngine.SDK.Lua.State;\n";

	public static TheoryData<string, string> InvalidFunctions
	{
		get
		{
			TheoryData<string, string> data = new();
			foreach ((string shape, string source) in FunctionMethodShapeRejections())
			{
				data.Add(shape, source);
			}

			foreach ((string shape, string source) in FunctionContainingTypeRejections())
			{
				data.Add(shape, source);
			}

			foreach ((string shape, string source) in FunctionNameRejections())
			{
				data.Add(shape, source);
			}

			foreach ((string shape, string source) in FunctionParameterTypeRejections())
			{
				data.Add(shape, source);
			}

			foreach ((string shape, string source) in FunctionParameterModifierRejections())
			{
				data.Add(shape, source);
			}

			foreach ((string shape, string source) in FunctionReturnTypeRejections())
			{
				data.Add(shape, source);
			}

			foreach ((string shape, string source) in FunctionDeclarationSiteRejections())
			{
				data.Add(shape, source);
			}

			foreach ((string shape, string source) in FunctionOptionalRejections())
			{
				data.Add(shape, source);
			}

			return data;
		}
	}

	public static TheoryData<string, string> InvalidGlobals
	{
		get
		{
			TheoryData<string, string> data = new();
			foreach ((string shape, string source) in GlobalShapeRejections())
			{
				data.Add(shape, source);
			}

			foreach ((string shape, string source) in GlobalNameRejections())
			{
				data.Add(shape, source);
			}

			foreach ((string shape, string source) in GlobalArgumentRejections())
			{
				data.Add(shape, source);
			}

			foreach ((string shape, string source) in GlobalResultTypeRejections())
			{
				data.Add(shape, source);
			}

			foreach ((string shape, string source) in GlobalCopyOutRejections())
			{
				data.Add(shape, source);
			}

			foreach ((string shape, string source) in GlobalReturnTypeRejections())
			{
				data.Add(shape, source);
			}

			foreach ((string shape, string source) in GlobalTryFormAndMiscRejections())
			{
				data.Add(shape, source);
			}

			foreach ((string shape, string source) in GlobalOptionalRejections())
			{
				data.Add(shape, source);
			}

			return data;
		}
	}

	private static IEnumerable<(string Shape, string Source)> FunctionMethodShapeRejections()
	{
		yield return (
			"instance method", "public static partial class T { [LuaFunction(\"f\")] public int F(int a) => a; }");
		yield return (
			"generic method",
			"public static partial class T { [LuaFunction(\"f\")] public static int F<TArg>(int a) => a; }");
		yield return (
			"async void method",
			"public static partial class T { [LuaFunction(\"f\")] public static async void F() { await System.Threading.Tasks.Task.Yield(); } }");
		yield return (
			"async Task method",
			"public static partial class T { [LuaFunction(\"f\")] public static async System.Threading.Tasks.Task F() { await System.Threading.Tasks.Task.Yield(); } }");
	}

	private static IEnumerable<(string Shape, string Source)> FunctionContainingTypeRejections()
	{
		yield return (
			"generic containing type",
			"public static partial class T<TArg> { [LuaFunction(\"f\")] public static int F(int a) => a; }");
		yield return (
			"nested in a generic type",
			"public partial class O<TArg> { public static partial class T { [LuaFunction(\"f\")] public static int F(int a) => a; } }");
		yield return (
			"containing type not partial",
			"public static class T { [LuaFunction(\"f\")] public static int F(int a) => a; }");
		yield return (
			"outer type not partial",
			"public class O { public static partial class T { [LuaFunction(\"f\")] public static int F(int a) => a; } }");
		yield return (
			"containing type is an interface",
			"public partial interface T { [LuaFunction(\"f\")] public static int F(int a) => a; }");
		yield return (
			"file-local containing type",
			"file static partial class T { [LuaFunction(\"f\")] public static int F(int a) => a; }");
	}

	private static IEnumerable<(string Shape, string Source)> FunctionNameRejections()
	{
		yield return (
			"empty name", "public static partial class T { [LuaFunction(\"\")] public static int F(int a) => a; }");
		yield return (
			"null name", "public static partial class T { [LuaFunction(null!)] public static int F(int a) => a; }");
		yield return (
			"name starting with a digit",
			"public static partial class T { [LuaFunction(\"1f\")] public static int F(int a) => a; }");
		yield return (
			"name with a dash",
			"public static partial class T { [LuaFunction(\"read-int\")] public static int F(int a) => a; }");
		yield return (
			"name with a dot",
			"public static partial class T { [LuaFunction(\"ce.read\")] public static int F(int a) => a; }");
		yield return (
			"reserved word as name",
			"public static partial class T { [LuaFunction(\"end\")] public static int F(int a) => a; }");
		yield return (
			"non-ASCII name",
			"public static partial class T { [LuaFunction(\"caf\\u00E9\")] public static int F(int a) => a; }");
	}

	private static IEnumerable<(string Shape, string Source)> FunctionParameterTypeRejections()
	{
		yield return (
			"object parameter",
			"public static partial class T { [LuaFunction(\"f\")] public static int F(object a) => 0; }");
		yield return (
			"nullable int parameter",
			"public static partial class T { [LuaFunction(\"f\")] public static int F(int? a) => 0; }");
		yield return (
			"byte parameter",
			"public static partial class T { [LuaFunction(\"f\")] public static int F(byte a) => a; }");
		yield return (
			"decimal parameter",
			"public static partial class T { [LuaFunction(\"f\")] public static int F(decimal a) => 0; }");
		yield return (
			"Span<byte> parameter",
			"public static partial class T { [LuaFunction(\"f\")] public static int F(Span<byte> a) => a.Length; }");
	}

	private static IEnumerable<(string Shape, string Source)> FunctionParameterModifierRejections()
	{
		yield return (
			"state parameter not first",
			"public static partial class T { [LuaFunction(\"f\")] public static int F(int a, LuaState L) => a; }");
		yield return (
			"ref parameter",
			"public static partial class T { [LuaFunction(\"f\")] public static int F(ref int a) => a; }");
		yield return (
			"in parameter",
			"public static partial class T { [LuaFunction(\"f\")] public static int F(in int a) => a; }");
		yield return (
			"out parameter",
			"public static partial class T { [LuaFunction(\"f\")] public static int F(out int a) { a = 0; return 0; } }");
		yield return (
			"params parameter",
			"public static partial class T { [LuaFunction(\"f\")] public static int F(params int[] a) => a.Length; }");
		yield return (
			"optional parameter",
			"public static partial class T { [LuaFunction(\"f\")] public static int F(int a = 5) => a; }");
	}

	private static IEnumerable<(string Shape, string Source)> FunctionReturnTypeRejections()
	{
		yield return (
			"object return",
			"public static partial class T { [LuaFunction(\"f\")] public static object F(int a) => a; }");
		yield return (
			"array return",
			"public static partial class T { [LuaFunction(\"f\")] public static int[] F(int a) => new[] { a }; }");
		yield return (
			"Span<byte> return",
			"public static partial class T { [LuaFunction(\"f\")] public static Span<byte> F(int a) => default; }");
		yield return (
			"nullable int return",
			"public static partial class T { [LuaFunction(\"f\")] public static int? F(int a) => a; }");
		yield return (
			"ref return",
			"public static partial class T { private static int s_field; [LuaFunction(\"f\")] public static ref int F(int a) => ref s_field; }");
	}

	private static IEnumerable<(string Shape, string Source)> FunctionDeclarationSiteRejections()
	{
		yield return (
			"duplicate names in one type",
			"public static partial class T { [LuaFunction(\"f\")] public static int F(int a) => a; [LuaFunction(\"f\")] public static int G(int a) => a; }");
		yield return (
			"local function",
			"public static partial class T { public static int Outer() { [LuaFunction(\"f\")] static int F(int a) => a; return F(1); } }");
		yield return (
			"lambda",
			"public static partial class T { public static Func<int, int> Outer() => [LuaFunction(\"f\")] (int a) => a; }");
		yield return (
			"property accessor",
			"public static partial class T { public static int P { [LuaFunction(\"f\")] get => 1; } }");
		yield return (
			"look-alike attribute",
			"namespace Other { public sealed class LuaFunctionAttribute : Attribute { public LuaFunctionAttribute(string n) { } } } public static partial class T { [Other.LuaFunction(\"f\")] public static int F(int a) => a; }");
	}

	private static IEnumerable<(string Shape, string Source)> GlobalShapeRejections()
	{
		yield return (
			"not partial",
			"public static partial class T { [LuaGlobal(\"g\")] public static bool TryG(nuint a, out int v) { v = 0; return false; } }");
		yield return (
			"already implemented",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial bool TryG(nuint a, out int v); public static partial bool TryG(nuint a, out int v) { v = 0; return false; } }");
		yield return (
			"attribute on the implementing part",
			"public static partial class T { public static partial bool TryG(nuint a, out int v); [LuaGlobal(\"g\")] public static partial bool TryG(nuint a, out int v) { v = 0; return false; } }");
		yield return (
			"instance method",
			"public partial class T { [LuaGlobal(\"g\")] public partial bool TryG(nuint a, out int v); public partial bool TryG(nuint a, out int v) { v = 0; return false; } }");
		yield return (
			"generic method",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial bool TryG<TArg>(nuint a, out int v); public static partial bool TryG<TArg>(nuint a, out int v) { v = 0; return false; } }");
		yield return (
			"generic containing type",
			"public static partial class T<TArg> { [LuaGlobal(\"g\")] public static partial bool TryG(nuint a, out int v); public static partial bool TryG(nuint a, out int v) { v = 0; return false; } }");
		yield return (
			"containing type not partial",
			"public static class T { [LuaGlobal(\"g\")] public static partial bool TryG(nuint a, out int v); public static partial bool TryG(nuint a, out int v) { v = 0; return false; } }");
		yield return (
			"async",
			"public static partial class T { [LuaGlobal(\"g\")] public static async partial void G(int a); public static async partial void G(int a) { await System.Threading.Tasks.Task.Yield(); } }");
	}

	private static IEnumerable<(string Shape, string Source)> GlobalNameRejections()
	{
		yield return ("empty name",
			"public static partial class T { [LuaGlobal(\"\")] public static partial int G(nuint a); }");
		yield return (
			"reserved word as name",
			"public static partial class T { [LuaGlobal(\"function\")] public static partial int G(nuint a); }");
	}

	private static IEnumerable<(string Shape, string Source)> GlobalArgumentRejections()
	{
		yield return (
			"object argument",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial int G(object a); }");
		yield return (
			"byte argument",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial int G(byte a); }");
		yield return (
			"ref argument",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial int G(ref int a); }");
		yield return (
			"in argument",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial int G(in int a); }");
		yield return (
			"params argument",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial int G(params int[] a); }");
		yield return (
			"optional argument",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial int G(int a = 1); }");
		yield return (
			"state parameter not first",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial int G(int a, LuaState L); }");
	}

	private static IEnumerable<(string Shape, string Source)> GlobalResultTypeRejections()
	{
		yield return (
			"object result",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial bool TryG(nuint a, out object v); }");
		yield return (
			"byte result",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial bool TryG(nuint a, out byte v); }");
		yield return (
			"span result",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial bool TryG(nuint a, out ReadOnlySpan<byte> v); }");
		yield return (
			"span return",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial ReadOnlySpan<byte> G(nuint a); }");
		yield return (
			"Span<byte> return",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial Span<byte> G(nuint a); }");
	}

	private static IEnumerable<(string Shape, string Source)> GlobalCopyOutRejections()
	{
		yield return (
			"copy-out destination without a count",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial bool TryG(nuint a, Span<byte> d, out long w); }");
		yield return (
			"copy-out destination alone",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial bool TryG(nuint a, Span<byte> d); }");
		yield return (
			"argument after a result",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial bool TryG(out int v, nuint a); }");
	}

	private static IEnumerable<(string Shape, string Source)> GlobalReturnTypeRejections()
	{
		yield return (
			"object return",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial object G(nuint a); }");
		yield return (
			"array return",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial int[] G(nuint a); }");
		yield return (
			"nullable int return",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial int? G(nuint a); }");
	}

	private static IEnumerable<(string Shape, string Source)> GlobalTryFormAndMiscRejections()
	{
		yield return (
			"Try form returning int",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial int G(nuint a, out int v); }");
		yield return (
			"Try form returning void",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial void G(out int v); }");
		yield return (
			"partial property",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial int G { get; } public static partial int G => 1; }");
		yield return (
			"look-alike attribute",
			"namespace Other { public sealed class LuaGlobalAttribute : Attribute { public LuaGlobalAttribute(string n) { } } } public static partial class T { [Other.LuaGlobal(\"g\")] public static partial int G(nuint a); public static partial int G(nuint a) => 0; }");
	}

	// CESDK2010, CESDK2011 and CESDK2013 shapes of a bound global (LuaOptional<T> spelled in full: the row usings are
	// shared with the other rows).
	private static IEnumerable<(string Shape, string Source)> GlobalOptionalRejections()
	{
		const string Optional = "CheatEngine.SDK.Lua.Marshalling.LuaOptional";
		const string Status = "CheatEngine.SDK.Lua.Calls.LuaOperationStatus";
		yield return (
			"required argument after an optional one",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial int G(" + Optional +
			"<int> a, int b); }");
		yield return (
			"required result after an optional one",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial bool TryG(out " + Optional +
			"<int> a, out int b); }");
		yield return (
			"copy-out result after an optional one",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial bool TryG(out " + Optional +
			"<int> a, Span<byte> d, out int w); }");
		yield return (
			"variadic pair in the Try form",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial bool TryG(Span<long> v, out int c); }");
		yield return (
			"variadic pair in the throwing form",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial void G(Span<long> v, out int c); }");
		yield return (
			"result after the variadic pair",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial " + Status +
			" G(Span<long> v, out int c, out int d); }");
		yield return (
			"two variadic pairs",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial " + Status +
			" G(Span<long> v, out int c, Span<int> w, out int e); }");
		yield return (
			"variadic string element",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial " + Status +
			" G(Span<string> v, out int c); }");
		yield return (
			"variadic span without a count",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial " + Status +
			" G(Span<long> v); }");
		yield return (
			"optional nullable string",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial int G(" + Optional +
			"<string?> a); }");
		yield return (
			"optional object",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial int G(" + Optional +
			"<object> a); }");
		yield return (
			"nested optional",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial int G(" + Optional + "<" +
			Optional + "<int>> a); }");
		yield return (
			"optional throwing return",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial " + Optional +
			"<int> G(int a); }");
		yield return (
			"optional with a C# default value",
			"public static partial class T { [LuaGlobal(\"g\")] public static partial int G(" + Optional +
			"<int> a = default); }");
	}

	// CESDK2010 and CESDK2013 shapes of an exported function.
	private static IEnumerable<(string Shape, string Source)> FunctionOptionalRejections()
	{
		const string Optional = "CheatEngine.SDK.Lua.Marshalling.LuaOptional";
		yield return (
			"required parameter after an optional one",
			"public static partial class T { [LuaFunction(\"f\")] public static int F(" + Optional +
			"<int> a, int b) => b; }");
		yield return (
			"optional return",
			"public static partial class T { [LuaFunction(\"f\")] public static " + Optional +
			"<int> F(int a) => default; }");
		yield return (
			"optional nullable string parameter",
			"public static partial class T { [LuaFunction(\"f\")] public static int F(" + Optional +
			"<string?> a) => 0; }");
	}

	[Fact]
	public void Generator_non_trailing_optional_argument_emits_nothing()
	{
		const string Source = "using CheatEngine.SDK.Annotations.Lua;\nusing CheatEngine.SDK.Lua.Marshalling;\n" +
							  "namespace Demo; public static partial class T { [LuaGlobal(\"g\")] public static partial int G(LuaOptional<int> a, int b); " +
							  "[LuaFunction(\"f\")] public static int F(LuaOptional<int> a, long b) => 0; }";

		roslyn.Run(Source).AssertNoOutput();
	}

	[Fact]
	public void Generator_same_named_LuaOptional_from_source_is_not_a_binding_contract()
	{
		// The real CheatEngine.SDK.Lua is referenced; the source copy wins C# name lookup (CS0436) but is a look-alike.
		const string Source = """
		                      using CheatEngine.SDK.Annotations.Lua;

		                      namespace CheatEngine.SDK.Lua.Marshalling
		                      {
		                          public readonly struct LuaOptional<T>
		                          {
		                              public bool IsOmitted => true;
		                          }
		                      }

		                      namespace Demo
		                      {
		                          using CheatEngine.SDK.Lua.Marshalling;

		                          public static partial class T
		                          {
		                              [LuaGlobal("g")]
		                              public static partial int G(int a, LuaOptional<int> b);

		                              [LuaGlobal("h")]
		                              public static partial bool TryH(int a, out LuaOptional<int> b);

		                              [LuaFunction("f")]
		                              public static int F(LuaOptional<int> b) => 0;
		                          }
		                      }
		                      """;

		roslyn.Run(Source).AssertNoOutput();
	}

	[Fact]
	public void Generator_same_named_LuaOperationStatus_from_another_assembly_does_not_select_the_outcome_form()
	{
		CSharpCompilation lookAlikeAssembly = CSharpCompilation.Create(
			"Fake.Lua.Contracts",
			[
				RoslynFixture.Parse(
					"namespace CheatEngine.SDK.Lua.Calls { public readonly struct LuaOperationStatus { } }",
					"Fake.cs")
			],
			roslyn.Environment.FrameworkReferences,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
		MetadataReference fake = lookAlikeAssembly.ToMetadataReference(["Fake"]);
		const string Source = """
		                      extern alias Fake;
		                      using CheatEngine.SDK.Annotations.Lua;

		                      namespace Demo;

		                      public static partial class T
		                      {
		                          [LuaGlobal("readInteger")]
		                          public static partial Fake::CheatEngine.SDK.Lua.Calls.LuaOperationStatus TryReadInt32(nuint address, out int value);

		                          [LuaGlobal("beep")]
		                          public static partial Fake::CheatEngine.SDK.Lua.Calls.LuaOperationStatus Beep();
		                      }
		                      """;
		CSharpCompilation compilation = roslyn.CreateCompilation(Source).AddReferences(fake);

		RoslynFixture.Run(compilation).AssertNoOutput();
	}

	[Theory]
	[MemberData(nameof(InvalidFunctions))]
	public void Generator_invalid_function_shape_emits_nothing(string shape, string source)
	{
		Assert.NotEmpty(shape);

		GeneratorRun run = roslyn.Run(Usings + source);

		run.AssertNoOutput();
	}

	[Theory]
	[MemberData(nameof(InvalidGlobals))]
	public void Generator_invalid_global_shape_emits_nothing(string shape, string source)
	{
		Assert.NotEmpty(shape);

		GeneratorRun run = roslyn.Run(Usings + source);

		run.AssertNoOutput();
	}

	[Fact]
	public void Generator_no_attribute_emits_nothing()
	{
		GeneratorRun run = roslyn.Run(Usings +
									  "public static partial class T { public static int F(int a) => a; public static partial int G(int a); public static partial int G(int a) => a; }");

		run.AssertNoOutput();
	}

	[Fact]
	public void Generator_same_name_source_LuaState_without_the_sdk_runtime_emits_nothing()
	{
		ImmutableArray<MetadataReference>.Builder references = ImmutableArray.CreateBuilder<MetadataReference>();
		references.AddRange(roslyn.Environment.FrameworkReferences);
		foreach (MetadataReference reference in roslyn.Environment.SdkReferences)
		{
			if (string.Equals(reference.Display, typeof(LuaState).Assembly.Location,
					StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			references.Add(reference);
		}

		const string source = """
		                      using CheatEngine.SDK.Annotations.Lua;

		                      namespace CheatEngine.SDK.Lua.State
		                      {
		                          public readonly struct LuaState
		                          {
		                          }
		                      }

		                      namespace Demo;

		                      public static partial class Functions
		                      {
		                          [LuaFunction("callback")]
		                          public static int Callback(global::CheatEngine.SDK.Lua.State.LuaState state) => 0;
		                      }

		                      public static partial class Globals
		                      {
		                          [LuaGlobal("read")]
		                          public static partial int Read(global::CheatEngine.SDK.Lua.State.LuaState state);
		                      }
		                      """;

		CSharpCompilation compilation = CSharpCompilation.Create(
			RoslynFixture.PluginAssemblyName,
			[RoslynFixture.Parse(source, "Source0.cs")],
			references.ToImmutable(),
			RoslynEnvironment.CompilationOptions);

		RoslynFixture.Run(compilation).AssertNoOutput();
	}

	[Fact]
	public void Generator_unsafe_not_allowed_skips_functions_but_emits_globals()
	{
		GeneratorRun run = RoslynFixture.Run(roslyn.CreateCompilation(RoslynEnvironment.SafeCompilationOptions,
			BindingSources.Functions, BindingSources.Globals));

		Assert.Null(run.Result.Exception);
		Assert.Empty(run.GeneratorDiagnostics);
		Assert.Single(run.GeneratedSources);
		Assert.Equal("Demo.Memory.LuaGlobals.g.cs", run.HintNames[0]);
	}

	[Fact]
	public void Generator_invalid_member_next_to_a_valid_one_is_skipped_alone()
	{
		const string Source = Usings + """
		                               public static partial class T
		                               {
		                                   [LuaFunction("good")] public static int Good(int a) => a;
		                                   [LuaFunction("bad")] public static int Bad(object a) => 0;
		                                   [LuaGlobal("readInteger")] public static partial bool TryRead(nuint a, out int v);
		                                   [LuaGlobal("broken")] public static partial bool TryBroken(nuint a, out object v);
		                               }
		                               """;

		GeneratorRun run = roslyn.Run(Source);

		Assert.Equal(2, run.GeneratedSources.Length);
		string functions = run.GeneratedText("T.LuaFunctions.g.cs");
		Assert.Contains("__LuaThunk_good", functions, StringComparison.Ordinal);
		Assert.DoesNotContain("__LuaThunk_bad", functions, StringComparison.Ordinal);
		string globals = run.GeneratedText("T.LuaGlobals.g.cs");
		Assert.Contains("TryRead(nuint a, out int v)", globals, StringComparison.Ordinal);
		Assert.DoesNotContain("TryBroken", globals, StringComparison.Ordinal);
		Assert.DoesNotContain("s_luaGlobal_broken", globals, StringComparison.Ordinal);
	}

	[Fact]
	public void Generator_generated_function_identity_collision_skips_only_the_affected_function()
	{
		const string Source = Usings + """
		                               public static partial class T
		                               {
		                                   [LuaFunction("good")] public static int Good(int a) => a;
		                                   [LuaFunction("bad")] public static int Bad(int a) => a;
		                                   private static int __LuaThunk_bad(nint handle) => 0;
		                               }
		                               """;

		GeneratorRun run = roslyn.Run(Source);

		string functions = run.SingleGeneratedText;
		Assert.Contains("__LuaThunk_good", functions, StringComparison.Ordinal);
		Assert.DoesNotContain("__LuaThunk_bad(nint", functions, StringComparison.Ordinal);
		run.AssertCompilesClean();
	}

	[Fact]
	public void Generator_generated_global_cache_collision_skips_only_the_affected_global()
	{
		const string Source = Usings + """
		                               public static partial class T
		                               {
		                                   private static readonly global::CheatEngine.SDK.Lua.References.LuaRef s_luaGlobal_bad = new();
		                                   [LuaGlobal("bad")] public static int Bad(nuint address) => address > 0 ? 1 : 0;
		                                   [LuaGlobal("good")] public static partial int Good(nuint address);
		                               }
		                               """;

		GeneratorRun run = roslyn.Run(Source);

		string globals = run.SingleGeneratedText;
		Assert.DoesNotContain("Bad(nuint address)", globals, StringComparison.Ordinal);
		Assert.Contains("Good(nuint address)", globals, StringComparison.Ordinal);
		Assert.DoesNotContain("s_luaGlobal_bad = new", globals, StringComparison.Ordinal);
		run.AssertCompilesClean();
	}

	[Fact]
	public void Generator_duplicate_name_drops_both_members_but_keeps_the_others()
	{
		const string Source = Usings + """
		                               public static partial class T
		                               {
		                                   [LuaFunction("twin")] public static int A(int a) => a;
		                                   [LuaFunction("twin")] public static int B(int a) => a;
		                                   [LuaFunction("single")] public static int C(int a) => a;
		                               }
		                               """;

		GeneratorRun run = roslyn.Run(Source);

		string text = run.SingleGeneratedText;
		Assert.DoesNotContain("twin", text, StringComparison.Ordinal);
		Assert.Contains("__LuaThunk_single", text, StringComparison.Ordinal);
		run.AssertCompilesClean();
	}

	[Fact]
	public void Generator_same_global_bound_twice_shares_one_cache_and_is_not_a_duplicate()
	{
		GeneratorRun run = roslyn.Run(BindingSources.Globals);

		string text = run.SingleGeneratedText;
		Assert.Equal(1, Count(text, "private static readonly global::CheatEngine.SDK.Lua.References.LuaRef "));
		Assert.Equal(2, Count(text, "TryPush(__L, global::Demo.Memory.s_luaGlobal_readInteger, \"readInteger\"u8)"));
	}

	private static int Count(string text, string needle)
	{
		int count = 0;
		for (int index = text.IndexOf(needle, StringComparison.Ordinal);
			 index >= 0;
			 index = text.IndexOf(needle, index + needle.Length, StringComparison.Ordinal))
		{
			count++;
		}

		return count;
	}
}
