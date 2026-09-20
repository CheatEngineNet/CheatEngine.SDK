using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>A copied entry from Cheat Engine's <c>enumMemoryRegions</c> or <c>getMemoryRegionInfo</c> table.</summary>
/// <remarks>
///     Provenance: the CE 7.7.0.10621 <c>celua.txt</c> entries name <c>BaseAddress</c>, <c>AllocationBase</c>,
///     <c>AllocationProtect</c>, <c>RegionSize</c>, <c>State</c>, <c>Protect</c>, <c>Type</c>, and optional
///     <c>Extra</c>. <see cref="Extra" /> is <see langword="null" /> only when CE omitted the optional field; a present
///     non-string field is <see cref="InspectionStatus.InvalidResult" />, never silently converted. This snapshot owns
///     no CE object, and its addresses can be stale after a target-memory layout change.
/// </remarks>
/// <param name="BaseAddress">The base target-process address of the region.</param>
/// <param name="AllocationBase">The allocation-base target-process address reported by CE.</param>
/// <param name="AllocationProtection">The original allocation protection in Windows <c>PAGE_*</c> form.</param>
/// <param name="Size">The region size in bytes.</param>
/// <param name="State">The Windows <c>MEM_*</c> state.</param>
/// <param name="Protection">The current Windows <c>PAGE_*</c> protection.</param>
/// <param name="Type">The Windows <c>MEM_*</c> backing type.</param>
/// <param name="Extra">The optional mapped-file description, or <see langword="null" /> when omitted.</param>
public readonly record struct MemoryRegionInfo(
    Address BaseAddress,
    Address AllocationBase,
    MemoryProtection AllocationProtection,
    MemorySize Size,
    MemoryRegionState State,
    MemoryProtection Protection,
    MemoryRegionType Type,
    string? Extra);
