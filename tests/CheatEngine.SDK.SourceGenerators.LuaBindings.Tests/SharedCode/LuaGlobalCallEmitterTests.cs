using CheatEngine.SDK.SourceGenerators.Shared;
using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.SharedCode;

/// <summary>
///     The call-shape emitter over hand-built models (no Roslyn): the text a spec-driven generator (EngineApi) gets
///     for a complete, non-partial wrapper, and the signature helpers it composes declarations from.
/// </summary>
public sealed class LuaGlobalCallEmitterTests
{
    [Fact]
    public void Emit_try_form_with_one_result_writes_the_exact_call_shape()
    {
        LuaGlobalCallModel model = new(
            "readInteger",
            "s_luaGlobal_readInteger",
            "public static",
            "TryReadInt32",
            string.Empty,
            new EquatableArray<LuaArgumentModel>([new LuaArgumentModel("address", LuaValueKind.Address, false)]),
            LuaCallForm.Try,
            new EquatableArray<LuaResultModel>([LuaResultModel.Value(LuaValueKind.Int32, "value")]),
            null,
            false);

        Assert.Equal(
            """
                public static bool TryReadInt32(nuint address, out int value)
                {
                    using global::CheatEngine.SDK.Lua.Runtime.LuaRuntimeOperation __operation = global::CheatEngine.SDK.Lua.Runtime.LuaRuntime.AcquireOperation();
                    global::CheatEngine.SDK.Lua.State.LuaState __L = __operation.State;
                    int __top = __L.Top;
                    try
                    {
                        if (!global::CheatEngine.SDK.Lua.CompilerServices.LuaGlobalFunctions.TryPush(__L, s_luaGlobal_readInteger, "readInteger"u8))
                        {
                            return global::CheatEngine.SDK.Lua.CompilerServices.LuaCallSupport.Fail(__L, __top, out value);
                        }

                        global::CheatEngine.SDK.Lua.Marshalling.AddressMarshaller.Push(__L, address);
                        if (!__L.TryCall(1, 1).IsOk)
                        {
                            return global::CheatEngine.SDK.Lua.CompilerServices.LuaCallSupport.Fail(__L, __top, out value);
                        }

                        bool __ok = global::CheatEngine.SDK.Lua.Marshalling.Int32Marshaller.TryRead(__L, -1, out value);
                        return __ok;
                    }
                    catch (global::CheatEngine.SDK.Lua.Calls.LuaException)
                    {
                        value = default;
                        return false;
                    }
                    finally
                    {
                        __L.SetTop(__top);
                    }
                }

                """.ReplaceLineEndings("\n"),
            Emit(model));
    }

    [Fact]
    public void Emit_throwing_void_form_keeps_no_result()
    {
        LuaGlobalCallModel model = new(
            "beep",
            "s_luaGlobal_beep",
            "internal static",
            "Beep",
            string.Empty,
            EquatableArray<LuaArgumentModel>.Empty,
            LuaCallForm.Throwing,
            EquatableArray<LuaResultModel>.Empty,
            null,
            false);

        Assert.Equal(
            """
                internal static void Beep()
                {
                    using global::CheatEngine.SDK.Lua.Runtime.LuaRuntimeOperation __operation = global::CheatEngine.SDK.Lua.Runtime.LuaRuntime.AcquireOperation();
                    global::CheatEngine.SDK.Lua.State.LuaState __L = __operation.State;
                    int __top = __L.Top;
                    try
                    {
                        if (!global::CheatEngine.SDK.Lua.CompilerServices.LuaGlobalFunctions.TryPush(__L, s_luaGlobal_beep, "beep"u8))
                        {
                            global::CheatEngine.SDK.Lua.CompilerServices.LuaCallSupport.ThrowUnresolvedGlobal(__L, __top, "beep");
                        }

                        global::CheatEngine.SDK.Lua.Calls.LuaStatus __status = __L.TryCall(0, 0);
                        if (!__status.IsOk)
                        {
                            global::CheatEngine.SDK.Lua.CompilerServices.LuaCallSupport.Throw(__L, __top, __status);
                        }
                    }
                    finally
                    {
                        __L.SetTop(__top);
                    }
                }

                """.ReplaceLineEndings("\n"),
            Emit(model));
    }

