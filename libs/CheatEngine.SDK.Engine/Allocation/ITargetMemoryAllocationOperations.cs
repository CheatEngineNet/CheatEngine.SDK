using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.SDK.Engine.Allocation;

/// <summary>
///     The narrow, generated-binding-facing operations behind <see cref="TargetMemoryAllocator" /> and
///     <see cref="AllocatedRegion" />.
/// </summary>
/// <remarks>
///     A CE 7.7 implementation maps <see cref="TryAllocate" /> to <c>allocateMemory</c> and
///     <see cref="TryDeallocate" /> to <c>deAlloc</c>. A <see langword="false" /> return is reserved for an expected
///     CE operation failure; global absence must throw <see cref="EngineGlobalUnavailableException" />, a binding/spec
///     incompatibility must throw <see cref="EngineBindingException" />, and malformed results must throw
///     <see cref="EngineMarshallingException" />. Protected-call errors are exposed as <see cref="EngineLuaException" />
///     with any SDK cause retained as its inner exception. The interface does not invent a post-allocation
///     protection-changing call:
///     CE 7.7's documented allocation global receives protection as an optional input. The CE 7.7 catalog establishes
///     no GUI-thread affinity for either global; implementations use the calling thread's host Lua state and must not
///     claim <c>MainThreadOnly</c> until a live probe establishes that contract.
/// </remarks>
public interface ITargetMemoryAllocationOperations
{
    /// <summary>
    ///     Attempts to allocate memory in the attached target process.
    /// </summary>
    /// <param name="request">The validated target allocation request.</param>
    /// <param name="address">The nonzero target address on success; <see cref="Address.Zero" /> on expected failure.</param>
    /// <returns><see langword="true" /> on success; <see langword="false" /> for an expected CE allocation failure.</returns>
    /// <exception cref="EngineGlobalUnavailableException">The required CE global is absent or non-callable.</exception>
    /// <exception cref="EngineBindingException">The binding cannot uphold its documented contract.</exception>
    /// <exception cref="EngineMarshallingException">The CE result cannot be represented by this contract.</exception>
    /// <exception cref="EngineLuaException">The protected CE Lua call failed.</exception>
    [RequiresPluginEnabled]
    public bool TryAllocate(TargetAllocationRequest request, out Address address);

    /// <summary>
    ///     Attempts to free a region previously created through <see cref="TryAllocate" />.
    /// </summary>
    /// <param name="address">The owned nonzero address in the target process.</param>
    /// <param name="size">The original allocation request size passed to <c>deAlloc</c>.</param>
    /// <returns><see langword="true" /> on success; <see langword="false" /> for an expected CE deallocation failure.</returns>
    /// <exception cref="EngineGlobalUnavailableException">The required CE global is absent or non-callable.</exception>
    /// <exception cref="EngineBindingException">The binding cannot uphold its documented contract.</exception>
    /// <exception cref="EngineMarshallingException">The CE result cannot be represented by this contract.</exception>
    /// <exception cref="EngineLuaException">The protected CE Lua call failed.</exception>
    [RequiresPluginEnabled]
    public bool TryDeallocate(Address address, TargetAllocationSize size);
}
