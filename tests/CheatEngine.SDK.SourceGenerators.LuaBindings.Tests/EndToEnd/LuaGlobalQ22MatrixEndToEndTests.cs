using System.Diagnostics.CodeAnalysis;

using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Infrastructure;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.EndToEnd;

/// <summary>
///     Qualification Q22 at C2, per form: a bound global returning <c>nil</c>, <c>false</c>, 0, <c>''</c>, <c>{}</c>, no
///     value, or raising (a string, or a table whose <c>__tostring</c> raises) stays distinguishable as each form
///     defines it, and every exit restores the stack (audit A07-09, A07-10, A07-21, A07-30 to A07-38). The stand-ins are
///     <see cref="FidelityBindingSources.Q22StandIns" />; every test asserts <c>L.Top == 0</c>.
/// </summary>
[Collection(LuaRuntimeSuite.Name)]
[Trait("Category", "NativeLua")]
public sealed class LuaGlobalQ22MatrixEndToEndTests(RoslynFixture roslyn) : IClassFixture<RoslynFixture>
{
	private const string Q22Type = "Demo.Q22";

	private static readonly string[] s_failures = ["nil", "false", "empty", "table", "none", "raise", "raise_table"];

	[Fact]
	[Trait("Qualification", "Q22")]
	public void Try_form_reports_only_a_readable_value_as_success()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		TryInt32Delegate tryInt32 = LoadSuite(roslyn).Delegate<TryInt32Delegate>(Q22Type, "TryInt32");

		Assert.False(tryInt32("value", out int unresolved)); // the global is not defined yet
		Assert.Equal(0, unresolved);
		LuaTest.Run(L, FidelityBindingSources.Q22StandIns);

