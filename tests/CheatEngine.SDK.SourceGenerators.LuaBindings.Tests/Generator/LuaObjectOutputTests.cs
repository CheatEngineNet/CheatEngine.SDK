using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Infrastructure;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Generator;

/// <summary>
///     Contract tests for the borrowed-handle side of the Lua bindings generator. These tests deliberately inspect the
///     emitted primitives as well as compiling them: an object wrapper must never turn into a second Lua stack runtime.
/// </summary>
public sealed class LuaObjectOutputTests(RoslynFixture roslyn) : IClassFixture<RoslynFixture>
{
    private const string Source = """
                                  using CheatEngine.SDK.Annotations.Lua;

                                  namespace Demo;

                                  [LuaClass("MemScan")]
                                  public readonly partial struct Scan
                                  {
                                      [LuaMethod("firstScan")]
                                      public partial void FirstScan();

                                      [LuaMethod("getCount")]
                                      public partial int GetCount();

                                      [LuaMethod("tryGetAddress")]
                                      public partial bool TryGetAddress(int index, out long address);

                                      [LuaProperty("Count")]
                                      public partial int Count { get; set; }
                                  }
                                  """;

    [Fact]
    public void Lua_annotation_usage_is_explicit_and_excludes_global_properties()
    {
        var luaClass = AttributeUsage(typeof(LuaClassAttribute));
        Assert.Equal(AttributeTargets.Struct, luaClass.ValidOn);
        Assert.False(luaClass.Inherited);
        Assert.False(luaClass.AllowMultiple);

        var luaGlobal = AttributeUsage(typeof(LuaGlobalAttribute));
        Assert.Equal(AttributeTargets.Method, luaGlobal.ValidOn);
        Assert.False(luaGlobal.Inherited);
        Assert.False(luaGlobal.AllowMultiple);

        Assert.False(AttributeUsage(typeof(LuaMethodAttribute)).AllowMultiple);
        Assert.False(AttributeUsage(typeof(LuaPropertyAttribute)).AllowMultiple);
    }

    [Fact]
    public void Borrowed_handle_generates_identity_marshalling_and_protected_members()
    {
        var run = roslyn.Run(Source);

        var handle = run.GeneratedText("Demo.Scan.LuaClass.g.cs");
        Assert.Contains("private readonly global::CheatEngine.SDK.Engine.Objects.CEObject _handle;", handle,
            StringComparison.Ordinal);
        Assert.Contains("ICEObject<global::Demo.Scan>", handle, StringComparison.Ordinal);
        Assert.Contains("ILuaMarshaller<global::Demo.Scan>", handle, StringComparison.Ordinal);
        Assert.Contains("public static global::Demo.Scan FromHandle", handle, StringComparison.Ordinal);
        Assert.Contains("public static bool TryRead", handle, StringComparison.Ordinal);
        Assert.Contains("public static bool operator ==", handle, StringComparison.Ordinal);

        var members = run.GeneratedText("Demo.Scan.LuaObjectMembers.g.cs");
        Assert.Contains("this.Handle.TryPushMethodLeavingObject(__ceState, \"firstScan\"u8)", members,
            StringComparison.Ordinal);
        Assert.Contains("__ceState.SetTop(__ceTop);", members, StringComparison.Ordinal);
        Assert.Contains("this.Handle.TryGetProperty<global::CheatEngine.SDK.Lua.Marshalling.Int32Marshaller, int>", members,
            StringComparison.Ordinal);
        Assert.Contains("this.Handle.TrySetProperty<global::CheatEngine.SDK.Lua.Marshalling.Int32Marshaller, int>", members,
            StringComparison.Ordinal);
        run.AssertCompilesClean();
    }

