using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Processes;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Assembly;

/// <summary>Observes CE's current selected process identifier, architecture probes, and instruction address width.</summary>
/// <remarks>
///     <para>
///         The observation calls <c>getOpenedProcessID</c>, <c>targetIs64Bit</c>, <c>targetIsX86</c>, and
///         <c>targetIsArm</c> under one Lua runtime admission, then checks the process identifier a second time. The
///         profile follows <see cref="RuntimeInfo.TryDeriveTargetArchitecture" />: on Cheat Engine, x86-64 is the x86
///         family plus the 64-bit flag (<c>targetIsX86() == true</c> and <c>targetIs64Bit() == true</c>), the 64-bit
///         flag alone never selects a family, and "contradictory" means both families or neither. The profile width is
///         Cheat Engine's 64-bit process flag (<c>targetIs64Bit</c>), never <c>getPointerSize</c>, the managed host
///         width, or <c>PointerSize.FromArchitecture</c>.
///     </para>
///     <para>
///         With no target selected Cheat Engine reports exactly the facts of an x64 target (spike C3 D2, ObservedHost,
///         Lua-only, 2026-09-22), so the process identifier is read first and a zero identifier returns
///         <see cref="InstructionOperationStatus.TargetNotSelected" /> before any ISA probe runs. The file-as-process
///         sentinel identifier 4294967295 returns <see cref="InstructionOperationStatus.UnsupportedTargetBackend" />,
///         also before any probe. Cheat Engine's "assembly mode" is the same 64-bit process flag
///         (<c>setAssemblerMode</c> writes it); the SDK reads it through <c>targetIs64Bit</c> and never calls
///         <c>setAssemblerMode</c>.
///     </para>
///     <para>
///         CE exposes ambient target state rather than a target-selection lock. The repeated identifier makes a
///         selection transition observable during this operation, but it is an observation, not a lock: it does not
///         identify a process incarnation or prove that a later assembler or disassembler call runs against the same
///         target. Consumers must retain the resulting <see cref="InstructionTargetProfile" /> and pass it to an
///         instruction operation, which performs the same before-and-after coherence check and reports an observed
///         change after the effect as <see cref="InstructionOperationStatus.TargetChanged" /> without promising any
///         rollback.
///     </para>
/// </remarks>
public static class InstructionProfiles
{
	/// <summary>Observes the current CE target and returns a profile only when its selected PID and ISA probes agree.</summary>
	/// <param name="targetProfile">The copied profile and selected PID only when the returned status is success.</param>
	/// <returns>A target, backend, availability, protected-Lua, or profile-consistency outcome.</returns>
	/// <exception cref="global::System.InvalidOperationException">
	///     The plugin is not enabled or the calling thread has no Lua
	///     state.
	/// </exception>
	[RequiresPluginEnabled]
	public static InstructionOperationStatus TryObserveCurrent(out InstructionTargetProfile targetProfile)
	{
		targetProfile = default;
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		int top = state.Top;
		try
		{
			TargetProbeResult probe = TargetArchitectureProbe.Observe(state, TargetProbeFacts.InstructionSet,
				TargetProbeFacts.InstructionSet);
			if (probe.Status != TargetProbeStatus.Success)
			{
				return FromProbe(probe.Status);
			}

			if (!RuntimeInfo.TryDeriveTargetArchitecture(probe.IsX86Family.GetValueOrDefault(),
				    probe.IsArmFamily.GetValueOrDefault(), probe.Is64Bit.GetValueOrDefault(),
				    out CheatEngineArchitecture architecture))
			{
				return InstructionOperationStatus.InvalidProfile;
			}

			targetProfile = new InstructionTargetProfile(new TargetProcessId(probe.ProcessId),
				ToProfile(architecture));
			return InstructionOperationStatus.Success;
		}
		catch (LuaException)
		{
			targetProfile = default;
			return InstructionOperationStatus.LuaFailure;
		}
		finally
		{
			state.SetTop(top);
		}
	}

	/// <summary>
	///     Re-checks, before or after an instruction call, that CE still selects the profiled process. Any other valid
	///     selection (another identifier, no target, or the file-as-process sentinel) differs from the profile and is
	///     <see cref="InstructionOperationStatus.TargetChanged" />, which after the effect is the uncertainty status of
	///     audit A15-06; a failed or malformed read keeps its own status.
	/// </summary>
	internal static InstructionOperationStatus TryVerifyCurrent(LuaState state, TargetProcessId expectedTarget)
	{
		TargetProbeStatus status = TargetArchitectureProbe.ReadProcessId(state, out int actualTarget, out _);
		return status switch
		{
			TargetProbeStatus.Success => actualTarget == expectedTarget.Value
				? InstructionOperationStatus.Success
				: InstructionOperationStatus.TargetChanged,
			TargetProbeStatus.NoTargetSelected or TargetProbeStatus.FileAsProcess => InstructionOperationStatus
				.TargetChanged,
			_ => FromProbe(status)
		};
	}

	private static InstructionOperationStatus FromProbe(TargetProbeStatus status)
	{
		return status switch
		{
			TargetProbeStatus.Success => InstructionOperationStatus.Success,
			TargetProbeStatus.NoTargetSelected => InstructionOperationStatus.TargetNotSelected,
			TargetProbeStatus.FileAsProcess => InstructionOperationStatus.UnsupportedTargetBackend,
			TargetProbeStatus.TargetChanged => InstructionOperationStatus.TargetChanged,
			TargetProbeStatus.GlobalUnavailable => InstructionOperationStatus.GlobalUnavailable,
			TargetProbeStatus.LuaFailure => InstructionOperationStatus.LuaFailure,
			TargetProbeStatus.InvalidProcessId or TargetProbeStatus.InvalidResult => InstructionOperationStatus
				.InvalidResult,
			_ => InstructionOperationStatus.Unknown
		};
	}

	private static InstructionProfile ToProfile(CheatEngineArchitecture architecture)
	{
		return architecture switch
		{
			CheatEngineArchitecture.X86 => InstructionProfile.X86,
			CheatEngineArchitecture.X64 => InstructionProfile.X64,
			CheatEngineArchitecture.Arm32 => InstructionProfile.Arm32,
			CheatEngineArchitecture.Arm64 => InstructionProfile.Arm64,
			_ => default
		};
	}
}
