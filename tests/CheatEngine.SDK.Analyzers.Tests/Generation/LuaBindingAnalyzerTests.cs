using System.Collections.Immutable;
using System.Globalization;

using CheatEngine.SDK.Analyzers.Diagnostics;
using CheatEngine.SDK.Analyzers.Generation;
using CheatEngine.SDK.Analyzers.Tests.Infrastructure;
using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Lua.Interop.Api;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.SourceGenerators.LuaBindings;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CheatEngine.SDK.Analyzers.Tests.Generation;

/// <summary>
///     CESDK2001 (AllowUnsafeBlocks), CESDK2002 (containing type), CESDK2003 (<c>[LuaFunction]</c>), CESDK2004
///     (<c>[LuaGlobal]</c>) and CESDK2005 (duplicate valid export name): the <c>LuaBindingAnalyzer</c> rules, which link
///     <c>CheatEngine.SDK.SourceGenerators.LuaBindings</c>'s own shape-validation source instead of a hand-written copy.
///     Test
///     compilations reference the REAL <c>CheatEngine.SDK.Annotations</c>, <c>CheatEngine.SDK.Lua.Interop</c> and
///     <c>CheatEngine.SDK.Lua</c>
///     assemblies (not stubs: <c>CheatEngine.SDK.Lua.State.LuaState</c> is part of the shape the rules recognise),
///     mirroring
///     <c>tests/CheatEngine.SDK.SourceGenerators.LuaBindings.Tests</c>' own approach.
/// </summary>
/// <remarks>
///     The last theory (<see cref="Generator_and_analyzer_agree_on_every_shape" />) builds a small compilation, runs
///     both <c>LuaBindingsGenerator</c> and <c>LuaBindingAnalyzer</c> over it, and asserts that "the generator emits a
///     binding" and "the analyzer reports no shape problem" agree. Full code sharing (used here for the shape checks
///     themselves) already gives CESDK2002/2003/2004 parity by construction; this theory is the extra proof for the
///     handful of shapes that exercise both the generator's grouping step and the analyzer's per-member reporting.
/// </remarks>
public sealed class LuaBindingAnalyzerTests
{
	private const string ShapeUsings = "using CheatEngine.SDK.Annotations.Lua;\nnamespace Demo;\n";

	private const string OptionalUsings =
		"using System;\nusing CheatEngine.SDK.Annotations.Lua;\nusing CheatEngine.SDK.Lua.Calls;\nusing CheatEngine.SDK.Lua.Marshalling;\nnamespace Demo;\n";