		Assert.True(tryInt32("value", out int value));
		Assert.Equal(42, value);
		Assert.True(tryInt32("zero", out int zero));
		Assert.Equal(0, zero);
		foreach (string kind in s_failures)
		{
			Assert.False(tryInt32(kind, out int failed), kind);
			Assert.Equal(0, failed);
			Assert.Equal(0, L.Top);
		}

		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q22")]
	public void Try_form_boolean_result_false_is_a_successful_call_not_a_failure()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, FidelityBindingSources.Q22StandIns);
		TryBooleanDelegate tryBoolean = LoadSuite(roslyn).Delegate<TryBooleanDelegate>(Q22Type, "TryBoolean");

		// The Try form's bool is the binding's success; the Lua value travels in the out parameter.
		Assert.True(tryBoolean("false", out bool isFalse));
		Assert.False(isFalse);
		Assert.True(tryBoolean("true", out bool isTrue));
		Assert.True(isTrue);

		// Booleans are strict: nil, 0 and '' are not booleans, and a raise is a failed call.
		Assert.False(tryBoolean("nil", out bool nil));
		Assert.False(nil);
		Assert.False(tryBoolean("zero", out _));
		Assert.False(tryBoolean("empty", out _));
		Assert.False(tryBoolean("none", out _));
		Assert.False(tryBoolean("raise", out _));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q22")]
	public void Outcome_form_classifies_nil_false_zero_empty_table_none_and_raise_distinctly()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		OutcomeInt32Delegate outcome = LoadSuite(roslyn).Delegate<OutcomeInt32Delegate>(Q22Type, "OutcomeInt32");
		Assert.Equal(LuaOperationStatusKind.GlobalUnavailable, outcome("value", out _).Kind);
		LuaTest.Run(L, FidelityBindingSources.Q22StandIns);

		AssertOutcome(LuaOperationStatusKind.NilResult, LuaStatus.Ok, outcome("nil", out _));
		AssertOutcome(LuaOperationStatusKind.InvalidResult, LuaStatus.Ok, outcome("false", out _));
		AssertOutcome(LuaOperationStatusKind.InvalidResult, LuaStatus.Ok, outcome("empty", out _));
		AssertOutcome(LuaOperationStatusKind.InvalidResult, LuaStatus.Ok, outcome("table", out _));
		AssertOutcome(LuaOperationStatusKind.Success, LuaStatus.Ok, outcome("zero", out int zero));
		Assert.Equal(0, zero);

		// A fixed-count binding asks Lua for exactly one result: a global that returns nothing reads as nil there.
		AssertOutcome(LuaOperationStatusKind.NilResult, LuaStatus.Ok, outcome("none", out _));
		AssertOutcome(LuaOperationStatusKind.LuaFailure, LuaStatus.RuntimeError, outcome("raise", out int raised));
		Assert.Equal(0, raised);
		AssertOutcome(LuaOperationStatusKind.LuaFailure, LuaStatus.RuntimeError, outcome("raise_table", out _));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q22")]
	public void Optional_result_outcome_form_distinguishes_zero_results_from_nil()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, FidelityBindingSources.Q22StandIns);
		OutcomeOptionalDelegate outcome =
			LoadSuite(roslyn).Delegate<OutcomeOptionalDelegate>(Q22Type, "OutcomeOptional");

		AssertOutcome(LuaOperationStatusKind.Success, LuaStatus.Ok, outcome("none", out LuaOptional<int> none));
		Assert.True(none.IsOmitted);
		AssertOutcome(LuaOperationStatusKind.Success, LuaStatus.Ok, outcome("nil", out LuaOptional<int> nil));
		Assert.True(nil.IsNil);
		AssertOutcome(LuaOperationStatusKind.Success, LuaStatus.Ok, outcome("zero", out LuaOptional<int> zero));
		Assert.Equal(LuaOptional.Of(0), zero);
		AssertOutcome(LuaOperationStatusKind.InvalidResult, LuaStatus.Ok, outcome("false", out LuaOptional<int> no));
		Assert.True(no.IsOmitted);
		AssertOutcome(LuaOperationStatusKind.LuaFailure, LuaStatus.RuntimeError,
			outcome("raise", out LuaOptional<int> raised));
		Assert.True(raised.IsOmitted);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q22")]
	public void Empty_string_result_is_a_value_not_nil()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, FidelityBindingSources.Q22StandIns);
		GeneratedAssembly assembly = LoadSuite(roslyn);
		TryTextDelegate tryText = assembly.Delegate<TryTextDelegate>(Q22Type, "TryText");
		TryTextCopyDelegate copyOut = assembly.Delegate<TryTextCopyDelegate>(Q22Type, "TryTextCopy");
		TextDelegate text = assembly.Delegate<TextDelegate>(Q22Type, "Text");
		Span<byte> buffer = stackalloc byte[8];

		Assert.True(tryText("empty", out string? empty));
		Assert.Equal(string.Empty, empty);
		Assert.True(copyOut("empty", buffer, out int written));
		Assert.Equal(0, written);
		Assert.Equal(string.Empty, text("empty"));

		Assert.False(tryText("nil", out string? nil));
		Assert.Null(nil);
		Assert.False(copyOut("nil", buffer, out int nothing));
		Assert.Equal(0, nothing);
		Assert.False(tryText("zero", out _)); // a number is not a string: no numeric coercion
		Assert.Equal("The Lua global 'q22' returned a nil value, not a string.",
			Assert.Throws<LuaException>(() => text("nil")).Message);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q22")]
	public void Throwing_form_raises_a_distinct_message_per_exit_and_restores_the_stack()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		GeneratedAssembly assembly = LoadSuite(roslyn);
		Int32Delegate int32 = assembly.Delegate<Int32Delegate>(Q22Type, "Int32");
		BooleanDelegate boolean = assembly.Delegate<BooleanDelegate>(Q22Type, "Boolean");

		Assert.Equal("The Lua global 'q22' is undefined or is not a function.",
			Assert.Throws<LuaException>(() => int32("value")).Message);
		Assert.Equal(0, L.Top);
		LuaTest.Run(L, FidelityBindingSources.Q22StandIns);

		Assert.Equal(0, int32("zero"));
		Assert.False(boolean("false"));
		LuaException raised = Assert.Throws<LuaException>(() => int32("raise"));
		Assert.Equal(LuaStatus.RuntimeError, raised.Status);
		Assert.Contains("boom", raised.Message, StringComparison.Ordinal);
		Assert.Equal(0, L.Top);

		AssertUnexpected("nil value, not an integer", () => int32("nil"));
		AssertUnexpected("nil value, not an integer", () => int32("none"));
		AssertUnexpected("boolean value, not an integer", () => int32("false"));
		AssertUnexpected("string value, not an integer", () => int32("empty"));
		AssertUnexpected("table value, not an integer", () => int32("table"));
		AssertUnexpected("nil value, not a boolean", () => boolean("nil"));
		AssertUnexpected("number value, not a boolean", () => boolean("zero"));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q22")]
	public void Throwing_form_keeps_the_runtime_error_status_when_the_error_object_tostring_raises()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, FidelityBindingSources.Q22StandIns);
		GeneratedAssembly assembly = LoadSuite(roslyn);
		Int32Delegate int32 = assembly.Delegate<Int32Delegate>(Q22Type, "Int32");
		OutcomeInt32Delegate outcome = assembly.Delegate<OutcomeInt32Delegate>(Q22Type, "OutcomeInt32");

		// The error object is described by its type: no metamethod runs, so '__tostring' cannot raise a second error.
		LuaException raised = Assert.Throws<LuaException>(() => int32("raise_table"));
		Assert.Equal(LuaStatus.RuntimeError, raised.Status);
		Assert.Equal("(error object is a table value)", raised.Message);
		Assert.Equal(0, L.Top);
		AssertOutcome(LuaOperationStatusKind.LuaFailure, LuaStatus.RuntimeError, outcome("raise_table", out _));
		Assert.Equal(42, int32("value"));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q22")]
	public void Next_call_on_the_same_state_succeeds_after_every_failure_kind()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, FidelityBindingSources.Q22StandIns);
		GeneratedAssembly assembly = LoadSuite(roslyn);
		TryInt32Delegate tryInt32 = assembly.Delegate<TryInt32Delegate>(Q22Type, "TryInt32");
		OutcomeInt32Delegate outcome = assembly.Delegate<OutcomeInt32Delegate>(Q22Type, "OutcomeInt32");
		Int32Delegate int32 = assembly.Delegate<Int32Delegate>(Q22Type, "Int32");
		OutcomeEvenDelegate even = assembly.Delegate<OutcomeEvenDelegate>(Q22Type, "OutcomeEven");

		foreach (string kind in s_failures)
		{
			Assert.False(tryInt32(kind, out _));
			Assert.True(tryInt32("value", out int afterTry), kind);
			Assert.False(outcome(kind, out _).IsSuccess);
			Assert.Equal(LuaOperationStatusKind.Success, outcome("value", out int afterOutcome).Kind);
			Assert.Throws<LuaException>(() => int32(kind));
			Assert.Equal(42, int32("value"));
			Assert.Equal(afterTry, afterOutcome);
			Assert.Equal(0, L.Top);
		}

		Assert.Throws<InvalidOperationException>(() => even("negative", out _));
		Assert.Equal(LuaOperationStatusKind.Success, even("zero", out long recovered).Kind);
		Assert.Equal(0, recovered);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q22")]
	public void Custom_marshaller_failure_after_a_successful_call_is_invalid_result_not_lua_failure()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, FidelityBindingSources.Q22StandIns);
		GeneratedAssembly assembly = LoadSuite(roslyn);
		TryEvenDelegate tryEven = assembly.Delegate<TryEvenDelegate>(Q22Type, "TryEven");
		OutcomeEvenDelegate outcome = assembly.Delegate<OutcomeEvenDelegate>(Q22Type, "OutcomeEven");
		EvenDelegate even = assembly.Delegate<EvenDelegate>(Q22Type, "Even");

		// The call succeeded (LuaStatus.Ok); only the marshaller refused 7.
		AssertOutcome(LuaOperationStatusKind.InvalidResult, LuaStatus.Ok, outcome("odd", out long odd));
		Assert.Equal(0, odd);
		AssertOutcome(LuaOperationStatusKind.NilResult, LuaStatus.Ok, outcome("nil", out _));
		AssertOutcome(LuaOperationStatusKind.Success, LuaStatus.Ok, outcome("value", out long value));
		Assert.Equal(42, value);
		Assert.False(tryEven("odd", out _));
		Assert.True(tryEven("zero", out long zero));
		Assert.Equal(0, zero);
		LuaException unexpected = Assert.Throws<LuaException>(() => even("odd"));
		Assert.Equal(LuaStatus.Ok, unexpected.Status);
		Assert.StartsWith("The Lua global 'q22' returned a number value, not ", unexpected.Message,
			StringComparison.Ordinal);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q22")]
	public void Custom_marshaller_exception_after_a_successful_call_propagates_with_the_stack_restored()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, FidelityBindingSources.Q22StandIns);
		GeneratedAssembly assembly = LoadSuite(roslyn);
		TryEvenDelegate tryEven = assembly.Delegate<TryEvenDelegate>(Q22Type, "TryEven");
		OutcomeEvenDelegate outcome = assembly.Delegate<OutcomeEvenDelegate>(Q22Type, "OutcomeEven");
		EvenDelegate even = assembly.Delegate<EvenDelegate>(Q22Type, "Even");

		// A marshaller's own exception is not a Lua failure: no form swallows or reclassifies it.
		Assert.Equal("negative token", Assert.Throws<InvalidOperationException>(() => tryEven("negative", out _)).Message);
		Assert.Equal(0, L.Top);
		Assert.Throws<InvalidOperationException>(() => outcome("negative", out _));
		Assert.Equal(0, L.Top);
		Assert.Throws<InvalidOperationException>(() => even("negative"));
		Assert.Equal(0, L.Top);
		Assert.Equal(42, even("value"));
		Assert.Equal(0, L.Top);
	}

	private static void AssertOutcome(LuaOperationStatusKind kind, LuaStatus luaStatus, LuaOperationStatus actual)
	{
		Assert.Equal(kind, actual.Kind);
		Assert.Equal(luaStatus, actual.LuaStatus);
	}

	private static void AssertUnexpected(string description, Action call)
	{
		LuaException exception = Assert.Throws<LuaException>(call);
		Assert.Equal("The Lua global 'q22' returned a " + description + ".", exception.Message);
		Assert.Equal(LuaStatus.Ok, exception.Status);
	}

	private static GeneratedAssembly LoadSuite(RoslynFixture roslyn)
	{
		return GeneratedAssembly.Load(roslyn.Run(FidelityBindingSources.Q22Suite));
	}

	private delegate bool TryInt32Delegate(string kind, out int value);

	private delegate bool TryBooleanDelegate(string kind, out bool value);

	private delegate bool TryTextDelegate(string kind, [MaybeNullWhen(false)] out string value);

	private delegate bool TryTextCopyDelegate(string kind, Span<byte> destination, out int written);

	private delegate LuaOperationStatus OutcomeInt32Delegate(string kind, out int value);

	private delegate LuaOperationStatus OutcomeOptionalDelegate(string kind, out LuaOptional<int> value);

	private delegate int Int32Delegate(string kind);

	private delegate bool BooleanDelegate(string kind);

	private delegate string TextDelegate(string kind);

	private delegate bool TryEvenDelegate(string kind, out long value);

	private delegate LuaOperationStatus OutcomeEvenDelegate(string kind, out long value);

	private delegate long EvenDelegate(string kind);
}
