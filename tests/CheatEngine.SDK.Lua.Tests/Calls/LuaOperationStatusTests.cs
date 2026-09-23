using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.SDK.Lua.Tests.Calls;

/// <summary>
///     The generated-binding outcome value (audit ADR-08, orchestrator decision O3): an unassigned status is
///     <see cref="LuaOperationStatusKind.Unknown" />, never success, and the numeric values of the kinds are pinned. No
///     Lua library involved.
/// </summary>
public sealed class LuaOperationStatusTests
{
	[Fact]
	public void Default_status_is_unknown_and_never_success()
	{
		LuaOperationStatus unassigned = default;

		Assert.Equal(LuaOperationStatusKind.Unknown, unassigned.Kind);
		Assert.False(unassigned.IsSuccess);
		Assert.Equal(LuaStatus.Ok, unassigned.LuaStatus);
		Assert.NotEqual(LuaOperationStatus.Success, unassigned);
		Assert.True(LuaOperationStatus.Success != unassigned);
	}

	[Fact]
	public void Named_statuses_keep_their_kind_and_lua_status()
	{
		(LuaOperationStatus Status, LuaOperationStatusKind Kind)[] named =
		[
			(LuaOperationStatus.Success, LuaOperationStatusKind.Success),
			(LuaOperationStatus.GlobalUnavailable, LuaOperationStatusKind.GlobalUnavailable),
			(LuaOperationStatus.NilResult, LuaOperationStatusKind.NilResult),
			(LuaOperationStatus.InvalidResult, LuaOperationStatusKind.InvalidResult),
			(LuaOperationStatus.StackUnavailable, LuaOperationStatusKind.StackUnavailable)
		];

		foreach ((LuaOperationStatus status, LuaOperationStatusKind kind) in named)
		{
			Assert.Equal(kind, status.Kind);
			Assert.Equal(LuaStatus.Ok, status.LuaStatus);
			Assert.Equal(kind == LuaOperationStatusKind.Success, status.IsSuccess);
		}

		LuaOperationStatus failure = LuaOperationStatus.LuaFailure(LuaStatus.RuntimeError);
		Assert.Equal(LuaOperationStatusKind.LuaFailure, failure.Kind);
		Assert.Equal(LuaStatus.RuntimeError, failure.LuaStatus);
		Assert.False(failure.IsSuccess);
		Assert.NotEqual(LuaOperationStatus.LuaFailure(LuaStatus.MemoryError), failure);
		Assert.Equal(LuaOperationStatus.LuaFailure(LuaStatus.RuntimeError).GetHashCode(), failure.GetHashCode());
	}

	[Fact]
	public void Status_kind_numeric_values_are_pinned()
	{
		Assert.Equal(0, (int) LuaOperationStatusKind.Unknown);
		Assert.Equal(1, (int) LuaOperationStatusKind.Success);
		Assert.Equal(2, (int) LuaOperationStatusKind.GlobalUnavailable);
		Assert.Equal(3, (int) LuaOperationStatusKind.LuaFailure);
		Assert.Equal(4, (int) LuaOperationStatusKind.NilResult);
		Assert.Equal(5, (int) LuaOperationStatusKind.InvalidResult);
		Assert.Equal(6, (int) LuaOperationStatusKind.StackUnavailable);
		Assert.Equal(7, Enum.GetValues<LuaOperationStatusKind>().Length);
	}
}
