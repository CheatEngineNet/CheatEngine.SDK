using System.Runtime.InteropServices;

using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Targets;

namespace CheatEngine.SDK.Engine.Allocation;

/// <summary>
///     The factual result of <see cref="TargetMemoryAllocator.TryAllocate" />: what Cheat Engine returned, whether an
///     effect happened, the target observation it was made against, whether an owner was published and, when Cheat
///     Engine allocated but no owner could be published, the result of the one compensation attempt.
/// </summary>
/// <remarks>
///     <para>
///         An allocation never yields a live address without an owner silently: when <see cref="Effect" /> is
///         <see cref="EngineEffectState.Applied" /> and <see cref="HasOwner" /> is <see langword="false" />,
///         <see cref="Compensation" /> is set. Whenever Cheat Engine returned an address, <see cref="Allocation" />
///         keeps it (<see cref="TargetMemoryAllocationOutcome.Address" /> is nonzero) even when no owner was published,
///         so a caller can recover the residue manually when the compensation was refused or unconfirmed.
///     </para>
///     <para>
///         <see langword="default" /> is not a result of the allocator: its <see cref="Effect" /> is
///         <see cref="EngineEffectState.Unknown" /> and it has no owner.
///     </para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct TargetAllocationAcquireOutcome
{
	internal TargetAllocationAcquireOutcome(TargetMemoryAllocationOutcome allocation, EngineEffectState effect,
		TargetSelectionObservation targetObservation, TargetReleaseOutcome? compensation, bool hasOwner)
	{
		Allocation = allocation;
		Effect = effect;
		TargetObservation = targetObservation;
		Compensation = compensation;
		HasOwner = hasOwner;
	}

	/// <summary>
	///     Gets the result of the Cheat Engine allocation call: its category and, whenever Cheat Engine returned one, the
	///     allocated address, including when no owner was published.
	/// </summary>
	public TargetMemoryAllocationOutcome Allocation
	{
		get;
	}

	/// <summary>Gets how far the allocation went.</summary>
	/// <remarks>
	///     <see cref="EngineEffectState.NotStarted" /> when no allocation call began (compatibility seam only, target not
	///     qualified, global unavailable); <see cref="EngineEffectState.NotApplied" /> when Cheat Engine returned its
	///     documented negative result (<c>nil</c>); <see cref="EngineEffectState.Applied" /> when Cheat Engine returned
	///     an address; <see cref="EngineEffectState.Unknown" /> for a protected failure or a malformed result.
	/// </remarks>
	public EngineEffectState Effect
	{
		get;
	}

	/// <summary>
	///     Gets the target observation made before the allocation call; <see langword="default" /> when no observation
	///     was made (the allocator only has the compatibility seam, or the binding failed before observing).
	/// </summary>
	public TargetSelectionObservation TargetObservation
	{
		get;
	}

	/// <summary>
	///     Gets the result of the one compensation attempt, set only when Cheat Engine allocated but no owner was
	///     published (unqualified target, runtime identity changed during the call, or owner publication failed).
	/// </summary>
	public TargetReleaseOutcome? Compensation
	{
		get;
	}

	/// <summary>Gets whether an <see cref="AllocatedRegion" /> owner was published through the <c>out</c> parameter.</summary>
	public bool HasOwner
	{
		get;
	}
}
