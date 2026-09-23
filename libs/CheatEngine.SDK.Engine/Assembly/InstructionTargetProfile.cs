using System.Runtime.InteropServices;

using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.SDK.Engine.Assembly;

/// <summary>
///     A copied selected target process identifier and instruction profile observed through CE's protected Lua
///     globals.
/// </summary>
/// <remarks>
///     Instances are created only by <see cref="InstructionProfiles.TryObserveCurrent" />, which reads the selected
///     process identifier before the ISA probes because Cheat Engine reports x64-like facts when no target is selected.
///     Instruction operations re-check the target process identifier before and after their CE call and report
///     <see cref="InstructionOperationStatus.TargetChanged" /> when either check differs. This is a coherence check,
///     an observation rather than a lock: Cheat Engine can still change its ambient selection after the final check,
///     an unseen A→B→A transition is not detected, a reused PID is not distinguished, and a change observed after the
///     CE effect reports uncertainty without any rollback promise.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct InstructionTargetProfile
{
	internal InstructionTargetProfile(TargetProcessId target, InstructionProfile profile)
	{
		Target = target;
		Profile = profile;
	}

	/// <summary>Gets the positive CE-selected process identifier observed with the profile; this is not an incarnation.</summary>
	public TargetProcessId Target
	{
		get;
	}

	/// <summary>Gets the target instruction architecture and address width observed with <see cref="Target" />.</summary>
	public InstructionProfile Profile
	{
		get;
	}

	internal InstructionOperationStatus Validate(Address address)
	{
		if (Target.Value <= 0)
		{
			return InstructionOperationStatus.TargetNotSelected;
		}

		return Profile.Validate(address);
	}
}
