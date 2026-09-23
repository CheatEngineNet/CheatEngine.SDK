using System;
using System.ComponentModel;
using System.Diagnostics;

using CheatEngine.SDK.Engine.Processes;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Lua.CompilerServices;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Engine.Targets;

/// <summary>Observes and validates the target currently selected by Cheat Engine.</summary>
/// <remarks>
///     <para>
///         This API never selects a process and never opens a replacement target for cleanup. It can prove only the
///         current PID plus the current local creation-time observation, and only for a local process. It does not claim
///         to have observed an external A-to-B-to-A transition that completed between two observations; a host
///         transition sequence or target-specific host primitive remains a separate qualification requirement for that
///         stronger guarantee. Cheat Engine's target is ambient: before-and-after checks reduce risk but are not a
///         transaction or a lock (audit A12-01).
///     </para>
///     <para>
///         In one protected operation it reads <c>getOpenedProcessID</c> and then <c>isConnectedToCEServer</c>. The
///         backend decides which evidence exists (audit A12-03, A12-05, A12-07):
///     </para>
///     <list type="table">
///         <item>
///             <term>PID in (0, <see cref="int.MaxValue" />] and <c>isConnectedToCEServer() == false</c></term>
///             <description>
///                 Local process: the local creation time is looked up and, when available, the observation is
///                 <see cref="TargetSelectionObservationStatus.CurrentTargetQualified" />.
///             </description>
///         </item>
///         <item>
///             <term>
///                 <c>isConnectedToCEServer() == true</c>
///             </term>
///             <description>
///                 <see cref="TargetSelectionObservationStatus.CurrentTargetRemoteBackend" />: a local PID and creation
///                 time do not describe a PID served by CEServer, so no lookup runs and no incarnation is emitted.
///             </description>
///         </item>
///         <item>
///             <term><c>isConnectedToCEServer</c> absent</term>
///             <description>
///                 <see cref="TargetSelectionObservationStatus.CurrentTargetBackendUnknown" />: without the backend
///                 fact no local incarnation evidence is emitted.
///             </description>
///         </item>
///         <item>
///             <term>PID 4294967295</term>
///             <description>
///                 <see cref="TargetSelectionObservationStatus.CurrentTargetFileAsProcess" />: the file-as-process
///                 sentinel (<c>LuaHandler.pas:14101-14108</c> at ec45d5f, ObservedSource; not observed on the 7.7
///                 binary, ToQualify). A file opened as a process has no operating-system process, so no Windows
///                 process is searched for. Every value this catches was already refused as an invalid PID; it is a
///                 refusal classification, and 0xFFFFFFFF is not a multiple of four, so it is never a Windows PID.
///             </description>
///         </item>
///     </list>
///     <para>
///         Only the local backend (<see cref="TargetBackend.LocalProcess" />) is a qualified backend, and these
///         behaviours are fixture-tested (C1); they are not host-qualified.
///     </para>
/// </remarks>
public static class TargetSelection
{
	private static readonly LuaRef SIsConnectedToCeServer = new();

