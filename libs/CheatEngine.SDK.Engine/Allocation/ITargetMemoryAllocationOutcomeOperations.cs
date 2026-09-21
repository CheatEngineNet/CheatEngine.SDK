using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.SDK.Engine.Allocation;

/// <summary>
///     An additive detailed-outcome view of <see cref="ITargetMemoryAllocationOperations" /> for consumers that need
///     to distinguish expected Cheat Engine results from binding-boundary failures without parsing exception text.
/// </summary>
/// <remarks>
///     This interface does not replace <see cref="ITargetMemoryAllocationOperations" />. Existing consumers retain its
///     <see langword="bool" /> and throwing facade behavior, while implementations may expose this view alongside it.
///     Neither result carries a Lua state, Lua error object, or error message.
/// </remarks>
public interface ITargetMemoryAllocationOutcomeOperations
{
    /// <summary>Runs the allocation operation and returns its structured outcome.</summary>
    /// <param name="request">The target allocation request.</param>
    /// <returns>The result category and nonzero address on success.</returns>
    /// <exception cref="System.InvalidOperationException">The plugin is not enabled or its Lua operation scope is unavailable.</exception>
    [RequiresPluginEnabled]
    public TargetMemoryAllocationOutcome AllocateWithOutcome(TargetAllocationRequest request);

    /// <summary>Runs the deallocation operation and returns its structured outcome.</summary>
    /// <param name="address">The owned nonzero address in the target process.</param>
    /// <param name="size">The original allocation request size.</param>
    /// <returns>The result category for the deallocation operation.</returns>
    /// <exception cref="System.InvalidOperationException">The plugin is not enabled or its Lua operation scope is unavailable.</exception>
    [RequiresPluginEnabled]
    public TargetMemoryOperationOutcome DeallocateWithOutcome(Address address, TargetAllocationSize size);
}
