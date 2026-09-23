using System;

using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Processes;

/// <summary>Produces a <see cref="RuntimeInfo" /> snapshot from read-only Cheat Engine runtime probes.</summary>
/// <remarks>
///     <para>
///         <see cref="TryObserveRuntimeInfo" /> reads the host facts (<c>getCheatEngineFileVersion</c>,
///         <c>getSystemArchitecture</c>, <c>cheatEngineIs64Bit</c>, <c>getOperatingSystem</c>) and then the target facts
///         (<c>getOpenedProcessID</c>, <c>isConnectedToCEServer</c>, <c>targetIs64Bit</c>, <c>targetIsX86</c>,
///         <c>targetIsArm</c>, <c>targetIsAndroid</c>, <c>getABI</c>, <c>getPointerSize</c>, <c>getOpenedProcessID</c>)
///         in one Lua admission. That is the complete list: the snapshot never calls <c>openProcess</c>,
///         <c>openFileAsProcess</c>, <c>setPointerSize</c>, <c>setAssemblerMode</c>, <c>pause</c>, a <c>dbk_*</c> or a
///         <c>dbvm_*</c> global, so taking it loads no driver, runs nothing remotely and changes no target (audit A17-18,
///         Q45; fixture-tested at C1/C2).
///     </para>
///     <para>
///         <see cref="RuntimeInfo.Capabilities" /> holds one entry per capability whose globals were probed: available
///         when every global resolved and returned a well-formed value, unavailable when a global is absent. A capability
///         that was not probed (for example every target capability when no target is selected) is not listed, so its
///         state stays unknown. No entry is filled by inference (ADR-09). Each entry carries the same contract metadata
///         as the <c>runtime-capabilities</c> EngineApi specification: minimum CE 7.7.0.10621 (celua.txt,
///         ExactInstalledFile, and spike C3), the Cheat Engine process scope and x64 for host facts, the target scope and
///         an unknown architecture requirement for target facts, unknown thread affinity, no ownership, and a single
///         value except the file version, which may have none.
///     </para>
/// </remarks>
public static class RuntimeObservations
{
	private static readonly RuntimeCapabilityContract SHostValue = new(CheatEngineVersion.Ce77010621,
		RuntimeArchitectureScope.CheatEngine, RuntimeArchitectureRequirement.X64, RuntimeThreadRequirement.Unknown,
		RuntimeOwnership.None, RuntimeReturnSemantics.Value);

	private static readonly RuntimeCapabilityContract SHostOptionalValue =
		SHostValue with
		{
			ReturnSemantics = RuntimeReturnSemantics.OptionalValue
		};

	private static readonly RuntimeCapabilityContract STargetValue = new(CheatEngineVersion.Ce77010621,
		RuntimeArchitectureScope.Target, RuntimeArchitectureRequirement.Unknown, RuntimeThreadRequirement.Unknown,
		RuntimeOwnership.None, RuntimeReturnSemantics.Value);

	/// <summary>Observes the Cheat Engine host and, when one is selected, the target, and returns an SDK-produced snapshot.</summary>
	/// <param name="info">The snapshot only when the returned status is successful; otherwise <see langword="null" />.</param>
	/// <returns>
	///     <para>
	///         <see cref="ProcessOperationStatusKind.Success" /> with host facts and, when a target is selected, target
	///         facts. No selected target is a legitimate snapshot: <see cref="RuntimeInfo.Target" /> is then
	///         <see langword="null" />. An absent global is not a failure either: its fact stays unknown and its capability
	///         is unavailable (an absent <c>getOpenedProcessID</c> or <c>targetIs64Bit</c> leaves the target unobserved).
	///     </para>
	///     <para>
	///         <see cref="ProcessOperationStatusKind.FileAsProcessTarget" /> for a file opened as a process, which has no
	///         capability profile in the SDK (audit A12-07); <see cref="ProcessOperationStatusKind.TargetChanged" />,
	///         <see cref="ProcessOperationStatusKind.ProtectedLuaFailure" /> or
	///         <see cref="ProcessOperationStatusKind.InvalidResult" /> when a probe cannot be attributed, raised or returned
	///         a malformed value. <paramref name="info" /> is <see langword="null" /> for each of them.
	///     </para>
	/// </returns>
	/// <exception cref="InvalidOperationException">The plugin is not enabled or the calling thread has no Lua state.</exception>
	[RequiresPluginEnabled]
	public static ProcessOperationStatus TryObserveRuntimeInfo(out RuntimeInfo? info)
	{
		info = null;
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		int top = state.Top;
		try
		{
			LuaOperationStatus hostStatus =
				RuntimeHostOperations.ObserveHost(state, out CheatEngineHostObservation host,
					out HostProbeFacts absent);
			if (!hostStatus.IsSuccess)
			{
				return FromHostStatus(hostStatus);
			}

			ProcessOperationStatus targetStatus = RuntimeProcessOperations.ObserveTargetArchitectureCore(state,
				out TargetArchitectureObservation observation, out TargetProbeResult probe);
			TargetArchitectureObservation? target;
			switch (targetStatus.Kind)
			{
				case ProcessOperationStatusKind.Success:
					target = observation;
					break;
				case ProcessOperationStatusKind.TargetNotAttached:
				case ProcessOperationStatusKind.GlobalUnavailable:
					target = null;
					break;
				default:
					return targetStatus;
			}

			info = new RuntimeInfo(host, target, CreateCapabilities(absent, probe));
			return ProcessOperationStatus.Success;
		}
		finally
		{
			state.SetTop(top);
		}
	}