    [Fact]
    public void Emit_throwing_string_form_reads_a_nullable_local_and_names_the_expected_kind()
    {
        LuaGlobalCallModel model = new(
            "readString",
            "s_luaGlobal_readString",
            "public static",
            "ReadString",
            "L",
            new EquatableArray<LuaArgumentModel>([
                new LuaArgumentModel("address", LuaValueKind.Address, false),
                new LuaArgumentModel("text", LuaValueKind.String, true)
            ]),
            LuaCallForm.Throwing,
            EquatableArray<LuaResultModel>.Empty,
            LuaValueKind.String,
            true);

        var text = Emit(model);
        Assert.StartsWith(
            "public static string? ReadString(global::CheatEngine.SDK.Lua.State.LuaState L, nuint address, string? text)\n", text,
            StringComparison.Ordinal);
        Assert.Contains(
            "using global::CheatEngine.SDK.Lua.Runtime.LuaRuntimeOperation __operation = global::CheatEngine.SDK.Lua.Runtime.LuaRuntime.AcquireOperation(L);\n",
            text,
            StringComparison.Ordinal);
        Assert.Contains("global::CheatEngine.SDK.Lua.State.LuaState __L = __operation.State;\n", text,
            StringComparison.Ordinal);
        Assert.Contains("global::CheatEngine.SDK.Lua.Marshalling.StringMarshaller.Push(__L, text);\n", text,
            StringComparison.Ordinal);
        Assert.Contains("if (!global::CheatEngine.SDK.Lua.Marshalling.StringMarshaller.TryRead(__L, -1, out string? __result))\n",
            text,
            StringComparison.Ordinal);
        Assert.Contains("ThrowUnexpectedResult(__L, __top, -1, \"readString\", \"a string\");", text,
            StringComparison.Ordinal);
        Assert.Contains("return __result;\n    }\n    finally\n    {\n        __L.SetTop(__top);", text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WriteParameterList_writes_state_arguments_and_copy_out_results()
    {
        LuaGlobalCallModel model = new(
            "readString",
            "s_luaGlobal_readString",
            "public static partial",
            "TryReadString",
            "state",
            new EquatableArray<LuaArgumentModel>([
                new LuaArgumentModel("address", LuaValueKind.Address, false),
                new LuaArgumentModel("maxLength", LuaValueKind.Int32, false)
            ]),
            LuaCallForm.Try,
            new EquatableArray<LuaResultModel>([
                LuaResultModel.CopyOut("destination", "written"),
                LuaResultModel.Value(LuaValueKind.String, "text", true)
            ]),
            null,
            false);

        SourceWriter writer = new();
        LuaGlobalCallEmitter.WriteParameterList(writer, model);

        Assert.Equal(
            "(global::CheatEngine.SDK.Lua.State.LuaState state, nuint address, int maxLength, global::System.Span<byte> destination, out int written, out string? text)",
            writer.ToString());
        Assert.Equal("bool", LuaGlobalCallEmitter.ReturnTypeName(model));
        Assert.Equal(2, model.ResultCount);
        Assert.True(model.TakesState);
    }

    [Fact]
    public void ResultCount_follows_the_form()
    {
        LuaGlobalCallModel throwingVoid = new("g", "s", "static", "G", string.Empty,
            EquatableArray<LuaArgumentModel>.Empty, LuaCallForm.Throwing, EquatableArray<LuaResultModel>.Empty, null,
            false);
        var throwingValue = throwingVoid with { ReturnKind = LuaValueKind.Double };

        Assert.Equal(0, throwingVoid.ResultCount);
        Assert.Equal(1, throwingValue.ResultCount);
        Assert.Equal("void", LuaGlobalCallEmitter.ReturnTypeName(throwingVoid));
        Assert.Equal("double", LuaGlobalCallEmitter.ReturnTypeName(throwingValue));
        Assert.Equal("s_luaGlobal_readInteger", LuaGlobalCallModel.CacheFieldFor("readInteger"));
    }

    [Fact]
    public void Emit_rejects_null_arguments()
    {
        LuaGlobalCallModel model = new("g", "s", "static", "G", string.Empty, EquatableArray<LuaArgumentModel>.Empty,
            LuaCallForm.Throwing, EquatableArray<LuaResultModel>.Empty, null, false);

        Assert.Throws<ArgumentNullException>(() => LuaGlobalCallEmitter.Emit(null!, model));
        Assert.Throws<ArgumentNullException>(() => LuaGlobalCallEmitter.Emit(new SourceWriter(), null!));
    }

    private static string Emit(LuaGlobalCallModel model)
    {
        SourceWriter writer = new();
        LuaGlobalCallEmitter.Emit(writer, model);
        return writer.ToString();
    }
}
