using System;
using System.Diagnostics.CodeAnalysis;
using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Values;

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
    /// <exception cref="EngineResourceHandoffException">
    ///     Cheat Engine accepted an allocation but an owner could not be published; <see cref="EngineResourceHandoffException.CleanupOutcome" />
    ///     records the one target-qualified compensation attempt or an unconfirmed effect when no address was available.
    /// </exception>
    [RequiresPluginEnabled]
    public AllocatedRegion Allocate(TargetAllocationRequest request)
    {
        return AllocateCore(request, CreateRegion);
    }

    // The factory is internal so tests can fail publication after the effect without allowing consumers to choose a
    // different ownership policy. Keep the target-bound tuple local until the owner has been published.
    internal AllocatedRegion AllocateCore(TargetAllocationRequest request, AllocatedRegionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (request.Size.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), request.Size.Value,
                "An allocation request must have a positive size.");

        if (_operations is not ITargetBoundMemoryAllocationOperations targetBound)
            throw new EngineTargetIdentityException("TargetMemoryAllocate", GetUnavailableTargetCheck());

        var allocationOutcome = targetBound.AllocateBoundWithOutcome(request, out var incarnation, out var observation);
        var allocated = allocationOutcome.IsSuccess;
        var address = allocationOutcome.Address;
        if (!allocated)
        {
            if (!observation.IsQualified)
                throw new EngineTargetIdentityException("TargetMemoryAllocate",
                    TargetSelection.CreateUnavailableCheck(observation));
            if (!address.IsZero)
                throw new EngineMarshallingException("TargetMemoryAllocate", EngineMarshallingDirection.Result,
                    "a null target address on failure", "a nonzero target address on failure");

            ThrowForAllocationOutcome(allocationOutcome.Operation);
        }

        if (address.IsZero)
            ThrowUnknownSuccessfulAllocation();

        if (!observation.IsQualified)
            ThrowUnqualifiedSuccessfulAllocation(observation);

        try
        {
            return factory(targetBound, address, request.Size, incarnation);
        }
        catch (Exception exception)
        {
            var cleanupOutcome = CompensateFailedPublication(targetBound, address, request.Size, incarnation);
            throw new EngineResourceHandoffException("TargetMemoryAllocate", cleanupOutcome, exception);
        }
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
            return TargetMemoryAllocationOutcome.Failed(TargetMemoryOperationOutcome.Failed(
                EngineFailureKind.MarshallingFailure));

        if (_operations is not ITargetBoundMemoryAllocationOperations targetBound)
            return TargetMemoryAllocationOutcome.Failed(TargetMemoryOperationOutcome.Failed(
                EngineFailureKind.TargetIdentityUnavailable));

        try
        {
            var outcome = targetBound.AllocateBoundWithOutcome(request, out _, out var observation);
            if (!observation.IsQualified)
                return TargetMemoryAllocationOutcome.Failed(TargetMemoryOperationOutcome.Failed(
                    EngineFailureKind.TargetIdentityUnavailable));
            return outcome;
        }
        catch (EngineException exception)
        {
            return TargetMemoryAllocationOutcome.Failed(CreateOutcome(exception));
        }
    }

    internal static TargetMemoryOperationOutcome CreateOutcome(EngineException exception)
    {
        return exception is EngineLuaException lua
            ? TargetMemoryOperationOutcome.Failed(exception.Kind, lua.Status)
            : TargetMemoryOperationOutcome.Failed(exception.Kind);
    }

    private static AllocatedRegion CreateRegion(ITargetBoundMemoryAllocationOperations operations, Address address,
        TargetAllocationSize size, TargetProcessIncarnation targetIncarnation)
    {
        return new AllocatedRegion(operations, address, size, targetIncarnation);
    }

    private static TargetReleaseOutcome CompensateFailedPublication(ITargetBoundMemoryAllocationOperations operations,
        Address address, TargetAllocationSize size, TargetProcessIncarnation targetIncarnation)
    {
        try
        {
            var outcome = operations.DeallocateBoundWithOutcome(targetIncarnation, address, size, out var targetCheck);
            if (!targetCheck.IsCurrent) return TargetReleaseOutcome.Refused(targetCheck);

            return outcome.IsSuccess
                ? TargetReleaseOutcome.Released()
                : TargetReleaseOutcome.Unconfirmed(outcome.FailureKind);
        }
        catch (EngineException exception)
        {
            return TargetReleaseOutcome.Unconfirmed(exception.Kind);
        }
        catch (Exception)
        {
            return TargetReleaseOutcome.Unconfirmed(failureKind: null);
        }
    }

    [DoesNotReturn]
    private static void ThrowUnknownSuccessfulAllocation()
    {
        var cause = new EngineMarshallingException("TargetMemoryAllocate", EngineMarshallingDirection.Result,
            "a nonzero target address on success", "a null target address on success");
        throw new EngineResourceHandoffException("TargetMemoryAllocate",
            TargetReleaseOutcome.Unconfirmed(EngineFailureKind.MarshallingFailure), cause);
    }

    [DoesNotReturn]
    private static void ThrowUnqualifiedSuccessfulAllocation(TargetSelectionObservation observation)
    {
        var check = TargetSelection.CreateUnavailableCheck(observation);
        var cause = new EngineTargetIdentityException("TargetMemoryAllocate", check);
        throw new EngineResourceHandoffException("TargetMemoryAllocate", TargetReleaseOutcome.Refused(check), cause);
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
