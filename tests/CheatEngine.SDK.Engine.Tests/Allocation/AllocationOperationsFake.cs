using CheatEngine.SDK.Engine.Allocation;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.SDK.Engine.Tests.Allocation;

/// <summary>
///     Deterministic managed implementation of the allocation boundary. It deliberately models only the public
///     contract, not a Lua fixture or a live Cheat Engine process.
/// </summary>
internal sealed class AllocationOperationsFake : ITargetMemoryAllocationOperations
{
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
}
