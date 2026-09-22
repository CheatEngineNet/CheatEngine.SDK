using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Engine.Targets;

/// <summary>A copied observation of the target currently selected by Cheat Engine.</summary>
/// <remarks>
///     Selection and incarnation are intentionally separate. A selected PID without a creation-time observation is
///     useful diagnostic data but is not admitted as an authority for target-mutating allocation or patch cleanup.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly record struct TargetSelectionObservation
{
	internal TargetSelectionObservation(TargetSelectionObservationStatus status, TargetIdentityEvidence evidence,
		int? selectedProcessId, TargetProcessIncarnation? incarnation)
	{
		Status = status;
		Evidence = evidence;
		SelectedProcessId = selectedProcessId;
		Incarnation = incarnation;
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

	/// <summary>Gets whether the observation can safely identify a local target-process incarnation.</summary>
	public bool IsQualified =>
		Status == TargetSelectionObservationStatus.CurrentTargetQualified && Incarnation.HasValue;

	internal static TargetSelectionObservation Qualified(TargetProcessIncarnation incarnation)
	{
		return new TargetSelectionObservation(TargetSelectionObservationStatus.CurrentTargetQualified,
			TargetIdentityEvidence.CheatEngineSelectedProcessId | TargetIdentityEvidence.LocalProcessStartTime,
			incarnation.ProcessId, incarnation);
	}

	internal static TargetSelectionObservation NoTarget()
	{
		return new TargetSelectionObservation(TargetSelectionObservationStatus.NoTargetSelected,
			TargetIdentityEvidence.None, null, null);
	}

	internal static TargetSelectionObservation Unqualified(int processId)
	{
		return new TargetSelectionObservation(TargetSelectionObservationStatus.CurrentTargetUnqualified,
			TargetIdentityEvidence.CheatEngineSelectedProcessId, processId, null);
	}

	internal static TargetSelectionObservation FromStatus(TargetSelectionObservationStatus status)
	{
		return new TargetSelectionObservation(status, TargetIdentityEvidence.None, null, null);
	}
}
