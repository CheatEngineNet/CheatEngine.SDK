using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Infrastructure;

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
        Assert.Contains("Handle.TryPushMethodLeavingObject(__ceState, \"firstScan\"u8)", members,
            StringComparison.Ordinal);
        Assert.Contains("__ceState.SetTop(__ceTop);", members, StringComparison.Ordinal);
        Assert.Contains("Handle.TryGetProperty<global::CheatEngine.SDK.Lua.Marshalling.Int32Marshaller, int>", members,
            StringComparison.Ordinal);
        Assert.Contains("Handle.TrySetProperty<global::CheatEngine.SDK.Lua.Marshalling.Int32Marshaller, int>", members,
            StringComparison.Ordinal);
        run.AssertCompilesClean();
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