	private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp14);

	// The real SDK assemblies the shape checks are written against, taken from the copies loaded in this test
	// process (RoslynEnvironment of CheatEngine.SDK.SourceGenerators.LuaBindings.Tests does the same).
	private static readonly ImmutableArray<MetadataReference> SdkReferences =
	[
		MetadataReference.CreateFromFile(typeof(LuaFunctionAttribute).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(LuaApi).Assembly.Location),
		MetadataReference.CreateFromFile(typeof(LuaState).Assembly.Location)
	];

	// The annotations are present, but no CheatEngine.SDK.Lua runtime assembly is referenced. A source type with the
	// runtime's metadata name must therefore not make a binding valid.
	private static readonly ImmutableArray<MetadataReference> SdkReferencesWithoutLuaRuntime =
	[
		MetadataReference.CreateFromFile(typeof(LuaFunctionAttribute).Assembly.Location)
	];

	public static TheoryData<string, string, bool> Shapes => new()
	{
		{
			"valid function", ShapeUsings +
			                  "public static partial class Functions { [LuaFunction(\"add\")] public static long Add(long a, long b) => a + b; }",
			true
		},
		{
			"instance method", ShapeUsings +
			                   "public partial class Functions { [LuaFunction(\"add\")] public long Add(long a, long b) => a + b; }",
			false
		},
		{
			"not partial container", ShapeUsings +
			                         "public static class Functions { [LuaFunction(\"add\")] public static long Add(long a, long b) => a + b; }",
			false
		},
		{
			"invalid lua name", ShapeUsings +
			                    "public static partial class Functions { [LuaFunction(\"end\")] public static long Add(long a, long b) => a + b; }",
			false
		},
		{
			"valid global try form", ShapeUsings +
			                         "public static partial class Bindings { [LuaGlobal(\"readInteger\")] public static partial bool TryReadInt32(nuint address, out int value); }",
			true
		},
		{
			"global try form returning int instead of bool", ShapeUsings +
			                                                 "public static partial class Bindings { [LuaGlobal(\"readInteger\")] public static partial int TryReadInt32(nuint address, out int value); }",
			false
		},
		{
			"documented detailed global form", """
			                                   using CheatEngine.SDK.Annotations.Lua;
			                                   using CheatEngine.SDK.Lua.Calls;

			                                   namespace MyPlugin;

			                                   public static partial class Memory
			                                   {
			                                       [LuaGlobal("readInteger")]
			                                       public static partial LuaOperationStatus TryReadInt32Detailed(nuint address, bool signed, out int value);
			                                   }
			                                   """,
			true
		}
	};

	// The LuaOptional<T>, optional-result and variadic shapes (CESDK2010 to CESDK2013), kept apart from Shapes only for length.
	public static TheoryData<string, string, bool> OptionalShapes => new()
	{
		{
			"optional arguments of a global", OptionalUsings +
			                                  "public static partial class Bindings { [LuaGlobal(\"load\")] public static partial void Load(string path, LuaOptional<bool> merge, LuaOptional<int> flags); }",
			true
		},
		{
			"optional results of a global", OptionalUsings +
			                                "public static partial class Bindings { [LuaGlobal(\"read\")] public static partial LuaOperationStatus Read(int mode, out long first, out LuaOptional<string> second); }",
			true
		},
		{
			"variadic outcome of a global", OptionalUsings +
			                                "public static partial class Bindings { [LuaGlobal(\"seq\")] public static partial LuaOperationStatus Seq(int n, Span<long> values, out int count); }",
			true
		},
		{
			"optional parameter of a function", OptionalUsings +
			                                    "public static partial class Functions { [LuaFunction(\"f\")] public static int F(int a, LuaOptional<string> b) => a; }",
			true
		},
		{
			"optional argument before a required one", OptionalUsings +
			                                           "public static partial class Bindings { [LuaGlobal(\"g\")] public static partial int G(LuaOptional<int> a, int b); }",
			false
		},
		{
			"required result after an optional one", OptionalUsings +
			                                         "public static partial class Bindings { [LuaGlobal(\"g\")] public static partial bool TryG(out LuaOptional<int> a, out int b); }",
			false
		},
		{
			"variadic pair on the try form", OptionalUsings +
			                                 "public static partial class Bindings { [LuaGlobal(\"g\")] public static partial bool TryG(Span<long> v, out int c); }",
			false
		},
		{
			"optional nullable string", OptionalUsings +
			                            "public static partial class Bindings { [LuaGlobal(\"g\")] public static partial int G(LuaOptional<string?> a); }",
			false
		},
		{
			"optional throwing return", OptionalUsings +
			                            "public static partial class Bindings { [LuaGlobal(\"g\")] public static partial LuaOptional<int> G(int a); }",
			false
		}
	};

	[Fact]
	public async Task Binding_without_allow_unsafe_reports_CESDK2001()
	{
		ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
			"""
			using CheatEngine.SDK.Annotations.Lua;

			namespace Demo;

			public static partial class Functions
			{
			    [LuaFunction("add")]
			    public static long Add(long a, long b) => a + b;
			}
			""",
			false);

		Diagnostic diagnostic = Assert.Single(diagnostics,
			static d => string.Equals(d.Id, DiagnosticIds.UnsafeBlocksRequired, StringComparison.Ordinal));
		Assert.Contains("Add", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
	}

	[Fact]
	public async Task Valid_binding_with_allow_unsafe_reports_nothing()
	{
		ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
			"""
			using CheatEngine.SDK.Annotations.Lua;
			using CheatEngine.SDK.Lua.State;

			namespace Demo;

			public static partial class Functions
			{
			    [LuaFunction("add")]
			    public static long Add(long a, long b) => a + b;

			    [LuaFunction("isint")]
			    public static bool IsInteger(LuaState state, double value) => state.IsInteger(1);
			}

			public static partial class Bindings
			{
			    [LuaGlobal("readInteger")]
			    public static partial bool TryReadInt32(nuint address, out int value);
			}
			""",
			true);

		Assert.Empty(diagnostics);
	}

	[Fact]
	public async Task Non_partial_containing_type_reports_CESDK2002()
	{
		ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
			"""
			using CheatEngine.SDK.Annotations.Lua;

			namespace Demo;

			public static class Functions
			{
			    [LuaFunction("add")]
			    public static long Add(long a, long b) => a + b;
			}
			""",
			true);

		Diagnostic diagnostic = Assert.Single(diagnostics,
			static d => string.Equals(d.Id, DiagnosticIds.InvalidLuaBindingContainingType, StringComparison.Ordinal));
		Assert.Contains("partial", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
	}

	[Fact]
	public async Task Instance_lua_function_reports_CESDK2003()
	{
		ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
			"""
			using CheatEngine.SDK.Annotations.Lua;

			namespace Demo;

			public partial class Functions
			{
			    [LuaFunction("add")]
			    public long Add(long a, long b) => a + b;
			}
			""",
			true);

		Diagnostic diagnostic = Assert.Single(diagnostics,
			static d => string.Equals(d.Id, DiagnosticIds.InvalidLuaFunction, StringComparison.Ordinal));
		Assert.Contains("static", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
	}

	[Fact]
	public async Task Lua_function_with_a_reserved_word_name_reports_CESDK2003()
	{
		ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
			"""
			using CheatEngine.SDK.Annotations.Lua;

			namespace Demo;

			public static partial class Functions
			{
			    [LuaFunction("end")]
			    public static long Add(long a, long b) => a + b;
			}
			""",
			true);

		Diagnostic diagnostic = Assert.Single(diagnostics,
			static d => string.Equals(d.Id, DiagnosticIds.InvalidLuaFunction, StringComparison.Ordinal));
		Assert.Contains("Lua identifier", diagnostic.GetMessage(CultureInfo.InvariantCulture),
			StringComparison.Ordinal);
	}

	[Fact]
	public async Task Lua_global_try_form_not_returning_bool_reports_CESDK2004()
	{
		ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
			"""
			using CheatEngine.SDK.Annotations.Lua;

			namespace Demo;

			public static partial class Bindings
			{
			    [LuaGlobal("readInteger")]
			    public static partial int TryReadInt32(nuint address, out int value);
			}
			""",
			true);

		Diagnostic diagnostic = Assert.Single(diagnostics,
			static d => string.Equals(d.Id, DiagnosticIds.InvalidLuaGlobal, StringComparison.Ordinal));
		Assert.Contains("bool", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
	}

	[Fact]
	public async Task Lua_global_outcome_form_returning_lua_operation_status_is_accepted()
	{
		ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
			"""
			using CheatEngine.SDK.Annotations.Lua;
			using CheatEngine.SDK.Lua.Calls;

			namespace Demo;

			public static partial class Bindings
			{
			    [LuaGlobal("readInteger")]
			    public static partial LuaOperationStatus TryReadInt32Detailed(nuint address, out int value);
			}
			""",
			true);

		Assert.DoesNotContain(diagnostics,
			static diagnostic =>
				string.Equals(diagnostic.Id, DiagnosticIds.InvalidLuaGlobal, StringComparison.Ordinal));
	}

	[Fact]
	public async Task Explicit_static_interface_marshaller_members_report_CESDK2003_and_skip_generation()
	{
		const string source = """
		                      using CheatEngine.SDK.Annotations.Lua;
		                      using CheatEngine.SDK.Lua.Marshalling;
		                      using CheatEngine.SDK.Lua.State;

		                      namespace Demo;

		                      public readonly struct Token { }

		                      public readonly struct ExplicitMarshaller : ILuaMarshaller<Token>
		                      {
		                          static void ILuaMarshaller<Token>.Push(LuaState state, Token value) { }

		                          static bool ILuaMarshaller<Token>.TryRead(LuaState state, int index, out Token value)
		                          {
		                              value = default;
		                              return false;
		                          }
		                      }

		                      public static partial class Bindings
		                      {
		                          [LuaFunction("token")]
		                          public static int RoundTrip([LuaMarshaller(typeof(ExplicitMarshaller))] Token value) => 0;
		                      }
		                      """;
		CSharpCompilation compilation = CreateCompilation(source, true);

		Assert.False(RunGenerator(compilation));

		Diagnostic diagnostic = Assert.Single(await GetDiagnosticsAsync(compilation),
			static d => string.Equals(d.Id, DiagnosticIds.InvalidLuaFunction, StringComparison.Ordinal));
		Assert.Contains("parameter types a marshaller reads", diagnostic.GetMessage(CultureInfo.InvariantCulture),
			StringComparison.Ordinal);
	}

	[Fact]
	public async Task Duplicate_lua_function_names_in_the_same_type_report_CESDK2005_on_both_members()
	{
		// LuaFunctionTables.Group/SelectThunks drops both members from the generator's output with no explanation
		// of its own: this is the compilation-end pass that names the cause.
		ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
			"""
			using CheatEngine.SDK.Annotations.Lua;

			namespace Demo;

			public static partial class Functions
			{
			    [LuaFunction("shared")]
			    public static int First() => 1;

			    [LuaFunction("shared")]
			    public static int Second() => 2;
			}
			""",
			true);

		Diagnostic[] duplicates =
		[
			.. diagnostics.Where(static d =>
				string.Equals(d.Id, DiagnosticIds.DuplicateLuaName, StringComparison.Ordinal))
		];
		Assert.Equal(2, duplicates.Length);
		Assert.Contains(duplicates,
			d => d.GetMessage(CultureInfo.InvariantCulture).Contains("First", StringComparison.Ordinal));
		Assert.Contains(duplicates,
			d => d.GetMessage(CultureInfo.InvariantCulture).Contains("Second", StringComparison.Ordinal));
		Assert.All(duplicates,
			d => Assert.Contains("duplicates the Lua name", d.GetMessage(CultureInfo.InvariantCulture),
				StringComparison.Ordinal));
	}

	[Fact]
	public async Task Duplicate_lua_function_names_in_different_types_report_nothing()
	{
		ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
			"""
			using CheatEngine.SDK.Annotations.Lua;

			namespace Demo;

			public static partial class First
			{
			    [LuaFunction("shared")]
			    public static int Value() => 1;
			}

			public static partial class Second
			{
			    [LuaFunction("shared")]
			    public static int Value() => 2;
			}
			""",
			true);

		Assert.Empty(diagnostics);
	}

	[Fact]
	public async Task Duplicate_lua_function_name_where_one_member_has_another_problem_reports_only_that_problem()
	{
		// 'Second' is not static: it was never a candidate for the generator's grouping step either
		// (LuaFunctionModel.IsValid), so the still-valid 'First' is not a duplicate of anything and is silently
		// exported; only the independent NotStatic problem on 'Second' is reported.
		ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
			"""
			using CheatEngine.SDK.Annotations.Lua;

			namespace Demo;

			public partial class Functions
			{
			    [LuaFunction("shared")]
			    public static int First() => 1;

			    [LuaFunction("shared")]
			    public int Second() => 2;
			}
			""",
			true);

		Diagnostic diagnostic = Assert.Single(diagnostics,
			static d => string.Equals(d.Id, DiagnosticIds.InvalidLuaFunction, StringComparison.Ordinal));
		Assert.Contains("Second", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
		Assert.Contains("static", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
	}

	[Fact]
	public async Task Several_problems_on_one_member_are_all_reported()
	{
		ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(
			"""
			using CheatEngine.SDK.Annotations.Lua;

			namespace Demo;

			public static class Functions
			{
			    [LuaFunction("end")]
			    public static long Add(long a, long b = 0) => a + b;
			}
			""",
			false);

		Assert.Contains(diagnostics,
			static d => string.Equals(d.Id, DiagnosticIds.UnsafeBlocksRequired, StringComparison.Ordinal));
		Assert.Contains(diagnostics,
			static d => string.Equals(d.Id, DiagnosticIds.InvalidLuaBindingContainingType, StringComparison.Ordinal));
		Assert.Contains(diagnostics,
			static d => string.Equals(d.Id, DiagnosticIds.InvalidLuaFunction, StringComparison.Ordinal) &&
			            d.GetMessage(CultureInfo.InvariantCulture).Contains("reserved word", StringComparison.Ordinal));
		Assert.Contains(diagnostics,
			static d => string.Equals(d.Id, DiagnosticIds.InvalidLuaFunction, StringComparison.Ordinal) &&
			            d.GetMessage(CultureInfo.InvariantCulture).Contains("default value", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Same_name_source_LuaState_without_the_sdk_runtime_is_rejected_by_generator_and_analyzer()
	{
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
		CSharpCompilation compilation = CreateCompilation(source, true,
			SdkReferencesWithoutLuaRuntime);

		Assert.False(RunGenerator(compilation));

		ImmutableArray<Diagnostic> diagnostics = await GetDiagnosticsAsync(compilation);
		Assert.Contains(diagnostics,
			static d => string.Equals(d.Id, DiagnosticIds.InvalidLuaFunction, StringComparison.Ordinal) &&
			            d.GetMessage(CultureInfo.InvariantCulture)
				            .Contains("parameter type", StringComparison.Ordinal));
		Assert.Contains(diagnostics,
			static d => string.Equals(d.Id, DiagnosticIds.InvalidLuaGlobal, StringComparison.Ordinal) &&
			            d.GetMessage(CultureInfo.InvariantCulture).Contains("argument type", StringComparison.Ordinal));
	}

	[Theory]
	[MemberData(nameof(Shapes))]
	[MemberData(nameof(OptionalShapes))]
	public async Task Generator_and_analyzer_agree_on_every_shape(string shape, string source, bool expectedValid)
	{
		CSharpCompilation compilation = CreateCompilation(source, true);

		bool generatorEmits = RunGenerator(compilation);
		ImmutableArray<Diagnostic> diagnostics = await GetDiagnosticsAsync(compilation);
		bool analyzerReportsShapeProblem = diagnostics.Any(static d =>
			string.Equals(d.Id, DiagnosticIds.InvalidLuaBindingContainingType, StringComparison.Ordinal)
			|| string.Equals(d.Id, DiagnosticIds.InvalidLuaFunction, StringComparison.Ordinal)
			|| string.Equals(d.Id, DiagnosticIds.InvalidLuaGlobal, StringComparison.Ordinal)
			|| string.Equals(d.Id, DiagnosticIds.NonTrailingOptionalLuaArgument, StringComparison.Ordinal)
			|| string.Equals(d.Id, DiagnosticIds.InvalidOptionalOrVariadicLuaResult, StringComparison.Ordinal)
			|| string.Equals(d.Id, DiagnosticIds.LookAlikeLuaContractType, StringComparison.Ordinal)
			|| string.Equals(d.Id, DiagnosticIds.UnsupportedLuaOptionalPosition, StringComparison.Ordinal));

		Assert.True(generatorEmits == expectedValid,
			$"'{shape}': the generator {(generatorEmits ? "emitted" : "stayed silent")}, expected {(expectedValid ? "output" : "silence")}.");
		Assert.True(
			analyzerReportsShapeProblem != expectedValid,
			$"'{shape}': the analyzer {(analyzerReportsShapeProblem ? "reported" : "stayed silent")} a shape problem, expected it to {(expectedValid ? "stay silent" : "report")}.");
	}

	[Fact]
	public async Task Non_trailing_optional_argument_reports_CESDK2010()
	{
		ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(OptionalUsings + """
			public static partial class Bindings
			{
			    [LuaGlobal("g")]
			    public static partial int G(LuaOptional<int> a, int b);

			    [LuaFunction("f")]
			    public static int F(LuaOptional<int> a, long b) => 0;
			}
			""", true);

		Diagnostic[] reported =
		[
			.. diagnostics.Where(static d =>
				string.Equals(d.Id, DiagnosticIds.NonTrailingOptionalLuaArgument, StringComparison.Ordinal))
		];
		Assert.Equal(2, reported.Length);
		Assert.Contains(reported,
			static d => d.GetMessage(CultureInfo.InvariantCulture).StartsWith(
				"Lua binding 'G' must declare every LuaOptional<T> argument after the required ones",
				StringComparison.Ordinal));
		Assert.Contains(reported,
			static d => d.GetMessage(CultureInfo.InvariantCulture).StartsWith(
				"Lua binding 'F' must declare every LuaOptional<T> parameter after the required ones",
				StringComparison.Ordinal));
		Assert.DoesNotContain(diagnostics, static d =>
			string.Equals(d.Id, DiagnosticIds.InvalidLuaGlobal, StringComparison.Ordinal)
			|| string.Equals(d.Id, DiagnosticIds.InvalidLuaFunction, StringComparison.Ordinal));
	}

	[Fact]
	public async Task Required_result_after_an_optional_result_reports_CESDK2011()
	{
		ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(OptionalUsings +
		                                                            "public static partial class Bindings { [LuaGlobal(\"g\")] public static partial bool TryG(out LuaOptional<int> a, out int b); }",
			true);

		Diagnostic diagnostic = Assert.Single(diagnostics);
		Assert.Equal(DiagnosticIds.InvalidOptionalOrVariadicLuaResult, diagnostic.Id);
		Assert.Contains("after the required results", diagnostic.GetMessage(CultureInfo.InvariantCulture),
			StringComparison.Ordinal);
	}

	[Fact]
	public async Task Variadic_pair_outside_the_outcome_form_reports_CESDK2011()
	{
		ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(OptionalUsings + """
			public static partial class Bindings
			{
			    [LuaGlobal("g")]
			    public static partial bool TryG(Span<long> values, out int count);

			    [LuaGlobal("h")]
			    public static partial LuaOperationStatus H(Span<long> values, out int count, Span<int> more, out int moreCount);

			    [LuaGlobal("k")]
			    public static partial LuaOperationStatus K(Span<string> values, out int count);
			}
			""", true);

		string[] messages =
		[
			.. diagnostics
				.Where(static d => string.Equals(d.Id, DiagnosticIds.InvalidOptionalOrVariadicLuaResult,
					StringComparison.Ordinal))
				.Select(static d => d.GetMessage(CultureInfo.InvariantCulture))
		];
		Assert.Contains(messages,
			static m => m.StartsWith("Lua global binding 'TryG' must return LuaOperationStatus",
				StringComparison.Ordinal));
		Assert.Contains(messages,
			static m => m.StartsWith("Lua global binding 'H' must declare at most one variadic",
				StringComparison.Ordinal));
		Assert.Contains(messages,
			static m => m.StartsWith("Lua global binding 'H' must declare the variadic", StringComparison.Ordinal));
		Assert.Contains(messages,
			static m => m.StartsWith("Lua global binding 'K' must use int, long, float, double, bool or nuint",
				StringComparison.Ordinal));
	}

	[Fact]
	public async Task Same_named_LuaOptional_from_source_reports_CESDK2012_and_the_generator_emits_nothing()
	{
		const string Source = """
		                      using CheatEngine.SDK.Annotations.Lua;

		                      namespace CheatEngine.SDK.Lua.Marshalling
		                      {
		                          public readonly struct LuaOptional<T>
		                          {
		                          }
		                      }

		                      namespace Demo
		                      {
		                          using CheatEngine.SDK.Lua.Marshalling;

		                          public static partial class Bindings
		                          {
		                              [LuaGlobal("g")]
		                              public static partial int G(int a, LuaOptional<int> b);

		                              [LuaFunction("f")]
		                              public static int F(LuaOptional<int> b) => 0;
		                          }
		                      }
		                      """;
		CSharpCompilation compilation = CreateCompilation(Source, true);

		Assert.False(RunGenerator(compilation));
		Diagnostic[] reported =
		[
			.. (await GetDiagnosticsAsync(compilation))
			.Where(static d => string.Equals(d.Id, DiagnosticIds.LookAlikeLuaContractType, StringComparison.Ordinal))
		];
		Assert.Equal(2, reported.Length);
		Assert.All(reported,
			static d => Assert.Contains("not a same-named type", d.GetMessage(CultureInfo.InvariantCulture),
				StringComparison.Ordinal));
	}

	[Fact]
	public async Task
		Same_named_LuaOperationStatus_from_source_reports_CESDK2012_instead_of_selecting_the_outcome_form()
	{
		const string Source = """
		                      using CheatEngine.SDK.Annotations.Lua;

		                      namespace CheatEngine.SDK.Lua.Calls
		                      {
		                          public readonly struct LuaOperationStatus
		                          {
		                          }
		                      }

		                      namespace Demo
		                      {
		                          using CheatEngine.SDK.Lua.Calls;

		                          public static partial class Bindings
		                          {
		                              [LuaGlobal("readInteger")]
		                              public static partial LuaOperationStatus TryReadInt32(nuint address, out int value);
		                          }
		                      }
		                      """;
		CSharpCompilation compilation = CreateCompilation(Source, true);

		Assert.False(RunGenerator(compilation));
		ImmutableArray<Diagnostic> diagnostics = await GetDiagnosticsAsync(compilation);
		Diagnostic diagnostic = Assert.Single(diagnostics);
		Assert.Equal(DiagnosticIds.LookAlikeLuaContractType, diagnostic.Id);
		Assert.StartsWith(
			"Lua binding 'TryReadInt32' must use the LuaOptional<T> and LuaOperationStatus types of CheatEngine.SDK.Lua",
			diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
	}

	[Fact]
	public async Task LuaOptional_of_nullable_string_reports_CESDK2013()
	{
		ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(OptionalUsings + """
			public static partial class Bindings
			{
			    [LuaGlobal("g")]
			    public static partial int G(LuaOptional<string?> a);

			    [LuaGlobal("h")]
			    public static partial bool TryH(out LuaOptional<object> a);

			    [LuaFunction("f")]
			    public static int F(LuaOptional<LuaOptional<int>> a) => 0;
			}
			""", true);

		Assert.Equal(["F", "G", "TryH"],
			diagnostics.Where(static d =>
					string.Equals(d.Id, DiagnosticIds.UnsupportedLuaOptionalPosition, StringComparison.Ordinal))
				.Select(static d => d.GetMessage(CultureInfo.InvariantCulture).Split('\'')[1])
				.Order(StringComparer.Ordinal),
			StringComparer.Ordinal);
	}

	[Fact]
	public async Task LuaOptional_return_reports_CESDK2013()
	{
		ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(OptionalUsings + """
			public static partial class Bindings
			{
			    [LuaGlobal("g")]
			    public static partial LuaOptional<int> G(int a);

			    [LuaFunction("f")]
			    public static LuaOptional<int> F(int a) => default;
			}
			""", true);

		Diagnostic[] reported =
		[
			.. diagnostics.Where(static d =>
				string.Equals(d.Id, DiagnosticIds.UnsupportedLuaOptionalPosition, StringComparison.Ordinal))
		];
		Assert.Equal(2, reported.Length);
		Assert.Contains(reported,
			static d => d.GetMessage(CultureInfo.InvariantCulture)
				.Contains("the throwing form cannot return one", StringComparison.Ordinal));
		Assert.Contains(reported,
			static d => d.GetMessage(CultureInfo.InvariantCulture)
				.Contains("a thunk cannot return one", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Valid_optional_and_variadic_shapes_report_nothing()
	{
		ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(OptionalUsings + """
			public static partial class Bindings
			{
			    [LuaGlobal("load")]
			    public static partial void Load(string path, LuaOptional<bool> merge);

			    [LuaGlobal("read")]
			    public static partial LuaOperationStatus Read(int mode, out long first, out LuaOptional<string> second, Span<double> rest, out int restCount);

			    [LuaFunction("f")]
			    public static int F(int a, LuaOptional<nuint> b) => a;
			}
			""", true);

		Assert.Empty(diagnostics);
	}

	private static CSharpCompilation CreateCompilation(string source, bool allowUnsafe)
	{
		return CreateCompilation(source, allowUnsafe, SdkReferences);
	}

	private static CSharpCompilation CreateCompilation(string source, bool allowUnsafe,
		ImmutableArray<MetadataReference> sdkReferences)
	{
		return CSharpCompilation.Create(
			"LuaBindingAnalyzerTestAssembly",
			[
				CSharpSyntaxTree.ParseText(TestText.Normalize(source), ParseOptions, "Test.cs",
					cancellationToken: TestContext.Current.CancellationToken)
			],
			LocalFrameworkReferences.References.AddRange(sdkReferences),
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
				nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: allowUnsafe));
	}

	private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source, bool allowUnsafe)
	{
		return GetDiagnosticsAsync(CreateCompilation(source, allowUnsafe));
	}

	private static Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(CSharpCompilation compilation)
	{
		CompilationWithAnalyzers withAnalyzers = compilation.WithAnalyzers([new LuaBindingAnalyzer()], options: null);
		return withAnalyzers.GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
	}

	// Same driver shape as CheatEngine.SDK.SourceGenerators.LuaBindings.Tests' GeneratorRun: "emits" means at least one
	// generated source (a thunk file or a wrapper-body file).
	private static bool RunGenerator(CSharpCompilation compilation)
	{
		GeneratorDriver driver = CSharpGeneratorDriver.Create([new LuaBindingsGenerator().AsSourceGenerator()],
			parseOptions: ParseOptions);
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _,
			TestContext.Current.CancellationToken);
		return !driver.GetRunResult().Results.Single().GeneratedSources.IsEmpty;
	}
}
