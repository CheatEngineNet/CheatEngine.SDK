using System;
using System.Runtime.InteropServices;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Memory;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.SDK.Engine.Allocation;

/// <summary>
///     The explicit input to Cheat Engine's target-process <c>allocateMemory</c> global.
/// </summary>
/// <remarks>
///     CE 7.7 documents the Lua shape as <c>allocateMemory(size, BaseAddress OPTIONAL, Protection OPTIONAL)</c>.
///     <see cref="PreferredBaseAddress" /> is therefore a target <see cref="Address" />, never a host
///     <see cref="HostAddress" />;
///     <see cref="Protection" /> is passed only when present. The current CE 7.7 evidence does not document a separate
///     global that changes protection after allocation, so this contract intentionally models protection at allocation
///     time only.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly record struct TargetAllocationRequest
{
    /// <summary>
    ///     Initializes a request for target-process memory.
    /// </summary>
    /// <param name="size">The positive number of requested bytes.</param>
    /// <param name="preferredBaseAddress">An optional target address near which Cheat Engine should allocate.</param>
    /// <param name="protection">The optional CE <c>PAGE_*</c> protection supplied at allocation time.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size" /> is the default, zero, or negative value.</exception>
    public TargetAllocationRequest(TargetAllocationSize size, Address? preferredBaseAddress = null,
        MemoryProtection? protection = null)
    {
        if (size.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(size), size.Value, "An allocation size must be positive.");
        Size = size;
        PreferredBaseAddress = preferredBaseAddress;
        Protection = protection;
    }

    /// <summary>
    ///     Gets the requested byte count.
    /// </summary>
    public TargetAllocationSize Size { get; }

    /// <summary>
    ///     Gets the optional target-process base-address preference.
    /// </summary>
    public Address? PreferredBaseAddress { get; }

    /// <summary>
    ///     Gets the optional initial page protection.
    /// </summary>
    public MemoryProtection? Protection { get; }

}
