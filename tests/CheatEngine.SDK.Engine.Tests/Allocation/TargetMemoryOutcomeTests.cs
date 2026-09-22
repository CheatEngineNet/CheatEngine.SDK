using CheatEngine.SDK.Engine.Allocation;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.SDK.Engine.Tests.Allocation;

/// <summary>Public outcome factories preserve the allocation binding result invariants for independent backends.</summary>
public sealed class TargetMemoryOutcomeTests
{
	[Fact]
	public void Succeeded_creates_a_successful_operation_with_an_ok_lua_status()
	{
		TargetMemoryOperationOutcome outcome = TargetMemoryOperationOutcome.Succeeded();

		Assert.True(outcome.IsSuccess);
		Assert.Equal(TargetMemoryOperationOutcomeKind.Succeeded, outcome.Kind);
		Assert.Equal(LuaStatus.Ok, outcome.LuaStatus);
		Assert.Null(outcome.FailureKind);
	}

	[Theory]
	[InlineData(EngineFailureKind.ExpectedOperationFailure, TargetMemoryOperationOutcomeKind.ExpectedFailure)]
	[InlineData(EngineFailureKind.GlobalUnavailable, TargetMemoryOperationOutcomeKind.GlobalUnavailable)]
	[InlineData(EngineFailureKind.CapabilityUnavailable, TargetMemoryOperationOutcomeKind.CapabilityUnavailable)]
	[InlineData(EngineFailureKind.BindingFailure, TargetMemoryOperationOutcomeKind.BindingFailure)]
	[InlineData(EngineFailureKind.MarshallingFailure, TargetMemoryOperationOutcomeKind.MarshallingFailure)]
	[InlineData(EngineFailureKind.TargetIdentityUnavailable,
		TargetMemoryOperationOutcomeKind.TargetIdentityUnavailable)]
	[InlineData(EngineFailureKind.TargetIdentityMismatch, TargetMemoryOperationOutcomeKind.TargetIdentityMismatch)]
	public void Failed_creates_each_non_lua_allocation_failure_with_an_ok_lua_status(EngineFailureKind failureKind,
		TargetMemoryOperationOutcomeKind expectedKind)
	{
		TargetMemoryOperationOutcome outcome = TargetMemoryOperationOutcome.Failed(failureKind, LuaStatus.RuntimeError);

		Assert.False(outcome.IsSuccess);
		Assert.Equal(expectedKind, outcome.Kind);
		Assert.Equal(failureKind, outcome.FailureKind);
		Assert.Equal(LuaStatus.Ok, outcome.LuaStatus);
	}

	[Fact]
	public void Failed_creates_a_protected_lua_failure_only_with_its_failure_status()
	{
		TargetMemoryOperationOutcome outcome = TargetMemoryOperationOutcome.Failed(
			EngineFailureKind.ProtectedLuaFailure,
			LuaStatus.RuntimeError);

		Assert.Equal(TargetMemoryOperationOutcomeKind.ProtectedLuaFailure, outcome.Kind);
		Assert.Equal(EngineFailureKind.ProtectedLuaFailure, outcome.FailureKind);
		Assert.Equal(LuaStatus.RuntimeError, outcome.LuaStatus);
	}

	[Fact]
	public void Failed_rejects_a_successful_lua_status_for_a_protected_lua_failure()
	{
		Assert.Throws<ArgumentException>(() => TargetMemoryOperationOutcome.Failed(
			EngineFailureKind.ProtectedLuaFailure));
	}

	[Fact]
	public void Failed_rejects_an_unknown_engine_failure_kind()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => TargetMemoryOperationOutcome.Failed(
			(EngineFailureKind) int.MaxValue));
	}

	[Fact]
	public void Succeeded_creates_an_allocation_outcome_only_for_a_nonzero_address()
	{
		TargetMemoryAllocationOutcome outcome = TargetMemoryAllocationOutcome.Succeeded(new Address(0x7FF6_4000_0000));

		Assert.True(outcome.IsSuccess);
		Assert.Equal(new Address(0x7FF6_4000_0000), outcome.Address);
		Assert.Throws<ArgumentException>(() => TargetMemoryAllocationOutcome.Succeeded(Address.Zero));
	}

	[Fact]
	public void Failed_creates_an_addressless_allocation_outcome_only_for_a_specified_failure()
	{
		TargetMemoryOperationOutcome failure =
			TargetMemoryOperationOutcome.Failed(EngineFailureKind.ExpectedOperationFailure);
		TargetMemoryAllocationOutcome outcome = TargetMemoryAllocationOutcome.Failed(failure);

		Assert.False(outcome.IsSuccess);
		Assert.Equal(failure, outcome.Operation);
		Assert.Equal(Address.Zero, outcome.Address);
		Assert.Throws<ArgumentException>(() => TargetMemoryAllocationOutcome.Failed(
			TargetMemoryOperationOutcome.Succeeded()));
		Assert.Throws<ArgumentException>(() => TargetMemoryAllocationOutcome.Failed(default));
	}
}
