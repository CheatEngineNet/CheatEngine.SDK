using CheatEngine.SDK.SourceGenerators.Shared;
using CheatEngine.SDK.SourceGenerators.Shared.LuaEmit;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.SharedCode;

/// <summary>The thunk and registration emitters over hand-built models (no Roslyn).</summary>
public sealed class LuaThunkEmitterTests
{
	private static readonly LuaThunkModel Ping = new("ping", "__LuaThunk_ping", "global::Demo.Suite.Ping",
		false, EquatableArray<LuaArgumentModel>.Empty, null);

	private static readonly LuaThunkModel IsInteger = new(
		"isint",
		"__LuaThunk_isint",
		"global::Demo.Suite.IsInteger",
		true,
		new EquatableArray<LuaArgumentModel>(
			[new LuaArgumentModel("value", LuaValueKind.Double, false)]),
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
		string text = Emit(IsInteger);

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
	public void WrongArgumentCountMessage_names_the_accepted_range()
	{
		Assert.Equal("wrong number of arguments to 'f' (1 to 3 expected)",
			LuaThunkEmitter.WrongArgumentCountMessage("f", 1, 3));
		Assert.Equal("wrong number of arguments to 'g' (0 to 1 expected)",
			LuaThunkEmitter.WrongArgumentCountMessage("g", 0, 1));
		Assert.Equal(LuaThunkEmitter.WrongArgumentCountMessage("add", 2),
			LuaThunkEmitter.WrongArgumentCountMessage("add", 2, 2));

		LuaThunkModel optional = new("f", "__LuaThunk_f", "global::Demo.Suite.F", false,
			new EquatableArray<LuaArgumentModel>([
				new LuaArgumentModel("a", LuaValueKind.Int64, false),
				LuaArgumentModel.Optional("b", LuaValueKind.String)
			]), null);
		string text = Emit(optional);
		Assert.Equal(1, optional.RequiredArgumentCount);
		Assert.Contains("if (__L.Top < 1 || __L.Top > 2)\n", text, StringComparison.Ordinal);
		Assert.Contains(
			"LuaCallSupport.TryReadOptional<string, global::CheatEngine.SDK.Lua.Marshalling.StringMarshaller>(__L, 2, out global::CheatEngine.SDK.Lua.Marshalling.LuaOptional<string> __arg1)",
			text, StringComparison.Ordinal);
		Assert.Contains("global::Demo.Suite.F(__arg0, __arg1);\n", text, StringComparison.Ordinal);
	}

	[Fact]
	public void Thunk_is_cdecl_unmanaged_callers_only_and_catches_every_exception()
	{
		foreach (LuaThunkModel model in (LuaThunkModel[]) [Ping, IsInteger])
		{
			string text = Emit(model);

			Assert.StartsWith(
				"[global::System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(global::System.Runtime.CompilerServices.CallConvCdecl) })]\nprivate static int ",
				text, StringComparison.Ordinal);
			Assert.Contains("(nint __handle)\n", text, StringComparison.Ordinal);
			Assert.Contains(
				"catch (global::System.Exception __exception)\n    {\n        return global::CheatEngine.SDK.Lua.Callbacks.LuaThunk.Fail(__L, __exception);",
				text, StringComparison.Ordinal);
			Assert.Equal(1, CountOccurrences(text, "catch ("));
			Assert.DoesNotContain("lua_error", text, StringComparison.Ordinal);
			Assert.DoesNotContain("throw", text, StringComparison.Ordinal);
		}
	}

	[Fact]
	public void Registration_emits_a_lease_and_legacy_registration_pair_in_the_given_order()
	{
		SourceWriter writer = new();
		LuaRegistrationEmitter.Emit(writer, new EquatableArray<LuaThunkModel>([IsInteger, Ping]), string.Empty);
		string text = writer.ToString();

		Assert.Contains(
			"public static unsafe global::CheatEngine.SDK.Lua.Registration.LuaRegistrationResult TryRegisterLuaFunctions(global::CheatEngine.SDK.Lua.State.LuaState state, global::CheatEngine.SDK.Lua.Registration.LuaRegistrationCollisionPolicy collisionPolicy = global::CheatEngine.SDK.Lua.Registration.LuaRegistrationCollisionPolicy.RejectExisting)\n",
			text, StringComparison.Ordinal);
		Assert.Contains(
			"new global::CheatEngine.SDK.Lua.Registration.LuaRegistrationEntry(\"isint\", new global::CheatEngine.SDK.Lua.Callbacks.LuaNativeFunction(&__LuaThunk_isint)),",
			text, StringComparison.Ordinal);
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
	public void Registration_puts_the_member_attributes_on_every_generated_registration_method()
	{
		SourceWriter writer = new();
		LuaRegistrationEmitter.Emit(writer, new EquatableArray<LuaThunkModel>([Ping]), "[Marker]");
		string text = writer.ToString();

		Assert.Contains(
			"[Marker]\npublic static unsafe global::CheatEngine.SDK.Lua.Registration.LuaRegistrationResult TryRegisterLuaFunctions",
			text, StringComparison.Ordinal);
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

	private static int CountOccurrences(string text, string value)
	{
		int count = 0;
		for (int index = text.IndexOf(value, StringComparison.Ordinal);
		     index >= 0;
		     index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
		{
			count++;
		}

		return count;
	}
}
