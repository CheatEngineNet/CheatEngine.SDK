using System;
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

    /// <summary>Creates a successful allocation outcome for a nonzero target address.</summary>
    /// <param name="address">The nonzero target address returned by the allocation operation.</param>
    /// <returns>A successful allocation outcome.</returns>
    /// <exception cref="ArgumentException"><paramref name="address" /> is <see cref="Address.Zero" />.</exception>
    public static TargetMemoryAllocationOutcome Succeeded(Address address)
    {
        if (address.IsZero)
            throw new ArgumentException("A successful allocation outcome requires a nonzero target address.", nameof(address));

        return new TargetMemoryAllocationOutcome(TargetMemoryOperationOutcome.Succeeded(), address);
    }

    /// <summary>Creates a specified unsuccessful allocation outcome without an address.</summary>
    /// <param name="operation">The specified non-success allocation operation outcome.</param>
    /// <returns>An unsuccessful allocation outcome whose <see cref="Address" /> is <see cref="Address.Zero" />.</returns>
    /// <exception cref="ArgumentException"><paramref name="operation" /> is successful or unspecified.</exception>
    public static TargetMemoryAllocationOutcome Failed(TargetMemoryOperationOutcome operation)
    {
        if (operation.IsSuccess || operation.Kind == TargetMemoryOperationOutcomeKind.Unspecified)
            throw new ArgumentException("An unsuccessful allocation outcome requires a specified failure.", nameof(operation));

        return new TargetMemoryAllocationOutcome(operation, Address.Zero);
    }
}
