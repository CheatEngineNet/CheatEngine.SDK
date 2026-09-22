using System.Runtime.InteropServices;

using CheatEngine.SDK.Engine.Targets;

namespace CheatEngine.SDK.Engine.Scanning.Values;

/// <summary>
///     A factual creation result for a target-dependent memory-scan session, including the target observation made
///     before either CE factory can acquire an owned object.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct MemoryScanCreationOutcome
{
	internal MemoryScanCreationOutcome(MemoryScanCreationStatus status, TargetSelectionObservation targetObservation)
	{
		Status = status;
		TargetObservation = targetObservation;
	}

	/// <summary>Gets the stable factory result category.</summary>
	public MemoryScanCreationStatus Status
	{
		get;
	}

	/// <summary>
	///     Gets the target observation made before scanner acquisition. Its facts are retained even when creation was
	///     refused because they do not qualify a target incarnation.
	/// </summary>
	public TargetSelectionObservation TargetObservation
	{
		get;
	}
}
