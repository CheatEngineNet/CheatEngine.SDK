using CheatEngine.SDK.Analyzers.Generation;
using CheatEngine.SDK.Analyzers.Tests.Infrastructure;

namespace CheatEngine.SDK.Analyzers.Tests.Generation;

/// <summary>CESDK2006 and CESDK2007: unsupported Lua object declarations and source collisions with generated members.</summary>
public sealed class LuaObjectBindingAnalyzerTests
{
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
}
