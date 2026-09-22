using System.Runtime.InteropServices;

using CheatEngine.SDK.Engine.Targets;

namespace CheatEngine.SDK.Engine.Scanning.Values;

/// <summary>
///     The stable outcome of consuming a memory-scan session's child <see cref="FoundList" /> owner before its parent
///     <see cref="MemScan" /> owner.
/// </summary>
/// <remarks>
///     Each owner is consumed exactly once. The two <see cref="TargetReleaseOutcome" /> values distinguish confirmed
///     cleanup, a safe refusal before the CE destroy call, cleanup that could not begin, and a native effect that began
///     but could not be confirmed. A later release or <see cref="MemoryScanSession.Dispose" /> returns this same outcome
///     and never retries either CE destroy call.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly record struct MemoryScanReleaseOutcome
{
	internal MemoryScanReleaseOutcome(TargetReleaseOutcome foundList, TargetReleaseOutcome memScan,
		bool foundListOwnershipConsumed, bool memScanOwnershipConsumed)
	{
		FoundList = foundList;
		MemScan = memScan;
		FoundListOwnershipConsumed = foundListOwnershipConsumed;
		MemScanOwnershipConsumed = memScanOwnershipConsumed;
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
}
