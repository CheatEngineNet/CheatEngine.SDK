using System.Collections.Immutable;
using System.Globalization;
using CheatEngine.SDK.Analyzers.Generation;
using CheatEngine.SDK.Analyzers.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CheatEngine.SDK.Analyzers.Tests.Generation;

/// <summary>CESDK2006 and CESDK2007: unsupported Lua object declarations and source collisions with generated members.</summary>
public sealed class LuaObjectBindingAnalyzerTests
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp14);

    [Fact]
    public async Task Invalid_lua_class_name_reserved_method_parameter_and_property_shape_report_CESDK2006()
    {
        await AnalyzerVerifier<LuaObjectBindingAnalyzer>.VerifyAsync(
            """
            using CheatEngine.SDK.Annotations.Lua;

            namespace Demo;

            [LuaClass("end")]
            public readonly partial struct {|CESDK2006:InvalidName|}
            {
            }

            [LuaClass("Object")]
            public readonly partial struct ValidHandle
            {
                [LuaMethod("call")]
                partial void {|CESDK2006:Call|}(int __ceState);

                [LuaMethod("operate")]
                partial void {|CESDK2006:Operate|}(int __ceOperation);

                [LuaProperty("value")]
                public int {|CESDK2006:Value|} => 0;
            }
            """);
    }

    [Fact]
    public async Task Generated_handle_and_lua_thunk_identity_collisions_report_CESDK2007()
    {
        await AnalyzerVerifier<LuaObjectBindingAnalyzer>.VerifyAsync(
            """
            using CheatEngine.SDK.Annotations.Lua;

            namespace Demo;

            [LuaClass("Object")]
            public readonly partial struct HandleWithCollision
            {
                private readonly int {|CESDK2007:_handle|};
                private readonly int {|CESDK2007:Handle|};
            }

            public static partial class Functions
            {
                [LuaFunction("load")]
                public static void Load() { }

                private static int {|CESDK2007:__LuaThunk_load|}() => 0;
                private static void {|CESDK2007:RegisterLuaFunctions|}() { }

                [LuaGlobal("read")]
                static partial void Read();

                private static int {|CESDK2007:s_luaGlobal_read|};
            }
            """);
    }

    [Fact]
    public async Task Lua_global_generated_local_parameters_report_CESDK2007()
    {
        await AnalyzerVerifier<LuaObjectBindingAnalyzer>.VerifyAsync(
            """
            using CheatEngine.SDK.Annotations.Lua;

            namespace Demo;

            public static partial class Globals
            {
                [LuaGlobal("readL")]
                static partial void ReadL(int {|CESDK2007:__L|});

                [LuaGlobal("readOperation")]
                static partial void ReadOperation(int {|CESDK2007:__operation|});

                [LuaGlobal("readTop")]
                static partial void ReadTop(int {|CESDK2007:__top|});

                [LuaGlobal("readOk")]
                static partial void ReadOk(int {|CESDK2007:__ok|});

                [LuaGlobal("readStatus")]
                static partial void ReadStatus(int {|CESDK2007:__status|});

                [LuaGlobal("readResult")]
                static partial void ReadResult(int {|CESDK2007:__result|});
            }
            """);
    }

    [Fact]
    public async Task Generated_handle_constructor_collision_reports_CESDK2007()
    {
        await AnalyzerVerifier<LuaObjectBindingAnalyzer>.VerifyAsync(
            """
            using CheatEngine.SDK.Annotations.Lua;

            namespace CheatEngine.SDK.Engine.Objects
            {
                public readonly struct CEObject
                {
                }
            }

            namespace Demo
            {
                [LuaClass("Object")]
                public readonly partial struct ConstructorCollision
                {
                    private {|CESDK2007:ConstructorCollision|}(
                        global::CheatEngine.SDK.Engine.Objects.CEObject handle)
                    {
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task Record_and_ref_like_borrowed_handles_report_CESDK2006()
    {
        await AnalyzerVerifier<LuaObjectBindingAnalyzer>.VerifyAsync(
            """
            using CheatEngine.SDK.Annotations.Lua;

            namespace Demo;

            [LuaClass("Record")]
            public readonly partial record struct {|CESDK2006:RecordHandle|}
            {
            }

            [LuaClass("Ref")]
            public readonly ref partial struct {|CESDK2006:RefLikeHandle|}
            {
            }
            """);
    }

    [Fact]
    public async Task Ref_return_methods_and_properties_report_CESDK2006()
    {
        var diagnostics = await AnalyzeAsync(
            """
            using CheatEngine.SDK.Annotations.Lua;

            namespace Demo;

            [LuaClass("Object")]
            public readonly partial struct ObjectHandle
            {
                [LuaMethod("refMethod")]
                private partial ref int RefMethod();

                [LuaMethod("readonlyRefMethod")]
                private partial ref readonly int ReadonlyRefMethod();

                [LuaProperty("refProperty")]
                public partial ref int RefProperty { get; }

                [LuaProperty("readonlyRefProperty")]
                public partial ref readonly int ReadonlyRefProperty { get; }
            }
            """);

        var count = 0;
        foreach (var diagnostic in diagnostics)
        {
            if (!string.Equals(diagnostic.Id, "CESDK2006", StringComparison.Ordinal)) continue;

            count++;
            Assert.Contains("ref and ref readonly", diagnostic.GetMessage(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);
        }

        Assert.Equal(4, count);
    }

    [Fact]
    public async Task Explicit_interface_lua_property_reports_CESDK2006()
    {
        var diagnostics = await AnalyzeAsync(
            """
            using CheatEngine.SDK.Annotations.Lua;

            namespace Demo;

            public interface IValue
            {
                int Value { get; }
            }

            [LuaClass("Object")]
            public readonly partial struct ObjectHandle : IValue
            {
                [LuaProperty("value")]
                partial int IValue.Value { get; }
            }
            """);

        var diagnostic = Assert.Single(diagnostics,
            static candidate => string.Equals(candidate.Id, "CESDK2006", StringComparison.Ordinal));
        Assert.Contains("partial property", diagnostic.GetMessage(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_valid_borrowed_handle_reports_nothing()
    {
        await AnalyzerVerifier<LuaObjectBindingAnalyzer>.VerifyAsync(
            """
            using CheatEngine.SDK.Annotations.Lua;

            namespace Demo;

            [LuaClass("Object")]
            public readonly partial struct ObjectHandle
            {
            }
            """);
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var compilation = CSharpCompilation.Create(
            "LuaObjectBindingAnalyzerTestAssembly",
            [CSharpSyntaxTree.ParseText(TestText.Normalize(source), ParseOptions, "Test.cs")],
            LocalFrameworkReferences.References.AddRange(ContractStubs.References),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var withAnalyzers = compilation.WithAnalyzers([new LuaObjectBindingAnalyzer()], options: null);
        return withAnalyzers.GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
    }
}
