using System.Runtime.InteropServices;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.SDK.Engine.Allocation;

/// <summary>
///     The factual outcome of one <c>allocateMemory</c> operation, including an address only after successful
///     allocation.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct TargetMemoryAllocationOutcome
{
    internal TargetMemoryAllocationOutcome(TargetMemoryOperationOutcome operation, Address address)
    {
        Operation = operation;
        Address = address;
    }

    /// <summary>Gets the allocation binding outcome.</summary>
    public TargetMemoryOperationOutcome Operation { get; }

    /// <summary>
    ///     Gets the allocated target address when <see cref="Operation" /> succeeded; otherwise
    ///     <see cref="Address.Zero" />.
    /// </summary>
    public Address Address { get; }

    /// <summary>Gets whether an allocation address is available.</summary>
    public bool IsSuccess => Operation.IsSuccess;

    internal static TargetMemoryAllocationOutcome Succeeded(Address address)
    {
        return new TargetMemoryAllocationOutcome(TargetMemoryOperationOutcome.Succeeded(), address);
    }

    internal static TargetMemoryAllocationOutcome FromOperation(TargetMemoryOperationOutcome operation)
    {
        return new TargetMemoryAllocationOutcome(operation, Address.Zero);
    }
}
