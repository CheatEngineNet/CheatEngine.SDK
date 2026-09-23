using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.SDK.Engine.Tests.AddressList;

/// <summary>
///     <see cref="AddressListMutations.SetActive" />: one setter call at most, the before and after state, host refusal,
///     pending asynchronous activation and indeterminate effects, never a retry.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class MemoryRecordActivationTests
{
	[Fact]
	[Trait("Qualification", "Q35")]
	public void SetActive_applied_reports_the_before_and_after_state()
	{
		using AddressListTestHost host = new();
		host.AddRecord(1);

		MemoryRecordActivationOutcome outcome = AddressListMutations.SetActive(new MemoryRecordId(1), true);

		Assert.Equal(MemoryRecordActivationOutcomeKind.Applied, outcome.Kind);
		Assert.Equal(EngineEffectState.Applied, outcome.Effect);
		Assert.Equal(MemoryRecordMutationProblem.None, outcome.Problem);
		Assert.True(outcome.RequestedActive);
		Assert.False(outcome.ActiveBefore);
		Assert.True(outcome.ActiveAfter);
		Assert.False(outcome.AsyncProcessingAfter);
		Assert.Equal(1, host.ReadInteger("active_set_calls"));
		Assert.Equal(0, host.State.Top);

		MemoryRecordActivationOutcome deactivated = AddressListMutations.SetActive(new MemoryRecordId(1), false);
		Assert.Equal(MemoryRecordActivationOutcomeKind.Applied, deactivated.Kind);
		Assert.True(deactivated.ActiveBefore);
		Assert.False(deactivated.ActiveAfter);
		Assert.Equal(2, host.ReadInteger("active_set_calls"));
		Assert.Equal(0, host.State.Top);
	}

	[Fact]
	[Trait("Qualification", "Q35")]
	public void SetActive_refused_by_the_host_reports_unknown_effect()
	{
		using AddressListTestHost host = new();
		host.AddRecord(1, "refuse");

		MemoryRecordActivationOutcome outcome = AddressListMutations.SetActive(new MemoryRecordId(1), true);

		Assert.Equal(MemoryRecordActivationOutcomeKind.RefusedByHost, outcome.Kind);
		Assert.Equal(EngineEffectState.Unknown, outcome.Effect);
		Assert.False(outcome.ActiveBefore);
		Assert.False(outcome.ActiveAfter);
		Assert.Equal(1, host.ReadInteger("active_set_calls"));
		Assert.Equal(0, host.State.Top);
	}

	[Fact]
	public void SetActive_raise_after_the_setter_started_is_indeterminate()
	{
		using AddressListTestHost host = new();
		host.AddRecord(1, "raise");

		MemoryRecordActivationOutcome outcome = AddressListMutations.SetActive(new MemoryRecordId(1), true);

		Assert.Equal(MemoryRecordActivationOutcomeKind.Indeterminate, outcome.Kind);
		Assert.Equal(EngineEffectState.Unknown, outcome.Effect);
		Assert.Equal(MemoryRecordMutationProblem.LuaFailure, outcome.Problem);
		Assert.Equal(LuaStatus.RuntimeError, outcome.LuaStatus);
		Assert.False(outcome.ActiveBefore);
		Assert.Null(outcome.ActiveAfter);
		Assert.Equal(1, host.ReadInteger("active_set_calls"));
		Assert.Equal(0, host.State.Top);
	}

	[Fact]
	public void SetActive_post_read_failure_after_the_effect_is_indeterminate_with_an_unknown_after_state()
	{
		using AddressListTestHost host = new();
		host.AddRecord(1, "post-read-fails");

		MemoryRecordActivationOutcome outcome = AddressListMutations.SetActive(new MemoryRecordId(1), true);

		Assert.Equal(MemoryRecordActivationOutcomeKind.Indeterminate, outcome.Kind);
		Assert.Equal(EngineEffectState.Unknown, outcome.Effect);
		Assert.Equal(MemoryRecordMutationProblem.LuaFailure, outcome.Problem);
		Assert.False(outcome.ActiveBefore);
		Assert.Null(outcome.ActiveAfter);
		Assert.Null(outcome.AsyncProcessingAfter);
		Assert.Equal(1, host.ReadInteger("active_set_calls"));
		Assert.Equal(0, host.State.Top);
	}

	[Fact]
	public void SetActive_async_record_still_processing_reports_pending()
	{
		using AddressListTestHost host = new();
		host.AddRecord(1, "async", extra: "o.props.Async = true");

		MemoryRecordActivationOutcome outcome = AddressListMutations.SetActive(new MemoryRecordId(1), true);

		Assert.Equal(MemoryRecordActivationOutcomeKind.Pending, outcome.Kind);
		Assert.Equal(EngineEffectState.Unknown, outcome.Effect);
		Assert.True(outcome.AsyncProcessingAfter);
		Assert.True(outcome.ActiveAfter);
		Assert.Equal(1, host.ReadInteger("active_set_calls"));
		Assert.Equal(0, host.State.Top);
	}

	[Fact]
	public void SetActive_in_the_requested_state_does_not_call_the_setter()
	{
		using AddressListTestHost host = new();
		host.AddRecord(1, active: true);

		MemoryRecordActivationOutcome outcome = AddressListMutations.SetActive(new MemoryRecordId(1), true);

		Assert.Equal(MemoryRecordActivationOutcomeKind.Unchanged, outcome.Kind);
		Assert.Equal(EngineEffectState.NotStarted, outcome.Effect);
		Assert.True(outcome.ActiveBefore);
		Assert.Null(outcome.ActiveAfter);
		Assert.Equal(0, host.ReadInteger("active_set_calls"));
		Assert.Equal(0, host.State.Top);
	}

	[Fact]
	[Trait("Qualification", "Q34")]
	public void SetActive_unknown_id_reports_record_not_found()
	{
		using AddressListTestHost host = new();
		host.AddRecord(1);

		MemoryRecordActivationOutcome outcome = AddressListMutations.SetActive(new MemoryRecordId(99), true);

		Assert.Equal(MemoryRecordActivationOutcomeKind.NotAttempted, outcome.Kind);
		Assert.Equal(EngineEffectState.NotStarted, outcome.Effect);
		Assert.Equal(MemoryRecordMutationProblem.RecordNotFound, outcome.Problem);
		Assert.Null(outcome.ActiveBefore);
		Assert.Equal(0, host.ReadInteger("active_set_calls"));
		Assert.Equal(0, host.State.Top);
	}

	[Fact]
	public void SetActive_never_retries_even_when_the_host_asks_for_a_retry()
	{
		using AddressListTestHost host = new();
		host.AddRecord(1, "retry-request");

		MemoryRecordActivationOutcome outcome = AddressListMutations.SetActive(new MemoryRecordId(1), true);

		Assert.Equal(MemoryRecordActivationOutcomeKind.RefusedByHost, outcome.Kind);
		Assert.Equal(1, host.ReadInteger("retry_requested"));
		Assert.Equal(1, host.ReadInteger("active_set_calls"));
		Assert.Equal(0, host.State.Top);
	}

	[Fact]
	public void SetActive_with_a_non_boolean_active_state_is_not_attempted()
	{
		using AddressListTestHost host = new();
		host.AddRecord(1, extra: "o.props.Active = 'yes'");

		MemoryRecordActivationOutcome outcome = AddressListMutations.SetActive(new MemoryRecordId(1), true);

		Assert.Equal(MemoryRecordActivationOutcomeKind.NotAttempted, outcome.Kind);
		Assert.Equal(MemoryRecordMutationProblem.InvalidResult, outcome.Problem);
		Assert.Equal(0, host.ReadInteger("active_set_calls"));
		Assert.Equal(0, host.State.Top);
	}

	[Fact]
	public void Default_activation_outcome_is_unknown_and_not_an_effect()
	{
		MemoryRecordActivationOutcome outcome = default;

		Assert.Equal(MemoryRecordActivationOutcomeKind.Unknown, outcome.Kind);
		Assert.Equal(EngineEffectState.Unknown, outcome.Effect);
		Assert.Equal(MemoryRecordMutationProblem.Uninitialized, outcome.Problem);
	}

	[Fact]
	public void Memory_record_active_script_and_offset_count_reads_restore_the_stack()
	{
		using AddressListTestHost host = new();
		CEObject handle = host.AddRecord(1, active: true, extra: """
		                                                         o.props.Script = "[ENABLE]\n[DISABLE]"
		                                                         o.props.OffsetCount = 2
		                                                         o.props.Async = true
		                                                         """);
		CEObject plain = host.AddRecord(2, extra: "o.props.OffsetCount = 'many'");
		MemoryRecord record = new(handle);
		MemoryRecord other = new(plain);

		Assert.True(record.TryGetActive(out bool active));
		Assert.True(active);
		Assert.True(record.TryGetAsync(out bool isAsync));
		Assert.True(isAsync);
		Assert.True(record.TryGetAsyncProcessing(out bool processing));
		Assert.False(processing);
		Assert.True(record.TryGetScript(out string? script));
		Assert.Equal("[ENABLE]\n[DISABLE]", script);
		Assert.True(record.TryGetOffsetCount(out int offsets));
		Assert.Equal(2, offsets);
		Assert.False(other.TryGetScript(out _));
		Assert.False(other.TryGetOffsetCount(out _));
		Assert.Equal(0, host.State.Top);
	}
}
