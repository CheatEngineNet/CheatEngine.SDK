using CheatEngine.SDK.Engine.Processes;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.SDK.Engine.Tests.Processes;

/// <summary>Managed contract tests for the value-only <see cref="ProcessOperationStatus" />.</summary>
public sealed class ProcessOperationStatusTests
{
	[Fact]
	public void process_operation_status_default_is_unknown_and_not_success()
	{
		ProcessOperationStatus unassigned = default;

		Assert.Equal(ProcessOperationStatusKind.Unknown, unassigned.Kind);
		Assert.False(unassigned.IsSuccess);
		Assert.NotEqual(ProcessOperationStatus.Success, unassigned);
		Assert.Equal(LuaStatus.Ok, unassigned.LuaStatus);
	}

	[Fact]
	public void named_statuses_carry_their_kind_and_only_success_reports_success()
	{
		(ProcessOperationStatus Status, ProcessOperationStatusKind Kind)[] statuses =
		[
			(ProcessOperationStatus.Success, ProcessOperationStatusKind.Success),
			(ProcessOperationStatus.TargetNotAttached, ProcessOperationStatusKind.TargetNotAttached),
			(ProcessOperationStatus.SelectionNotConfirmed, ProcessOperationStatusKind.SelectionNotConfirmed),
			(ProcessOperationStatus.GlobalUnavailable, ProcessOperationStatusKind.GlobalUnavailable),
			(ProcessOperationStatus.InvalidResult, ProcessOperationStatusKind.InvalidResult),
			(ProcessOperationStatus.TargetChanged, ProcessOperationStatusKind.TargetChanged),
			(ProcessOperationStatus.FileAsProcessTarget, ProcessOperationStatusKind.FileAsProcessTarget),
			(ProcessOperationStatus.ProtectedLuaFailure(LuaStatus.RuntimeError),
				ProcessOperationStatusKind.ProtectedLuaFailure)
		];

		foreach ((ProcessOperationStatus status, ProcessOperationStatusKind kind) in statuses)
		{
			Assert.Equal(kind, status.Kind);
			Assert.Equal(kind == ProcessOperationStatusKind.Success, status.IsSuccess);
		}

		Assert.Equal(LuaStatus.RuntimeError, ProcessOperationStatus.ProtectedLuaFailure(LuaStatus.RuntimeError).LuaStatus);
		Assert.Throws<ArgumentException>(() => ProcessOperationStatus.ProtectedLuaFailure(LuaStatus.Ok));
	}
}
