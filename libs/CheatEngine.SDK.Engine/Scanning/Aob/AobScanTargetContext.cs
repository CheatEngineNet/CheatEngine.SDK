using System.Runtime.InteropServices;

using CheatEngine.SDK.Engine.Targets;

namespace CheatEngine.SDK.Engine.Scanning.Aob;

/// <summary>
///     The Cheat Engine target observations made immediately before and immediately after one global <c>AOBScan</c>
///     call, inside the same admitted Lua operation.
/// </summary>
/// <remarks>
///     <para>
///         The global <c>AOBScan</c> route is target-agnostic: it scans whatever process CE has selected when the call
///         runs, and CE returns bare address strings. These observations are facts that let the caller decide whether the
///         returned addresses can be attributed to the target it expected. They never refuse the scan and never change
///         the scan's <see cref="AobScanOutcome.Kind" />.
///     </para>
///     <para>
///         When <see cref="IsSameQualifiedIncarnation" /> is <see langword="false" />, the addresses may belong to
///         another target (the selection changed during the call, or it could not be qualified before or after it). Even
///         when it is <see langword="true" />, an A-to-B-to-A transition completed entirely between the two observations
///         remains unobservable; see <see cref="TargetSelection" />.
///     </para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly record struct AobScanTargetContext
{
	internal AobScanTargetContext(TargetSelectionObservation before, TargetSelectionObservation after)
	{
		Before = before;
		After = after;
	}

	/// <summary>Gets the target observation made immediately before the <c>AOBScan</c> call.</summary>
	public TargetSelectionObservation Before
	{
		get;
	}

	/// <summary>Gets the target observation made immediately after the <c>AOBScan</c> call returned.</summary>
	public TargetSelectionObservation After
	{
		get;
	}

	/// <summary>
	///     Gets whether both observations are qualified and denote the same process incarnation (process identifier and
	///     local start time).
	/// </summary>
	public bool IsSameQualifiedIncarnation =>
		Before.IsQualified && After.IsQualified && Before.Incarnation == After.Incarnation;
}
