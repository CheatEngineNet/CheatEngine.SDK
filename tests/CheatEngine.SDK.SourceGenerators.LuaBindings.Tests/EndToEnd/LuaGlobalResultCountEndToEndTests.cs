using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.Infrastructure;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.SourceGenerators.LuaBindings.Tests.EndToEnd;

/// <summary>
///     Generated wrappers with optional and variadic results read the factual number of values a global returned
///     (<c>LUA_MULTRET</c>): zero results are not <c>nil</c>, a missing required result is
///     <see cref="LuaOperationStatusKind.MissingResult" />, extra values are ignored by a scalar binding, and a variadic
///     tail is copied or refused with the needed capacity (audit A19-08, A07-10, A07-14, A07-26, A06-09, Q22; spike C3
///     D1 and D5 shapes). Every test asserts <c>L.Top == 0</c>.
/// </summary>
[Collection(LuaRuntimeSuite.Name)]
[Trait("Category", "NativeLua")]
public sealed class LuaGlobalResultCountEndToEndTests(RoslynFixture roslyn) : IClassFixture<RoslynFixture>
{
	private const string OptionalsType = "Demo.Optionals";

	[Fact]
	[Trait("Qualification", "Q22")]
	public void Zero_results_and_nil_are_distinct_for_an_optional_result()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, OptionalBindingSources.StandIns);
		ShapeOptionalDelegate shape = LoadSuite(roslyn).Delegate<ShapeOptionalDelegate>(OptionalsType, "ShapeOptional");

		// Mode 0 returns no value at all (the AOBScan zero-match shape of spike C3 D1); mode 1 returns an explicit nil.
		Assert.Equal(LuaOperationStatusKind.Success, shape(0, out LuaOptional<long> none).Kind);
		Assert.True(none.IsOmitted);
		Assert.Equal(LuaOperationStatusKind.Success, shape(1, out LuaOptional<long> nil).Kind);
		Assert.True(nil.IsNil);
		Assert.Equal(LuaOperationStatusKind.Success, shape(2, out LuaOptional<long> value).Kind);
		Assert.Equal(LuaOptional.Of(7L), value);
		Assert.Equal(LuaOperationStatusKind.InvalidResult, shape(6, out LuaOptional<long> text).Kind);
		Assert.True(text.IsOmitted);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q22")]
	public void Missing_required_result_before_an_optional_one_is_missing_not_nil()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, OptionalBindingSources.StandIns);
		GeneratedAssembly assembly = LoadSuite(roslyn);
		ShapePairDelegate detailed = assembly.Delegate<ShapePairDelegate>(OptionalsType, "ShapeRequiredThenOptional");
		TryShapePairDelegate tryShape =
			assembly.Delegate<TryShapePairDelegate>(OptionalsType, "TryShapeRequiredThenOptional");

		Assert.Equal(LuaOperationStatusKind.MissingResult, detailed(0, out long missing, out _).Kind);
		Assert.Equal(0, missing);
		Assert.Equal(LuaOperationStatusKind.NilResult, detailed(1, out _, out _).Kind);
		Assert.Equal(LuaOperationStatusKind.Success, detailed(2, out long onlyFirst, out LuaOptional<long> absent).Kind);
		Assert.Equal(7, onlyFirst);
		Assert.True(absent.IsOmitted);
		Assert.Equal(LuaOperationStatusKind.Success, detailed(3, out long first, out LuaOptional<long> second).Kind);
		Assert.Equal(7, first);
		Assert.Equal(LuaOptional.Of(8L), second);
		Assert.Equal(LuaOperationStatusKind.Success, detailed(8, out _, out LuaOptional<long> explicitNil).Kind);
		Assert.True(explicitNil.IsNil);
		Assert.Equal(LuaOperationStatusKind.InvalidResult, detailed(7, out long defaulted, out LuaOptional<long> bad).Kind);
		Assert.Equal(0, defaulted);
		Assert.True(bad.IsOmitted);

		Assert.False(tryShape(0, out _, out _));
		Assert.True(tryShape(3, out long tryFirst, out LuaOptional<long> trySecond));
		Assert.Equal(7, tryFirst);
		Assert.Equal(LuaOptional.Of(8L), trySecond);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q22")]
	public void Second_result_is_kept_when_the_first_is_a_boolean()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, OptionalBindingSources.StandIns);
		FlagAndWarningDelegate shape =
			LoadSuite(roslyn).Delegate<FlagAndWarningDelegate>(OptionalsType, "ShapeFlagAndWarning");

		Assert.Equal(LuaOperationStatusKind.Success, shape(4, out bool ok, out LuaOptional<string> warning).Kind);
		Assert.True(ok);
		Assert.Equal(LuaOptional.Of("warn"), warning);
		Assert.Equal(LuaOperationStatusKind.InvalidResult, shape(2, out bool notBoolean, out _).Kind);
		Assert.False(notBoolean);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Extra_non_scalar_results_are_ignored_by_a_scalar_binding()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, OptionalBindingSources.StandIns);
		GeneratedAssembly assembly = LoadSuite(roslyn);
		TryShapeScalarDelegate scalar = assembly.Delegate<TryShapeScalarDelegate>(OptionalsType, "TryShapeScalar");
		ShapeOptionalDelegate optional = assembly.Delegate<ShapeOptionalDelegate>(OptionalsType, "ShapeOptional");

		// Mode 5 returns (integer, table), the getCheatEngineFileVersion shape of spike C3 D5.
		Assert.True(scalar(5, out long packed));
		Assert.Equal(1970354901756285, packed);
		Assert.Equal(LuaOperationStatusKind.Success, optional(5, out LuaOptional<long> first).Kind);
		Assert.Equal(LuaOptional.Of(1970354901756285L), first);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q22")]
	public void Variadic_result_copies_every_value_and_reports_the_count()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, OptionalBindingSources.StandIns);
		GeneratedAssembly assembly = LoadSuite(roslyn);
		SequenceDelegate sequence = assembly.Delegate<SequenceDelegate>(OptionalsType, "Sequence");
		SequenceAfterOneDelegate afterOne = assembly.Delegate<SequenceAfterOneDelegate>(OptionalsType, "SequenceAfterOne");
		Span<long> values = stackalloc long[8];

		Assert.Equal(LuaOperationStatusKind.Success, sequence(3, default, values, out int count).Kind);
		Assert.Equal(3, count);
		Assert.Equal([10L, 20L, 30L], values[..count].ToArray());
		Assert.Equal(LuaOperationStatusKind.Success, sequence(0, default, values, out int none).Kind);
		Assert.Equal(0, none);

		Assert.Equal(LuaOperationStatusKind.Success,
			afterOne(3, out long first, out LuaOptional<long> second, values, out int restCount).Kind);
		Assert.Equal(10, first);
		Assert.Equal(LuaOptional.Of(20L), second);
		Assert.Equal(1, restCount);
		Assert.Equal(30, values[0]);
		Assert.Equal(LuaOperationStatusKind.Success,
			afterOne(1, out long single, out LuaOptional<long> noSecond, values, out int noRest).Kind);
		Assert.Equal(10, single);
		Assert.True(noSecond.IsOmitted);
		Assert.Equal(0, noRest);
		Assert.Equal(LuaOperationStatusKind.MissingResult, afterOne(0, out _, out _, values, out int missing).Kind);
		Assert.Equal(0, missing);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q22")]
	public void Variadic_result_above_capacity_is_refused_with_the_needed_count_and_no_stack_residue()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, OptionalBindingSources.StandIns);
		GeneratedAssembly assembly = LoadSuite(roslyn);
		SequenceDelegate sequence = assembly.Delegate<SequenceDelegate>(OptionalsType, "Sequence");
		SequenceAfterOneDelegate afterOne = assembly.Delegate<SequenceAfterOneDelegate>(OptionalsType, "SequenceAfterOne");
		Span<long> small = stackalloc long[2];

		Assert.Equal(LuaOperationStatusKind.ResultCapacityExceeded, sequence(5, default, small, out int needed).Kind);
		Assert.Equal(5, needed);
		Assert.Equal([0L, 0L], small.ToArray());
		Assert.Equal(LuaOperationStatusKind.ResultCapacityExceeded,
			afterOne(6, out long first, out LuaOptional<long> second, small, out int restNeeded).Kind);
		Assert.Equal(4, restNeeded);
		Assert.Equal(0, first);
		Assert.True(second.IsOmitted);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q22")]
	public void Variadic_nil_or_wrong_kind_element_is_classified_without_reading_error_text()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, OptionalBindingSources.StandIns);
		SequenceDelegate sequence = LoadSuite(roslyn).Delegate<SequenceDelegate>(OptionalsType, "Sequence");
		Span<long> values = stackalloc long[8];

		LuaOperationStatus nil = sequence(0, LuaOptional.Of(1), values, out int nilCount);
		Assert.Equal(LuaOperationStatusKind.NilResult, nil.Kind);
		Assert.Equal(LuaStatus.Ok, nil.LuaStatus);
		Assert.Equal(0, nilCount);
		Assert.Equal(0, values[0]);
		LuaOperationStatus text = sequence(0, LuaOptional.Of(2), values, out int textCount);
		Assert.Equal(LuaOperationStatusKind.InvalidResult, text.Kind);
		Assert.Equal(0, textCount);
		Assert.Equal(0, values[0]);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q22")]
	public void Lua_error_after_partial_results_restores_the_stack_and_the_next_call_succeeds()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, OptionalBindingSources.StandIns);
		GeneratedAssembly assembly = LoadSuite(roslyn);
		SequenceDelegate sequence = assembly.Delegate<SequenceDelegate>(OptionalsType, "Sequence");
		ShapeOptionalDelegate shape = assembly.Delegate<ShapeOptionalDelegate>(OptionalsType, "ShapeOptional");
		Span<long> values = stackalloc long[4];

		LuaOperationStatus raised = sequence(-1, default, values, out int raisedCount);
		Assert.Equal(LuaOperationStatusKind.LuaFailure, raised.Kind);
		Assert.Equal(LuaStatus.RuntimeError, raised.LuaStatus);
		Assert.Equal(0, raisedCount);
		Assert.Equal(0, L.Top);
		Assert.Equal(LuaOperationStatusKind.Success, sequence(2, default, values, out int count).Kind);
		Assert.Equal(2, count);

		Assert.Equal(LuaOperationStatusKind.LuaFailure, shape(99, out LuaOptional<long> failed).Kind);
		Assert.True(failed.IsOmitted);
		Assert.Equal(LuaOperationStatusKind.Success, shape(2, out LuaOptional<long> recovered).Kind);
		Assert.Equal(LuaOptional.Of(7L), recovered);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Warm_optional_result_and_variadic_forms_allocate_nothing()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, OptionalBindingSources.StandIns);
		GeneratedAssembly assembly = LoadSuite(roslyn);
		ShapePairDelegate pair = assembly.Delegate<ShapePairDelegate>(OptionalsType, "ShapeRequiredThenOptional");
		SequenceDelegate sequence = assembly.Delegate<SequenceDelegate>(OptionalsType, "Sequence");
		long[] buffer = new long[8];
		long sink = 0;

		AllocationGate.AssertZero(() =>
		{
			if (!pair(3, out long first, out LuaOptional<long> second).IsSuccess || !second.TryGetValue(out long value)
				|| !sequence(3, default, buffer, out int count).IsSuccess || count != 3)
			{
				throw new InvalidOperationException("wrong results");
			}

			sink += first + value + buffer[2];
		});

		Assert.NotEqual(0, sink);
		Assert.Equal(0, L.Top);
	}

	private static GeneratedAssembly LoadSuite(RoslynFixture roslyn)
	{
		return GeneratedAssembly.Load(roslyn.Run(OptionalBindingSources.GlobalSuite));
	}

	private delegate LuaOperationStatus ShapeOptionalDelegate(int mode, out LuaOptional<long> first);

	private delegate LuaOperationStatus ShapePairDelegate(int mode, out long first, out LuaOptional<long> second);

	private delegate bool TryShapePairDelegate(int mode, out long first, out LuaOptional<long> second);

	private delegate LuaOperationStatus FlagAndWarningDelegate(int mode, out bool ok, out LuaOptional<string> warning);

	private delegate bool TryShapeScalarDelegate(int mode, out long value);

	private delegate LuaOperationStatus SequenceDelegate(int count, LuaOptional<int> bad, Span<long> values,
		out int valueCount);

	private delegate LuaOperationStatus SequenceAfterOneDelegate(int count, out long first, out LuaOptional<long> second,
		Span<long> rest, out int restCount);
}
