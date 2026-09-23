using System.Runtime.InteropServices;

using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.SDK.Engine.Targets;

/// <summary>A copied observation of the target currently selected by Cheat Engine.</summary>
/// <remarks>
///     Selection and incarnation are intentionally separate. A selected PID without a creation-time observation is
///     useful diagnostic data but is not admitted as an authority for target-mutating allocation or patch cleanup. Only a
///     <see cref="TargetBackend.LocalProcess" /> selection can be qualified: a CEServer, file-as-process or unknown
///     backend keeps its PID (when there is one) and its backend, but never an incarnation.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly record struct TargetSelectionObservation
{
	internal TargetSelectionObservation(TargetSelectionObservationStatus status, TargetIdentityEvidence evidence,
		int? selectedProcessId, TargetProcessIncarnation? incarnation, TargetBackend backend)
	{
		Status = status;
		Evidence = evidence;
		SelectedProcessId = selectedProcessId;
		Incarnation = incarnation;
		Backend = backend;
	}

	/// <summary>Gets the factual observation category.</summary>
	public TargetSelectionObservationStatus Status
	{
		get;
	}

	/// <summary>Gets the evidence actually available for this observation.</summary>
	public TargetIdentityEvidence Evidence
	{
		get;
	}

	/// <summary>Gets the selected PID when Cheat Engine reported one; otherwise <see langword="null" />.</summary>
	public int? SelectedProcessId
	{
		get;
	}

	/// <summary>Gets the local process incarnation only when the observation is qualified.</summary>
	public TargetProcessIncarnation? Incarnation
	{
		get;
	}

	/// <summary>
	///     Gets the backend established for the selection: <see cref="TargetBackend.LocalProcess" /> when Cheat Engine
	///     reported no CEServer connection, <see cref="TargetBackend.CEServer" />, <see cref="TargetBackend.FileAsProcess" />,
	///     or <see cref="TargetBackend.Unknown" /> when no selection was observed or the backend probe is absent.
	/// </summary>
	public TargetBackend Backend
	{
		get;
	}

	/// <summary>Gets whether the observation can safely identify a local target-process incarnation.</summary>
	public bool IsQualified =>
		Status == TargetSelectionObservationStatus.CurrentTargetQualified && Incarnation.HasValue;

	internal static TargetSelectionObservation Qualified(TargetProcessIncarnation incarnation)
	{
		return new TargetSelectionObservation(TargetSelectionObservationStatus.CurrentTargetQualified,
			TargetIdentityEvidence.CheatEngineSelectedProcessId | TargetIdentityEvidence.LocalBackendConfirmed |
			TargetIdentityEvidence.LocalProcessStartTime, incarnation.ProcessId, incarnation, TargetBackend.LocalProcess);
	}

	internal static TargetSelectionObservation NoTarget()
	{
		return new TargetSelectionObservation(TargetSelectionObservationStatus.NoTargetSelected,
			TargetIdentityEvidence.None, null, null, TargetBackend.Unknown);
	}

	internal static TargetSelectionObservation Unqualified(int processId)
	{
		return new TargetSelectionObservation(TargetSelectionObservationStatus.CurrentTargetUnqualified,
			TargetIdentityEvidence.CheatEngineSelectedProcessId | TargetIdentityEvidence.LocalBackendConfirmed,
			processId, null, TargetBackend.LocalProcess);
	}

	internal static TargetSelectionObservation RemoteBackend(int processId)
	{
		return new TargetSelectionObservation(TargetSelectionObservationStatus.CurrentTargetRemoteBackend,
			TargetIdentityEvidence.CheatEngineSelectedProcessId, processId, null, TargetBackend.CEServer);
	}

	internal static TargetSelectionObservation FileAsProcess()
	{
		return new TargetSelectionObservation(TargetSelectionObservationStatus.CurrentTargetFileAsProcess,
			TargetIdentityEvidence.None, null, null, TargetBackend.FileAsProcess);
	}

	internal static TargetSelectionObservation BackendUnknown(int processId)
	{
		return new TargetSelectionObservation(TargetSelectionObservationStatus.CurrentTargetBackendUnknown,
			TargetIdentityEvidence.CheatEngineSelectedProcessId, processId, null, TargetBackend.Unknown);
	}

	internal static TargetSelectionObservation FromStatus(TargetSelectionObservationStatus status)
	{
		return new TargetSelectionObservation(status, TargetIdentityEvidence.None, null, null, TargetBackend.Unknown);
	}
}
