using CheatEngine.SDK.SourceGenerators.Shared;
using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.SharedCode;

/// <summary>The thunk and registration emitters over hand-built models (no Roslyn).</summary>
public sealed class LuaThunkEmitterTests
{
    private static readonly LuaThunkModel Ping = new("ping", "__LuaThunk_ping", "global::Demo.Suite.Ping",
        PassesState: false, EquatableArray<LuaArgumentModel>.Empty, ReturnKind: null);

    private static readonly LuaThunkModel IsInteger = new(
        "isint",
        "__LuaThunk_isint",
        "global::Demo.Suite.IsInteger",
        PassesState: true,
        new EquatableArray<LuaArgumentModel>(
            [new LuaArgumentModel("value", LuaValueKind.Double, IsNullable: false)]),
        LuaValueKind.Boolean);

    [Fact]
    public void Emit_void_target_without_arguments_checks_the_count_and_returns_zero()
    {
        Assert.Equal(
            """
                [global::System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(global::System.Runtime.CompilerServices.CallConvCdecl) })]
                private static int __LuaThunk_ping(nint __handle)
                {
                    global::CheatEngine.SDK.Lua.State.LuaState __L = new(__handle);
                    try
                    {
                        if (__L.Top != 0)
                        {
                            return global::CheatEngine.SDK.Lua.Callbacks.LuaThunk.Fail(__L, "wrong number of arguments to 'ping' (0 expected)"u8);
                        }

                        global::Demo.Suite.Ping();
                        return 0;
                    }
                    catch (global::System.Exception __exception)
                    {
                        return global::CheatEngine.SDK.Lua.Callbacks.LuaThunk.Fail(__L, __exception);
                    }
                }

                """.ReplaceLineEndings("\n"),
            Emit(Ping));
    }

    [Fact]
    public void Emit_state_passing_target_passes_the_state_first_and_pushes_the_result()
    {
        var text = Emit(IsInteger);

        Assert.Contains("if (__L.Top != 1)\n", text, StringComparison.Ordinal);
        Assert.Contains(
            "if (!global::CheatEngine.SDK.Lua.Marshalling.DoubleMarshaller.TryRead(__L, 1, out double __arg0))\n",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "return global::CheatEngine.SDK.Lua.Callbacks.LuaThunk.FailBadArgument(__L, 1, \"number\"u8);\n", text,
            StringComparison.Ordinal);
        Assert.Contains("bool __result = global::Demo.Suite.IsInteger(__L, __arg0);\n", text, StringComparison.Ordinal);
        Assert.Contains(
            "global::CheatEngine.SDK.Lua.Marshalling.BooleanMarshaller.Push(__L, __result);\n        return 1;\n",
            text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WrongArgumentCountMessage_names_the_function_and_the_count()
    {
        Assert.Equal("wrong number of arguments to 'add' (2 expected)",
            LuaThunkEmitter.WrongArgumentCountMessage("add", 2));
        Assert.Equal("__LuaThunk_add", LuaThunkModel.ThunkNameFor("add"));
    }

    [Fact]
    public void Registration_registers_and_unregisters_in_the_given_order()
    {
        SourceWriter writer = new();
        LuaRegistrationEmitter.Emit(writer, new EquatableArray<LuaThunkModel>([IsInteger, Ping]), string.Empty);
        var text = writer.ToString();

        Assert.Contains(
            "public static unsafe global::CheatEngine.SDK.Lua.Calls.LuaStatus RegisterLuaFunctions(global::CheatEngine.SDK.Lua.State.LuaState state)\n",
            text, StringComparison.Ordinal);
        Assert.Contains(
            "public static global::CheatEngine.SDK.Lua.Calls.LuaStatus UnregisterLuaFunctions(global::CheatEngine.SDK.Lua.State.LuaState state)\n",
            text, StringComparison.Ordinal);
        Assert.Contains(
            "global::CheatEngine.SDK.Lua.Runtime.LuaRuntime.TryPushGeneratedFunction(state, new global::CheatEngine.SDK.Lua.Callbacks.LuaNativeFunction(&__LuaThunk_isint));",
            text,
            StringComparison.Ordinal);
        Assert.True(
            text.IndexOf("TrySetGlobal(\"isint\"u8)", StringComparison.Ordinal) <
            text.IndexOf("TrySetGlobal(\"ping\"u8)", StringComparison.Ordinal),
            "The order of the model was not kept.");
        Assert.Contains("<c>isint</c>, <c>ping</c>.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("GeneratedCode", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Registration_puts_the_member_attributes_on_both_methods()
    {
        SourceWriter writer = new();
        LuaRegistrationEmitter.Emit(writer, new EquatableArray<LuaThunkModel>([Ping]), "[Marker]");
        var text = writer.ToString();

        Assert.Contains(
            "[Marker]\npublic static unsafe global::CheatEngine.SDK.Lua.Calls.LuaStatus RegisterLuaFunctions", text,
            StringComparison.Ordinal);
        Assert.Contains("[Marker]\npublic static global::CheatEngine.SDK.Lua.Calls.LuaStatus UnregisterLuaFunctions",
            text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Registration_rejects_an_empty_table_and_null_writer()
    {
        Assert.Throws<ArgumentException>(() =>
            LuaRegistrationEmitter.Emit(new SourceWriter(), EquatableArray<LuaThunkModel>.Empty, string.Empty));
        Assert.Throws<ArgumentNullException>(() =>
            LuaRegistrationEmitter.Emit(null!, new EquatableArray<LuaThunkModel>([Ping]), string.Empty));
        Assert.Throws<ArgumentNullException>(() => LuaThunkEmitter.Emit(null!, Ping));
        Assert.Throws<ArgumentNullException>(() => LuaThunkEmitter.Emit(new SourceWriter(), null!));
    }

    private static string Emit(LuaThunkModel model)
    {
        SourceWriter writer = new();
        LuaThunkEmitter.Emit(writer, model);
        return writer.ToString();
    }
}
