using System.Runtime.InteropServices;

using CheatEngine.SDK.Engine.Targets;

namespace CheatEngine.SDK.Engine.Scanning.Values;

/// <summary>
///     The stable outcome of consuming a memory-scan session's child <see cref="FoundList" /> owner before its parent
///     <see cref="MemScan" /> owner, including the cooperative stop of a scan that may still have been running.
/// </summary>
/// <remarks>
///     Each owner is consumed exactly once. The two <see cref="TargetReleaseOutcome" /> values distinguish confirmed
///     cleanup, a safe refusal before the CE destroy call, cleanup that could not begin, and a native effect that began
///     but could not be confirmed. <see cref="Termination" /> reports whether a scan had to be stopped first and whether
///     that stop was confirmed. A later release or <see cref="MemoryScanSession.Dispose" /> returns this same outcome and
///     never retries either CE destroy call or the stop request.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly record struct MemoryScanReleaseOutcome
{
	internal MemoryScanReleaseOutcome(TargetReleaseOutcome foundList, TargetReleaseOutcome memScan,
		bool foundListOwnershipConsumed, bool memScanOwnershipConsumed, MemoryScanTerminationStatus termination)
	{
		FoundList = foundList;
		MemScan = memScan;
		FoundListOwnershipConsumed = foundListOwnershipConsumed;
		MemScanOwnershipConsumed = memScanOwnershipConsumed;
		Termination = termination;
	}

	/// <summary>Gets the child found-list cleanup result, which is always processed before <see cref="MemScan" />.</summary>
	public TargetReleaseOutcome FoundList
	{
		get;
	}

	/// <summary>Gets the parent scanner cleanup result.</summary>
	public TargetReleaseOutcome MemScan
	{
		get;
	}

	/// <summary>Gets whether the session consumed its found-list ownership capability.</summary>
	public bool FoundListOwnershipConsumed
	{
		get;
	}

	/// <summary>Gets whether the session consumed its scanner ownership capability.</summary>
	public bool MemScanOwnershipConsumed
	{
		get;
	}

	/// <summary>Gets whether both session ownership capabilities have been consumed.</summary>
	public bool OwnershipConsumed => FoundListOwnershipConsumed && MemScanOwnershipConsumed;

	/// <summary>
	///     Gets how a scan that may still have been running was stopped before the owners were released.
	/// </summary>
	/// <remarks>
	///     <see cref="MemoryScanTerminationStatus.NotRequired" /> when no scan could be running;
	///     <see cref="MemoryScanTerminationStatus.Confirmed" /> when the one cooperative stop was confirmed;
	///     <see cref="MemoryScanTerminationStatus.NotInvoked" /> when a scan may run, no stop was requested and no CE call
	///     was allowed. Any other value is an unconfirmed stop, never retried: when the release could reach CE, the found
	///     list and the scanner were still destroyed once each (CE's own destroy stops and waits for its scan controller);
	///     <see cref="FoundList" /> and <see cref="MemScan" /> say whether they were. The default value is
	///     <see cref="MemoryScanTerminationStatus.Unknown" />.
	/// </remarks>
	public MemoryScanTerminationStatus Termination
	{
		get;
	}
}
