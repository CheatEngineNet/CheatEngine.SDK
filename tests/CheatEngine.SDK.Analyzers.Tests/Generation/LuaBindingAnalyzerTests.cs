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
///     <c>CheatEngine.SDK.SourceGenerators.LuaBindings</c>'s own shape-validation source instead of a hand-written copy. Test
///     compilations reference the REAL <c>CheatEngine.SDK.Annotations</c>, <c>CheatEngine.SDK.Lua.Interop</c> and <c>CheatEngine.SDK.Lua</c>
///     assemblies (not stubs: <c>CheatEngine.SDK.Lua.State.LuaState</c> is part of the shape the rules recognise), mirroring
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
            "valid function",
            ShapeUsings +
            "public static partial class Functions { [LuaFunction(\"add\")] public static long Add(long a, long b) => a + b; }",
            true
        },
        {
            "instance method",
            ShapeUsings +
            "public partial class Functions { [LuaFunction(\"add\")] public long Add(long a, long b) => a + b; }",
            false
        },
        {
            "not partial container",
            ShapeUsings +
            "public static class Functions { [LuaFunction(\"add\")] public static long Add(long a, long b) => a + b; }",
            false
        },
        {
            "invalid lua name",
            ShapeUsings +
            "public static partial class Functions { [LuaFunction(\"end\")] public static long Add(long a, long b) => a + b; }",
            false
        },
        {
            "valid global try form",
            ShapeUsings +
            "public static partial class Bindings { [LuaGlobal(\"readInteger\")] public static partial bool TryReadInt32(nuint address, out int value); }",
            true
        },
        {
            "global try form returning int instead of bool",
            ShapeUsings +
            "public static partial class Bindings { [LuaGlobal(\"readInteger\")] public static partial int TryReadInt32(nuint address, out int value); }",
            false
        }
    };

    [Fact]
    public async Task Binding_without_allow_unsafe_reports_CESDK2001()
    {
        var diagnostics = await AnalyzeAsync(
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

        var diagnostic = Assert.Single(diagnostics,
            static d => string.Equals(d.Id, DiagnosticIds.UnsafeBlocksRequired, StringComparison.Ordinal));
        Assert.Contains("Add", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Valid_binding_with_allow_unsafe_reports_nothing()
    {
        var diagnostics = await AnalyzeAsync(
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
        var diagnostics = await AnalyzeAsync(
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

        var diagnostic = Assert.Single(diagnostics,
            static d => string.Equals(d.Id, DiagnosticIds.InvalidLuaBindingContainingType, StringComparison.Ordinal));
        Assert.Contains("partial", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Instance_lua_function_reports_CESDK2003()
    {
        var diagnostics = await AnalyzeAsync(
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

        var diagnostic = Assert.Single(diagnostics,
            static d => string.Equals(d.Id, DiagnosticIds.InvalidLuaFunction, StringComparison.Ordinal));
        Assert.Contains("static", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lua_function_with_a_reserved_word_name_reports_CESDK2003()
    {
        var diagnostics = await AnalyzeAsync(
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

        var diagnostic = Assert.Single(diagnostics,
            static d => string.Equals(d.Id, DiagnosticIds.InvalidLuaFunction, StringComparison.Ordinal));
        Assert.Contains("Lua identifier", diagnostic.GetMessage(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lua_global_try_form_not_returning_bool_reports_CESDK2004()
    {
        var diagnostics = await AnalyzeAsync(
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

        var diagnostic = Assert.Single(diagnostics,
            static d => string.Equals(d.Id, DiagnosticIds.InvalidLuaGlobal, StringComparison.Ordinal));
        Assert.Contains("bool", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Duplicate_lua_function_names_in_the_same_type_report_CESDK2005_on_both_members()
    {
        // LuaFunctionTables.Group/SelectThunks drops both members from the generator's output with no explanation
        // of its own: this is the compilation-end pass that names the cause.
        var diagnostics = await AnalyzeAsync(
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
        var diagnostics = await AnalyzeAsync(
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
        var diagnostics = await AnalyzeAsync(
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

        var diagnostic = Assert.Single(diagnostics,
            static d => string.Equals(d.Id, DiagnosticIds.InvalidLuaFunction, StringComparison.Ordinal));
        Assert.Contains("Second", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Contains("static", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Several_problems_on_one_member_are_all_reported()
    {
        var diagnostics = await AnalyzeAsync(
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
        var compilation = CreateCompilation(source, true, SdkReferencesWithoutLuaRuntime);

        Assert.False(RunGenerator(compilation));

        var diagnostics = await GetDiagnosticsAsync(compilation);
        Assert.Contains(diagnostics,
            static d => string.Equals(d.Id, DiagnosticIds.InvalidLuaFunction, StringComparison.Ordinal) &&
                        d.GetMessage(CultureInfo.InvariantCulture).Contains("parameter type", StringComparison.Ordinal));
        Assert.Contains(diagnostics,
            static d => string.Equals(d.Id, DiagnosticIds.InvalidLuaGlobal, StringComparison.Ordinal) &&
                        d.GetMessage(CultureInfo.InvariantCulture).Contains("argument type", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task Generator_and_analyzer_agree_on_every_shape(string shape, string source, bool expectedValid)
    {
        var compilation = CreateCompilation(source, true);

        var generatorEmits = RunGenerator(compilation);
        var diagnostics = await GetDiagnosticsAsync(compilation);
        var analyzerReportsShapeProblem = diagnostics.Any(static d =>
            string.Equals(d.Id, DiagnosticIds.InvalidLuaBindingContainingType, StringComparison.Ordinal)
            || string.Equals(d.Id, DiagnosticIds.InvalidLuaFunction, StringComparison.Ordinal)
            || string.Equals(d.Id, DiagnosticIds.InvalidLuaGlobal, StringComparison.Ordinal));

        Assert.True(generatorEmits == expectedValid,
            $"'{shape}': the generator {(generatorEmits ? "emitted" : "stayed silent")}, expected {(expectedValid ? "output" : "silence")}.");
        Assert.True(
            analyzerReportsShapeProblem != expectedValid,
            $"'{shape}': the analyzer {(analyzerReportsShapeProblem ? "reported" : "stayed silent")} a shape problem, expected it to {(expectedValid ? "stay silent" : "report")}.");
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
            [CSharpSyntaxTree.ParseText(TestText.Normalize(source), ParseOptions, "Test.cs")],
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
        var withAnalyzers = compilation.WithAnalyzers([new LuaBindingAnalyzer()], options: null);
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