    [Fact]
    public void Object_method_parameter_named_handle_does_not_shadow_the_generated_property()
    {
        const string source = """
                              using CheatEngine.SDK.Annotations.Lua;

                              namespace Demo;

                              [LuaClass("Object")]
                              public readonly partial struct ObjectHandle
                              {
                                  [LuaMethod("call")]
                                  public partial void Call(int Handle);
                              }
                              """;

        var run = roslyn.Run(source);

        run.AssertCompilesClean();
        Assert.Contains("this.Handle.TryPushMethodLeavingObject", run.GeneratedText("Demo.ObjectHandle.LuaObjectMembers.g.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_handle_does_not_block_a_valid_sibling()
    {
        const string source = """
                              using CheatEngine.SDK.Annotations.Lua;

                              namespace Demo;

                              [LuaClass("Bad")]
                              public partial struct Bad
                              {
                              }

                              [LuaClass("Good")]
                              public readonly partial struct Good
                              {
                              }
                              """;

        var run = roslyn.Run(source);

        Assert.Single(run.GeneratedSources);
        Assert.Equal("Demo.Good.LuaClass.g.cs", run.HintNames[0]);
        Assert.Contains("partial struct Good", run.SingleGeneratedText, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_handle_name_does_not_generate_members_without_its_handle()
    {
        const string source = """
                              using CheatEngine.SDK.Annotations.Lua;

                              namespace Demo;

                              [LuaClass("")]
                              public readonly partial struct Bad
                              {
                                  [LuaMethod("go")]
                                  partial void Go();
                              }

                              [LuaClass("Good")]
                              public readonly partial struct Good
                              {
                                  [LuaMethod("go")]
                                  public partial void Go();
                              }
                              """;

        var run = roslyn.Run(source);

        Assert.Equal(2, run.GeneratedSources.Length);
        Assert.Contains("Demo.Good.LuaClass.g.cs", run.HintNames, StringComparer.Ordinal);
        Assert.Contains("Demo.Good.LuaObjectMembers.g.cs", run.HintNames, StringComparer.Ordinal);
        Assert.DoesNotContain("Bad", run.GeneratedText("Demo.Good.LuaObjectMembers.g.cs"), StringComparison.Ordinal);
        run.AssertCompilesClean();
    }

    [Fact]
    public void Generated_identity_collision_skips_only_the_affected_handle()
    {
        const string source = """
                              using CheatEngine.SDK.Annotations.Lua;

                              namespace Demo;

                              [LuaClass("Bad")]
                              public readonly partial struct Bad
                              {
                                  public global::CheatEngine.SDK.Engine.Objects.CEObject Handle => default;
                              }

                              [LuaClass("Good")]
                              public readonly partial struct Good
                              {
                              }
                              """;

        var run = roslyn.Run(source);

        Assert.Single(run.GeneratedSources);
        Assert.Equal("Demo.Good.LuaClass.g.cs", run.HintNames[0]);
        run.AssertCompilesClean();
    }

    [Fact]
    public void Generated_handle_constructor_collision_skips_only_the_affected_handle()
    {
        const string source = """
                              using CheatEngine.SDK.Annotations.Lua;

                              namespace Demo;

                              [LuaClass("Bad")]
                              public readonly partial struct Bad
                              {
                                  private Bad(global::CheatEngine.SDK.Engine.Objects.CEObject handle)
                                  {
                                      _ = handle;
                                  }
                              }

                              [LuaClass("Good")]
                              public readonly partial struct Good
                              {
                              }
                              """;

        var run = roslyn.Run(source);

        Assert.Single(run.GeneratedSources);
        Assert.Equal("Demo.Good.LuaClass.g.cs", run.HintNames[0]);
        run.AssertCompilesClean();
    }

    [Fact]
    public void Record_and_ref_like_handles_do_not_block_a_valid_sibling()
    {
        const string source = """
                              using CheatEngine.SDK.Annotations.Lua;

                              namespace Demo;

                              [LuaClass("Record")]
                              public readonly partial record struct RecordHandle;

                              [LuaClass("Ref")]
                              public readonly ref partial struct RefHandle
                              {
                              }

                              [LuaClass("Good")]
                              public readonly partial struct Good
                              {
                              }
                              """;

        var run = roslyn.Run(source);

        Assert.Single(run.GeneratedSources);
        Assert.Equal("Demo.Good.LuaClass.g.cs", run.HintNames[0]);
        run.AssertCompilesClean();
    }

    [Fact]
    public void Object_wide_arguments_and_results_preflight_the_stack_before_any_push()
    {
        const string source = """
                              using CheatEngine.SDK.Annotations.Lua;

                              namespace Demo;

                              [LuaClass("Wide")]
                              public readonly partial struct Wide
                              {
                                  [LuaMethod("sum15")]
                                  public partial bool TrySum15(
                                      int a01, int a02, int a03, int a04, int a05,
                                      int a06, int a07, int a08, int a09, int a10,
                                      int a11, int a12, int a13, int a14, int a15,
                                      out long total);

                                  [LuaMethod("fanout")]
                                  public partial bool TryFanout(
                                      int input,
                                      out int r01, out int r02, out int r03, out int r04, out int r05,
                                      out int r06, out int r07, out int r08, out int r09, out int r10,
                                      out int r11, out int r12, out int r13, out int r14, out int r15,
                                      out int r16, out int r17);
                              }
                              """;

        var run = roslyn.Run(source);
        var members = run.GeneratedText("Demo.Wide.LuaObjectMembers.g.cs");
        var stackCheck = members.IndexOf("if (!__ceState.TryEnsureStack(17))", StringComparison.Ordinal);
        var receiverPush = members.IndexOf("this.Handle.TryPushMethodLeavingObject(__ceState, \"sum15\"u8)",
            StringComparison.Ordinal);

        Assert.True(stackCheck >= 0 && stackCheck < receiverPush,
            "The object receiver, function and all arguments must be preflighted before the first push.");
        Assert.Contains(
            "return global::CheatEngine.SDK.Lua.CompilerServices.LuaCallSupport.Fail(__ceState, __ceTop, out total);",
            members, StringComparison.Ordinal);
        Assert.Contains("if (!__ceState.TryEnsureStack(18))", members, StringComparison.Ordinal);
        Assert.Contains("__ceState.TryCall(15, 1)", members, StringComparison.Ordinal);
        Assert.Contains("__ceState.TryCall(1, 17)", members, StringComparison.Ordinal);
        run.AssertCompilesClean();
    }

    [Fact]
    public void Object_try_method_defaults_every_result_when_a_marshaller_push_throws()
    {
        const string source = """
                              using CheatEngine.SDK.Annotations.Lua;

                              namespace Demo;

                              [LuaClass("Probe")]
                              public readonly partial struct Probe
                              {
                                  [LuaMethod("describe")]
                                  public partial bool TryDescribe(string input, out string? text, out int count);
                              }
                              """;

        var run = roslyn.Run(source);
        var members = run.GeneratedText("Demo.Probe.LuaObjectMembers.g.cs");
        var root = RoslynFixture.Parse(members, "Demo.Probe.LuaObjectMembers.g.cs")
            .GetCompilationUnitRoot(TestContext.Current.CancellationToken);
        var method = FindGeneratedMethod(root, "TryDescribe");
        Assert.NotNull(method.Body);
        var luaCall = FindTryStatement(method.Body!);
        var exceptionCatch = Assert.Single(luaCall.Catches);

        Assert.Contains("global::CheatEngine.SDK.Lua.Marshalling.StringMarshaller.Push(__ceState, input);", members,
            StringComparison.Ordinal);
        Assert.Contains(luaCall.Block.Statements,
            static statement => statement.ToFullString().Contains("StringMarshaller.Push(__ceState, input)",
                StringComparison.Ordinal));
        Assert.Equal("global::CheatEngine.SDK.Lua.Calls.LuaException", exceptionCatch.Declaration!.Type.ToString());
        Assert.Contains("text = default!;\n                count = default;\n                return false;", members,
            StringComparison.Ordinal);
        run.AssertCompilesClean();
    }

    [Fact]
    public void Partial_property_modifiers_and_accessor_visibility_are_preserved()
    {
        const string source = """
                              using CheatEngine.SDK.Annotations.Lua;

                              namespace Demo;

                              [LuaClass("Properties")]
                              public readonly partial struct Properties
                              {
                                  [LuaProperty("Required")]
                                  public required partial int Required { get; set; }

                                  [LuaProperty("Writable")]
                                  public partial int Writable { get; private set; }

                                  [LuaProperty("Readable")]
                                  public partial int Readable { private get; set; }
                              }
                              """;

        var run = roslyn.Run(source);
        var members = run.GeneratedText("Demo.Properties.LuaObjectMembers.g.cs");

        Assert.Contains("public required partial int Required", members, StringComparison.Ordinal);
        Assert.Contains("[global::System.Diagnostics.CodeAnalysis.SetsRequiredMembers]",
            run.GeneratedText("Demo.Properties.LuaClass.g.cs"), StringComparison.Ordinal);
        Assert.Contains("public partial int Writable", members, StringComparison.Ordinal);
        Assert.Contains("private set", members, StringComparison.Ordinal);
        Assert.Contains("public partial int Readable", members, StringComparison.Ordinal);
        Assert.Contains("private get", members, StringComparison.Ordinal);
        run.AssertCompilesClean();
    }

    [Fact]
    public void Unsupported_partial_property_forms_do_not_emit_object_members()
    {
        const string source = """
                              using CheatEngine.SDK.Annotations.Lua;

                              namespace Demo;

                              public interface IContract
                              {
                                  int Explicit { get; }
                              }

                              [LuaClass("Unsupported")]
                              public readonly partial struct Unsupported : IContract
                              {
                                  [LuaProperty("Init")]
                                  public partial int Init { get; init; }

                                  [LuaProperty("Ref")]
                                  public partial ref int Ref { get; }

                                  [LuaProperty("RefReadonly")]
                                  public partial ref readonly int RefReadonly { get; }

                                  [LuaProperty("Explicit")]
                                  partial int IContract.Explicit { get; }
                              }
                              """;

        var run = roslyn.Run(source);

        Assert.Single(run.GeneratedSources);
        Assert.Equal("Demo.Unsupported.LuaClass.g.cs", run.HintNames[0]);
    }

    private static MethodDeclarationSyntax FindGeneratedMethod(CompilationUnitSyntax root, string methodName)
    {
        MethodDeclarationSyntax? result = null;
        foreach (var node in root.DescendantNodes())
            if (node is MethodDeclarationSyntax candidate
                && string.Equals(candidate.Identifier.ValueText, methodName, StringComparison.Ordinal))
            {
                Assert.Null(result);
                result = candidate;
            }

        Assert.NotNull(result);
        return result!;
    }

    private static TryStatementSyntax FindTryStatement(BlockSyntax body)
    {
        TryStatementSyntax? result = null;
        foreach (var statement in body.Statements)
            if (statement is TryStatementSyntax candidate)
            {
                Assert.Null(result);
                result = candidate;
            }

        Assert.NotNull(result);
        return result!;
    }

    [Fact]
    public void Case_only_type_name_difference_gets_stable_distinct_class_hints()
    {
        const string source = """
                              using CheatEngine.SDK.Annotations.Lua;

                              namespace Demo;

                              [LuaClass("Upper")]
                              public readonly partial struct Case
                              {
                              }

                              [LuaClass("Lower")]
                              public readonly partial struct CASE
                              {
                              }
                              """;

        var run = roslyn.Run(source);

        Assert.Equal(2, run.HintNames.Length);
        Assert.NotEqual(run.HintNames[0], run.HintNames[1], StringComparer.OrdinalIgnoreCase);
        run.AssertCompilesClean();
    }

    [Fact]
    public void Globals_generate_without_unsafe_but_functions_remain_gated()
    {
        var globalsOnly = RoslynFixture.Run(roslyn.CreateCompilation(RoslynEnvironment.SafeCompilationOptions,
            BindingSources.Globals));
        Assert.Single(globalsOnly.GeneratedSources);
        Assert.Equal("Demo.Memory.LuaGlobals.g.cs", globalsOnly.HintNames[0]);
        globalsOnly.AssertCompilesClean();

        var mixed = RoslynFixture.Run(roslyn.CreateCompilation(RoslynEnvironment.SafeCompilationOptions,
            BindingSources.Functions, BindingSources.Globals));
        Assert.Single(mixed.GeneratedSources);
        Assert.Equal("Demo.Memory.LuaGlobals.g.cs", mixed.HintNames[0]);
    }

    private static AttributeUsageAttribute AttributeUsage(Type attributeType)
    {
        return (AttributeUsageAttribute)Attribute.GetCustomAttribute(attributeType, typeof(AttributeUsageAttribute))!;
    }
}
