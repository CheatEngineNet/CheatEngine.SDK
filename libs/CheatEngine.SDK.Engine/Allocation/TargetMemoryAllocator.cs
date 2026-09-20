using System;
using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Errors;

namespace CheatEngine.SDK.Engine.Allocation;

/// <summary>
///     Allocates explicitly owned regions in the attached target process through the CE 7.7 allocation contract.
/// </summary>
/// <remarks>
///     This facade validates the strongly typed request and makes an expected CE failure explicit as
///     <see cref="EngineOperationFailedException" />. Protected Lua, binding, and marshalling failures use the canonical
///     Engine error hierarchy instead of being folded into an expected CE result. The generated CE binding is supplied
///     through <see cref="ITargetMemoryAllocationOperations" /> so the lifetime wrapper is testable without an attached
///     Cheat Engine process.
/// </remarks>
public sealed class TargetMemoryAllocator
{
    private readonly ITargetMemoryAllocationOperations _operations;

    /// <summary>
    ///     Initializes the target-memory allocation facade.
    /// </summary>
    /// <param name="operations">The CE 7.7 generated-binding-facing operations.</param>
    /// <exception cref="ArgumentNullException"><paramref name="operations" /> is <see langword="null" />.</exception>
    public TargetMemoryAllocator(ITargetMemoryAllocationOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        _operations = operations;
    }

    /// <summary>
    ///     Allocates a region in the current target process and transfers sole ownership to the returned wrapper.
    /// </summary>
    /// <param name="request">The allocation size, optional target base preference, and optional initial protection.</param>
    /// <returns>The explicitly owned allocation.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="request" /> has the default or an invalid size.</exception>
    /// <exception cref="EngineOperationFailedException">Cheat Engine reported that allocation did not complete.</exception>
    /// <exception cref="EngineGlobalUnavailableException">The required CE global is absent or non-callable.</exception>
    /// <exception cref="EngineBindingException">The CE binding cannot uphold its documented contract.</exception>
    /// <exception cref="EngineMarshallingException">The binding returned an invalid success/failure shape.</exception>
    /// <exception cref="EngineLuaException">The protected CE Lua call failed.</exception>
    [RequiresPluginEnabled]
    public AllocatedRegion Allocate(TargetAllocationRequest request)
    {
        if (request.Size.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), request.Size.Value,
                "An allocation request must have a positive size.");

        var allocated = _operations.TryAllocate(request, out var address);
        if (!allocated)
        {
            if (!address.IsZero)
                throw new EngineMarshallingException("TargetMemoryAllocate", EngineMarshallingDirection.Result,
                    "a null target address on failure", "a nonzero target address on failure");

            throw new EngineOperationFailedException("TargetMemoryAllocate");
        }

        if (address.IsZero)
            throw new EngineMarshallingException("TargetMemoryAllocate", EngineMarshallingDirection.Result,
                "a nonzero target address on success", "a null target address on success");

        return new AllocatedRegion(_operations, address, request.Size);
    }
}