	private static ProcessOperationStatus FromHostStatus(LuaOperationStatus status)
	{
		return status.Kind switch
		{
			LuaOperationStatusKind.LuaFailure => ProcessOperationStatus.ProtectedLuaFailure(status.LuaStatus),
			LuaOperationStatusKind.GlobalUnavailable => ProcessOperationStatus.GlobalUnavailable,
			_ => ProcessOperationStatus.InvalidResult
		};
	}

	private static RuntimeCapabilities CreateCapabilities(HostProbeFacts absentHostFacts, in TargetProbeResult probe)
	{
		RuntimeCapabilityAvailability[] entries = new RuntimeCapabilityAvailability[10];
		int count = 0;
		entries[count++] = Host(RuntimeCapabilityId.CheatEngineVersion, HostProbeFacts.FileVersion, absentHostFacts,
			SHostOptionalValue);
		entries[count++] = Host(RuntimeCapabilityId.SystemArchitecture, HostProbeFacts.SystemArchitecture,
			absentHostFacts, SHostValue);
		entries[count++] = Host(RuntimeCapabilityId.CheatEngineBitness, HostProbeFacts.CheatEngineBitness,
			absentHostFacts, SHostValue);
		entries[count++] = Host(RuntimeCapabilityId.OperatingSystem, HostProbeFacts.OperatingSystem, absentHostFacts,
			SHostValue);

		AddTarget(entries, ref count, RuntimeCapabilityId.CurrentProcess, TargetProbeFacts.SelectedProcess, probe);
		AddTarget(entries, ref count, RuntimeCapabilityId.TargetBackend, TargetProbeFacts.CeServerConnection, probe);
		AddTarget(entries, ref count, RuntimeCapabilityId.TargetArchitecture, TargetProbeFacts.InstructionSet, probe);
		AddTarget(entries, ref count, RuntimeCapabilityId.TargetAndroid, TargetProbeFacts.Android, probe);
		AddTarget(entries, ref count, RuntimeCapabilityId.TargetAbi, TargetProbeFacts.Abi, probe);
		AddTarget(entries, ref count, RuntimeCapabilityId.ConfiguredPointerSize, TargetProbeFacts.ConfiguredPointerSize,
			probe);
		return RuntimeCapabilities.Create(entries.AsSpan(0, count));
	}

	private static RuntimeCapabilityAvailability Host(RuntimeCapabilityId capability, HostProbeFacts fact,
		HostProbeFacts absent, RuntimeCapabilityContract contract)
	{
		RuntimeCapabilityAvailabilityState state = (absent & fact) != HostProbeFacts.None
			? RuntimeCapabilityAvailabilityState.Unavailable
			: RuntimeCapabilityAvailabilityState.Available;
		return new RuntimeCapabilityAvailability(capability, state, contract);
	}

	// A capability is listed only when its globals were probed: unavailable when any of them is absent, available when
	// all of them resolved to a well-formed value.
	private static void AddTarget(RuntimeCapabilityAvailability[] entries, ref int count,
		RuntimeCapabilityId capability,
		TargetProbeFacts facts, in TargetProbeResult probe)
	{
		if ((probe.Absent & facts) != TargetProbeFacts.None)
		{
			entries[count++] = new RuntimeCapabilityAvailability(capability,
				RuntimeCapabilityAvailabilityState.Unavailable, STargetValue);
		}
		else if ((probe.Resolved & facts) == facts)
		{
			entries[count++] = new RuntimeCapabilityAvailability(capability,
				RuntimeCapabilityAvailabilityState.Available, STargetValue);
		}
	}
}