	/// <summary>Gets a copied observation of Cheat Engine's current target selection.</summary>
	public static TargetSelectionObservation ObserveCurrent()
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		int top = state.Top;
		try
		{
			return ObserveCurrent(state);
		}
		finally
		{
			state.SetTop(top);
		}
	}

	/// <summary>Checks whether the current qualified selection still denotes <paramref name="expected" />.</summary>
	/// <param name="expected">The incarnation captured when the target-bound owner was acquired.</param>
	/// <returns>A factual current, changed, reused, or unavailable result.</returns>
	public static TargetIdentityCheck ValidateCurrent(TargetProcessIncarnation expected)
	{
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		LuaState state = operation.State;
		int top = state.Top;
		try
		{
			return ValidateCurrent(state, expected);
		}
		finally
		{
			state.SetTop(top);
		}
	}

	internal static TargetSelectionObservation ObserveCurrent(LuaState state)
	{
		TargetProbeStatus selection = TargetArchitectureProbe.ReadProcessId(state, out int processId, out _);
		switch (selection)
		{
			case TargetProbeStatus.Success:
				break;
			case TargetProbeStatus.NoTargetSelected:
				return TargetSelectionObservation.NoTarget();
			case TargetProbeStatus.FileAsProcess:
				return TargetSelectionObservation.FileAsProcess();
			case TargetProbeStatus.GlobalUnavailable:
				return TargetSelectionObservation.FromStatus(TargetSelectionObservationStatus.GlobalUnavailable);
			case TargetProbeStatus.LuaFailure:
				return TargetSelectionObservation.FromStatus(TargetSelectionObservationStatus.LuaFailure);
			default:
				return TargetSelectionObservation.FromStatus(TargetSelectionObservationStatus.InvalidResult);
		}

		TargetBackend backend = ReadBackend(state, out TargetSelectionObservationStatus failure);
		if (failure != TargetSelectionObservationStatus.Unspecified)
		{
			return TargetSelectionObservation.FromStatus(failure);
		}

		switch (backend)
		{
			case TargetBackend.LocalProcess:
				return TryObserveIncarnation(processId, out TargetProcessIncarnation incarnation)
					? TargetSelectionObservation.Qualified(incarnation)
					: TargetSelectionObservation.Unqualified(processId);
			case TargetBackend.CEServer:
				return TargetSelectionObservation.RemoteBackend(processId);
			default:
				return TargetSelectionObservation.BackendUnknown(processId);
		}
	}

	internal static TargetIdentityCheck ValidateCurrent(LuaState state, TargetProcessIncarnation expected)
	{
		TargetSelectionObservation observed = ObserveCurrent(state);
		if (!observed.IsQualified)
		{
			return CreateUnavailableCheck(observed);
		}

		TargetProcessIncarnation current = observed.Incarnation.GetValueOrDefault();
		if (current.ProcessId != expected.ProcessId)
		{
			return new TargetIdentityCheck(TargetIdentityCheckKind.TargetChanged, observed);
		}

		return current.StartedAtUtcTicks == expected.StartedAtUtcTicks
			? new TargetIdentityCheck(TargetIdentityCheckKind.Current, observed)
			: new TargetIdentityCheck(TargetIdentityCheckKind.ProcessReused, observed);
	}

	internal static TargetIdentityCheck CreateUnavailableCheck(TargetSelectionObservation observation)
	{
		return new TargetIdentityCheck(MapUnavailable(observation.Status), observation);
	}

	// isConnectedToCEServer, in the same protected operation as the PID: false is the local backend, true is CEServer, an
	// absent global leaves the backend unknown. A raising probe or a non-boolean (nil included) is a failure, never local.
	private static TargetBackend ReadBackend(LuaState state, out TargetSelectionObservationStatus failure)
	{
		failure = TargetSelectionObservationStatus.Unspecified;
		int top = state.Top;
		try
		{
			LuaGlobalPushOutcome global =
				LuaGlobalFunctions.TryPushWithOutcome(state, SIsConnectedToCeServer, "isConnectedToCEServer"u8);
			if (global.Status == LuaGlobalPushStatus.Unavailable)
			{
				return TargetBackend.Unknown;
			}

			if (!global.IsSuccess || !state.TryCall(0, 1).IsOk)
			{
				failure = TargetSelectionObservationStatus.LuaFailure;
				return TargetBackend.Unknown;
			}

			if (state.TypeOf(-1) != LuaType.Boolean)
			{
				failure = TargetSelectionObservationStatus.InvalidResult;
				return TargetBackend.Unknown;
			}

			return state.ToBoolean(-1) ? TargetBackend.CEServer : TargetBackend.LocalProcess;
		}
		finally
		{
			state.SetTop(top);
		}
	}

	private static bool TryObserveIncarnation(int processId, out TargetProcessIncarnation incarnation)
	{
		try
		{
			using Process process = Process.GetProcessById(processId);
			long startedAtUtcTicks = process.StartTime.ToUniversalTime().Ticks;
			if (startedAtUtcTicks <= 0)
			{
				incarnation = default;
				return false;
			}

			incarnation = new TargetProcessIncarnation(processId, startedAtUtcTicks);
			return true;
		}
		catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception
			                                  or NotSupportedException or UnauthorizedAccessException)
		{
			incarnation = default;
			return false;
		}
	}

	private static TargetIdentityCheckKind MapUnavailable(TargetSelectionObservationStatus status)
	{
		return status switch
		{
			TargetSelectionObservationStatus.NoTargetSelected => TargetIdentityCheckKind.NoTargetSelected,
			TargetSelectionObservationStatus.CurrentTargetUnqualified => TargetIdentityCheckKind
				.CurrentTargetUnqualified,
			TargetSelectionObservationStatus.GlobalUnavailable => TargetIdentityCheckKind.GlobalUnavailable,
			TargetSelectionObservationStatus.LuaFailure => TargetIdentityCheckKind.LuaFailure,
			TargetSelectionObservationStatus.CurrentTargetRemoteBackend => TargetIdentityCheckKind.RemoteBackend,
			TargetSelectionObservationStatus.CurrentTargetFileAsProcess => TargetIdentityCheckKind.FileAsProcess,
			TargetSelectionObservationStatus.CurrentTargetBackendUnknown => TargetIdentityCheckKind.BackendUnknown,
			_ => TargetIdentityCheckKind.InvalidResult
		};
	}
}
