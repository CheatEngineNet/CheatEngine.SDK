using CheatEngine.SDK.Engine.Allocation;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.SDK.Engine.Tests.Allocation;

/// <summary>
///     Deterministic managed implementation of the allocation boundary. It deliberately models only the public
///     contract, not a Lua fixture or a live Cheat Engine process.
/// </summary>
internal sealed class AllocationOperationsFake : ITargetMemoryAllocationOperations, ITargetBoundMemoryAllocationOperations
{
    private static readonly TargetProcessIncarnation STarget = new(4242, 1);

    public Address AllocatedAddress { get; set; } = new(0x7FF6_1000_0000);

    public bool AllocationResult { get; set; } = true;

    public Exception? AllocationException { get; set; }

    public bool DeallocationResult { get; set; } = true;

    public Exception? DeallocationException { get; set; }

    public int AllocateCalls { get; private set; }

    public int DeallocateCalls { get; private set; }

    public TargetAllocationRequest LastRequest { get; private set; }

    public Address LastDeallocatedAddress { get; private set; }

    public TargetAllocationSize LastDeallocatedSize { get; private set; }

    public TargetSelectionObservation TargetObservation { get; set; } = TargetSelectionObservation.Qualified(STarget);

    public TargetMemoryAllocationOutcome? BoundAllocationOutcomeOverride { get; set; }

    public bool TryAllocate(TargetAllocationRequest request, out Address address)
    {
        AllocateCalls++;
        LastRequest = request;
        if (AllocationException is not null) throw AllocationException;

        address = AllocatedAddress;
        return AllocationResult;
    }

    public bool TryDeallocate(Address address, TargetAllocationSize size)
    {
        DeallocateCalls++;
        LastDeallocatedAddress = address;
        LastDeallocatedSize = size;
        if (DeallocationException is not null) throw DeallocationException;

        return DeallocationResult;
    }

    public TargetMemoryAllocationOutcome AllocateBoundWithOutcome(TargetAllocationRequest request,
        out TargetProcessIncarnation incarnation, out TargetSelectionObservation observation)
    {
        observation = TargetObservation;
        incarnation = observation.Incarnation.GetValueOrDefault();
        if (!observation.IsQualified)
            return TargetMemoryAllocationOutcome.Failed(TargetMemoryOperationOutcome.Failed(
                EngineFailureKind.TargetIdentityUnavailable));

        if (BoundAllocationOutcomeOverride.HasValue)
            return BoundAllocationOutcomeOverride.GetValueOrDefault();

        var allocated = TryAllocate(request, out var address);

        // Preserve caller-provided shapes so boundary tests can deliberately exercise
        // malformed native results which public factories rightly reject.
        return allocated
            ? new TargetMemoryAllocationOutcome(TargetMemoryOperationOutcome.Succeeded(), address)
            : new TargetMemoryAllocationOutcome(
                TargetMemoryOperationOutcome.Failed(EngineFailureKind.ExpectedOperationFailure),
                address);
    }

    public bool TryDeallocateBound(TargetProcessIncarnation expected, Address address, TargetAllocationSize size,
        out TargetIdentityCheck targetCheck)
    {
        var outcome = DeallocateBoundWithOutcome(expected, address, size, out targetCheck);
        return targetCheck.IsCurrent && outcome.IsSuccess;
    }

    public TargetMemoryOperationOutcome DeallocateBoundWithOutcome(TargetProcessIncarnation expected, Address address,
        TargetAllocationSize size, out TargetIdentityCheck targetCheck)
    {
        targetCheck = GetTargetCheck(expected, TargetObservation);
        if (!targetCheck.IsCurrent)
            return TargetMemoryOperationOutcome.Failed(targetCheck.Kind is TargetIdentityCheckKind.TargetChanged
                or TargetIdentityCheckKind.ProcessReused
                ? EngineFailureKind.TargetIdentityMismatch
                : EngineFailureKind.TargetIdentityUnavailable);

        return TryDeallocate(address, size)
            ? TargetMemoryOperationOutcome.Succeeded()
            : TargetMemoryOperationOutcome.Failed(EngineFailureKind.ExpectedOperationFailure);
    }

    private static TargetIdentityCheck GetTargetCheck(TargetProcessIncarnation expected,
        TargetSelectionObservation observation)
    {
        if (!observation.IsQualified)
            return TargetSelection.CreateUnavailableCheck(observation);

        var current = observation.Incarnation.GetValueOrDefault();
        if (current.ProcessId != expected.ProcessId)
            return new TargetIdentityCheck(TargetIdentityCheckKind.TargetChanged, observation);

        return current.StartedAtUtcTicks == expected.StartedAtUtcTicks
            ? new TargetIdentityCheck(TargetIdentityCheckKind.Current, observation)
            : new TargetIdentityCheck(TargetIdentityCheckKind.ProcessReused, observation);
    }
}
