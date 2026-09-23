using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.CompilerServices;

namespace CheatEngine.SDK.Lua.Tests.CompilerServices;

/// <summary>
///     The generator-facing global-resolution outcome (orchestrator decision O3): an unassigned outcome is
///     <see cref="LuaGlobalPushStatus.Unknown" /> and projects to an unknown operation status, never to success and never
///     to a Lua failure with an <c>Ok</c> status. No Lua library involved.
/// </summary>
public sealed class LuaGlobalPushOutcomeTests
{
	[Fact]
	public void Default_outcome_is_unknown_and_projects_to_an_unknown_operation_status()
	{
		LuaGlobalPushOutcome unassigned = default;

		Assert.Equal(LuaGlobalPushStatus.Unknown, unassigned.Status);
		Assert.False(unassigned.IsSuccess);

		LuaOperationStatus projected = unassigned.ToOperationStatus();
		Assert.Equal(LuaOperationStatusKind.Unknown, projected.Kind);
		Assert.False(projected.IsSuccess);
		Assert.Equal(default, projected);
	}

	[Fact]
	public void Named_outcomes_project_to_their_operation_status()
	{
		Assert.True(LuaGlobalPushOutcome.Success.IsSuccess);
		Assert.Equal(LuaGlobalPushStatus.Success, LuaGlobalPushOutcome.Success.Status);
		Assert.Equal(LuaOperationStatus.Success, LuaGlobalPushOutcome.Success.ToOperationStatus());

		Assert.Equal(LuaGlobalPushStatus.Unavailable, LuaGlobalPushOutcome.Unavailable.Status);
		Assert.Equal(LuaOperationStatus.GlobalUnavailable, LuaGlobalPushOutcome.Unavailable.ToOperationStatus());

		LuaGlobalPushOutcome failure = LuaGlobalPushOutcome.LuaFailure(LuaStatus.MemoryError);
		Assert.Equal(LuaGlobalPushStatus.LuaFailure, failure.Status);
		Assert.Equal(LuaStatus.MemoryError, failure.LuaStatus);
		Assert.Equal(LuaOperationStatus.LuaFailure(LuaStatus.MemoryError), failure.ToOperationStatus());
	}

	[Fact]
	public void Push_status_numeric_values_are_pinned()
	{
		Assert.Equal(0, (int) LuaGlobalPushStatus.Unknown);
		Assert.Equal(1, (int) LuaGlobalPushStatus.Success);
		Assert.Equal(2, (int) LuaGlobalPushStatus.Unavailable);
		Assert.Equal(3, (int) LuaGlobalPushStatus.LuaFailure);
		Assert.Equal(4, Enum.GetValues<LuaGlobalPushStatus>().Length);
	}
}
