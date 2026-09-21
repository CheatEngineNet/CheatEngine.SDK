using System;
using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Targets;

namespace CheatEngine.SDK.Engine.Allocation;

/// <summary>
///     Allocates explicitly owned regions in the attached target process through the CE 7.7 allocation contract.
/// </summary>
/// <remarks>
///     This facade validates the strongly typed request and makes an expected CE failure explicit as
///     <see cref="EngineOperationFailedException" />. Protected Lua, binding, and marshalling failures use the canonical
///     Engine error hierarchy instead of being folded into an expected CE result. The generated CE binding is supplied
///     through <see cref="ITargetMemoryAllocationOperations" />. An owned allocation additionally requires the
///     implementation to opt in to <see cref="ITargetBoundMemoryAllocationOperations" />; a legacy direct-operation
///     implementation is never silently used to create an owner with an unverified cleanup target.
/// </remarks>
public sealed class TargetMemoryAllocator
{
    private readonly ITargetMemoryAllocationOperations _operations;

    /// <summary>
    ///     Initializes an allocator backed by the production CE 7.7 <c>allocateMemory</c>/<c>deAlloc</c> binding.
    /// </summary>
    /// <remarks>
    ///     The binding resolves its globals only while an enabled plugin has a Lua state. Constructing this facade does
    ///     not contact Cheat Engine and is safe before plugin enable; <see cref="Allocate" /> remains lifecycle-gated.
    /// </remarks>
    public TargetMemoryAllocator()
        : this(LuaTargetMemoryAllocationOperations.Instance)
    {
    }

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
    /// <exception cref="EngineTargetIdentityException">The operation cannot qualify a target incarnation.</exception>
    [RequiresPluginEnabled]
    public AllocatedRegion Allocate(TargetAllocationRequest request)
    {
        if (request.Size.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), request.Size.Value,
                "An allocation request must have a positive size.");

        if (_operations is not ITargetBoundMemoryAllocationOperations targetBound)
            throw new EngineTargetIdentityException("TargetMemoryAllocate", GetUnavailableTargetCheck());

        var allocationOutcome = targetBound.AllocateBoundWithOutcome(request, out var incarnation, out var observation);
        var allocated = allocationOutcome.IsSuccess;
        var address = allocationOutcome.Address;
        if (!observation.IsQualified)
            throw new EngineTargetIdentityException("TargetMemoryAllocate", TargetSelection.CreateUnavailableCheck(observation));
        if (!allocated)
        {
            if (!address.IsZero)
                throw new EngineMarshallingException("TargetMemoryAllocate", EngineMarshallingDirection.Result,
                    "a null target address on failure", "a nonzero target address on failure");

            ThrowForAllocationOutcome(allocationOutcome.Operation);
        }

        if (address.IsZero)
            throw new EngineMarshallingException("TargetMemoryAllocate", EngineMarshallingDirection.Result,
                "a nonzero target address on success", "a null target address on success");

        return new AllocatedRegion(targetBound, address, request.Size, incarnation);
    }

    /// <summary>
    ///     Attempts an allocation while retaining a compact factual category for each expected or Engine-boundary
    ///     result.
    /// </summary>
    /// <param name="request">The allocation size, optional target base preference, and optional initial protection.</param>
    /// <returns>The structured allocation outcome and a nonzero target address on success.</returns>
    /// <remarks>
    ///     This additive API does not change the direct <see cref="ITargetMemoryAllocationOperations" /> contract.
    ///     It reports <see cref="EngineFailureKind.TargetIdentityUnavailable" /> when an implementation has not opted
    ///     into <see cref="ITargetBoundMemoryAllocationOperations" />, because creating an owner without a qualified
    ///     cleanup target would be unsafe. Lifecycle failures outside the Engine failure hierarchy still throw.
    /// </remarks>
    [RequiresPluginEnabled]
    public TargetMemoryAllocationOutcome AllocateWithOutcome(TargetAllocationRequest request)
    {
        if (request.Size.Value <= 0)
            return TargetMemoryAllocationOutcome.FromOperation(TargetMemoryOperationOutcome.FromFailureKind(
                EngineFailureKind.MarshallingFailure));

        if (_operations is not ITargetBoundMemoryAllocationOperations targetBound)
            return TargetMemoryAllocationOutcome.FromOperation(TargetMemoryOperationOutcome.FromFailureKind(
                EngineFailureKind.TargetIdentityUnavailable));

        try
        {
            var outcome = targetBound.AllocateBoundWithOutcome(request, out _, out var observation);
            if (!observation.IsQualified)
                return TargetMemoryAllocationOutcome.FromOperation(TargetMemoryOperationOutcome.FromFailureKind(
                    EngineFailureKind.TargetIdentityUnavailable));
            return outcome;
        }
        catch (EngineException exception)
        {
            return TargetMemoryAllocationOutcome.FromOperation(CreateOutcome(exception));
        }
    }

    internal static TargetMemoryOperationOutcome CreateOutcome(EngineException exception)
    {
        return exception is EngineLuaException lua
            ? TargetMemoryOperationOutcome.FromFailureKind(exception.Kind, lua.Status)
            : TargetMemoryOperationOutcome.FromFailureKind(exception.Kind);
    }

    private static void ThrowForAllocationOutcome(TargetMemoryOperationOutcome outcome)
    {
        if (outcome.Kind == TargetMemoryOperationOutcomeKind.ExpectedFailure)
            throw new EngineOperationFailedException("TargetMemoryAllocate");

        if (outcome.Kind == TargetMemoryOperationOutcomeKind.GlobalUnavailable)
            throw new EngineGlobalUnavailableException("TargetMemoryAllocate");

        if (outcome.Kind == TargetMemoryOperationOutcomeKind.CapabilityUnavailable)
            throw new EngineCapabilityUnavailableException("TargetMemoryAllocation");

        if (outcome.Kind == TargetMemoryOperationOutcomeKind.ProtectedLuaFailure)
            throw new EngineLuaException("TargetMemoryAllocate", outcome.LuaStatus);

        if (outcome.Kind == TargetMemoryOperationOutcomeKind.MarshallingFailure)
            throw new EngineMarshallingException("TargetMemoryAllocate", EngineMarshallingDirection.Result,
                "a target address or nil", "a result that is neither an address nor nil");

        if (outcome.Kind is TargetMemoryOperationOutcomeKind.TargetIdentityUnavailable or
            TargetMemoryOperationOutcomeKind.TargetIdentityMismatch)
        {
            throw new EngineTargetIdentityException("TargetMemoryAllocate", GetUnavailableTargetCheck());
        }

        throw new EngineBindingException("TargetMemoryAllocate");
    }

    private static TargetIdentityCheck GetUnavailableTargetCheck()
    {
        var observation = TargetSelectionObservation.FromStatus(
            TargetSelectionObservationStatus.CurrentTargetUnqualified);
        return TargetSelection.CreateUnavailableCheck(observation);
    }
}
