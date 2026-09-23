using CheatEngine.SDK.Engine.Assembly;
using CheatEngine.SDK.Engine.Processes;

namespace CheatEngine.SDK.Engine.Tests.Assembly;

/// <summary>
///     Managed contract tests for the numeric shape of the instruction and process status enums: the zero value is
///     <c>Unknown</c>, never success, and every member keeps an explicit, literal value.
/// </summary>
public sealed class InstructionStatusContractTests
{
	[Fact]
	public void instruction_operation_status_default_is_unknown_and_not_success()
	{
		InstructionOperationStatus unassigned = default;

		Assert.Equal(InstructionOperationStatus.Unknown, unassigned);
		Assert.NotEqual(InstructionOperationStatus.Success, unassigned);
		Assert.Equal(0, (int) unassigned);
	}

	[Fact]
	public void instruction_and_process_status_values_are_pinned()
	{
		(InstructionOperationStatus Member, int Value)[] instruction =
		[
			(InstructionOperationStatus.Unknown, 0),
			(InstructionOperationStatus.Success, 1),
			(InstructionOperationStatus.InvalidProfile, 2),
			(InstructionOperationStatus.AddressExceedsProfileWidth, 3),
			(InstructionOperationStatus.TargetNotSelected, 4),
			(InstructionOperationStatus.TargetChanged, 5),
			(InstructionOperationStatus.DestinationTooSmall, 6),
			(InstructionOperationStatus.OutputTooLong, 7),
			(InstructionOperationStatus.InstructionRejected, 8),
			(InstructionOperationStatus.GlobalUnavailable, 9),
			(InstructionOperationStatus.LuaFailure, 10),
			(InstructionOperationStatus.InvalidResult, 11),
			(InstructionOperationStatus.UnsupportedTargetBackend, 12)
		];
		(ProcessOperationStatusKind Member, int Value)[] process =
		[
			(ProcessOperationStatusKind.Unknown, 0),
			(ProcessOperationStatusKind.Success, 1),
			(ProcessOperationStatusKind.TargetNotAttached, 2),
			(ProcessOperationStatusKind.SelectionNotConfirmed, 3),
			(ProcessOperationStatusKind.GlobalUnavailable, 4),
			(ProcessOperationStatusKind.ProtectedLuaFailure, 5),
			(ProcessOperationStatusKind.InvalidResult, 6),
			(ProcessOperationStatusKind.TargetChanged, 7),
			(ProcessOperationStatusKind.FileAsProcessTarget, 8)
		];

		foreach ((InstructionOperationStatus member, int value) in instruction)
		{
			Assert.Equal(value, (int) member);
		}

		foreach ((ProcessOperationStatusKind member, int value) in process)
		{
			Assert.Equal(value, (int) member);
		}

		Assert.Equal(instruction.Length, Enum.GetValues<InstructionOperationStatus>().Length);
		Assert.Equal(process.Length, Enum.GetValues<ProcessOperationStatusKind>().Length);
	}
}
