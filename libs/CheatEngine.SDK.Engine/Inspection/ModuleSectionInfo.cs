using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>A copied section entry from Cheat Engine's <c>enumSectionsOfModule</c> table.</summary>
/// <remarks>
///     Provenance: CE 7.7.0.10621 <c>celua.txt</c> documents the <c>Name</c>, <c>Size</c>, <c>Address</c> and
///     <c>FileAddress</c> fields. <see cref="FileOffset" /> is intentionally not an <see cref="Address" /> because CE
///     defines it as the address in the module file on disk. This is a borrowed-free snapshot; it owns no CE resource
///     and can become stale when the selected module unloads.
/// </remarks>
/// <param name="Name">The section name.</param>
/// <param name="Size">The section size in bytes.</param>
/// <param name="Address">The section's current target-process address.</param>
/// <param name="FileOffset">The section's byte offset in the module file.</param>
public readonly record struct ModuleSectionInfo(
    string Name,
    MemorySize Size,
    Address Address,
    ModuleFileOffset FileOffset);
