using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.SDK.Engine.Allocation;

/// <summary>Target-qualified extension of the allocation binding seam for callers that need an owned region.</summary>
/// <remarks>
///     A legacy <see cref="ITargetMemoryAllocationOperations" /> implementation cannot prove which ambient target a
///     later <c>deAlloc</c> call would affect. The allocator therefore admits allocation owners only through this SDK
///     contract, which keeps target observation and the effectful Lua call in one SDK operation scope. The existing
///     <see cref="ITargetMemoryAllocationOperations" /> contract remains available for its original direct binding
///     operations, but cannot create an <see cref="AllocatedRegion" /> until its implementation opts in here.
/// </remarks>
public interface ITargetBoundMemoryAllocationOperations
{
    /// <summary>
    ///     Allocates only after capturing the current qualified target incarnation in the same operation scope.
    /// </summary>
    /// <param name="request">The allocation request to submit to the current qualified target.</param>
    /// <param name="incarnation">The captured incarnation when <paramref name="observation" /> is qualified.</param>
    /// <param name="observation">The factual current-target observation made before the operation.</param>
    /// <returns>The allocation boundary outcome.</returns>
    TargetMemoryAllocationOutcome AllocateBoundWithOutcome(TargetAllocationRequest request,
        out TargetProcessIncarnation incarnation, out TargetSelectionObservation observation);

    /// <summary>
    ///     Attempts deallocation only when the current qualified target matches <paramref name="expected" />.
    /// </summary>
    /// <param name="expected">The incarnation captured when ownership was acquired.</param>
    /// <param name="address">The owned nonzero allocation address.</param>
    /// <param name="size">The original allocation size.</param>
    /// <param name="targetCheck">The factual current-target comparison made before any deallocation attempt.</param>
    /// <returns><see langword="true" /> only after a matching target and a confirmed deallocation.</returns>
    bool TryDeallocateBound(TargetProcessIncarnation expected, Address address, TargetAllocationSize size,
        out TargetIdentityCheck targetCheck);

    /// <summary>
    ///     Attempts deallocation only when the current qualified target matches <paramref name="expected" />.
    /// </summary>
    /// <param name="expected">The incarnation captured when ownership was acquired.</param>
    /// <param name="address">The owned nonzero allocation address.</param>
    /// <param name="size">The original allocation size.</param>
    /// <param name="targetCheck">The factual current-target comparison made before any deallocation attempt.</param>
    /// <returns>The deallocation boundary outcome or a target-identity refusal.</returns>
    TargetMemoryOperationOutcome DeallocateBoundWithOutcome(TargetProcessIncarnation expected, Address address,
        TargetAllocationSize size, out TargetIdentityCheck targetCheck);
}
