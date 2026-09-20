using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Engine.Runtime;

/// <summary>
///     Evidence-backed metadata for a capability's version, architecture, thread, ownership, and normal return
///     semantics.
/// </summary>
/// <remarks>
///     <see cref="RuntimeCapabilityAvailabilityState.Available" /> proves only that a capability was observed as
///     callable in one snapshot. It does not fill an unknown field or imply behavior on another Cheat Engine build.
/// </remarks>
/// <param name="MinimumCheatEngineVersion">
///     The lowest CE version supported by evidence, or <see langword="null" /> when
///     unknown.
/// </param>
/// <param name="ArchitectureScope">The process side to which <paramref name="ArchitectureRequirement" /> applies.</param>
/// <param name="ArchitectureRequirement">The required architecture, any architecture, or unknown.</param>
/// <param name="ThreadRequirement">The thread-affinity requirement, or unknown.</param>
/// <param name="Ownership">The ownership rule, or unknown.</param>
/// <param name="ReturnSemantics">The normal return shape, or unknown.</param>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct RuntimeCapabilityContract(
    CheatEngineVersion? MinimumCheatEngineVersion,
    RuntimeArchitectureScope ArchitectureScope,
    RuntimeArchitectureRequirement ArchitectureRequirement,
    RuntimeThreadRequirement ThreadRequirement,
    RuntimeOwnership Ownership,
    RuntimeReturnSemantics ReturnSemantics)
{
    /// <summary>Gets a contract whose evidence fields are all unknown.</summary>
    public static RuntimeCapabilityContract Unknown => default;
}
