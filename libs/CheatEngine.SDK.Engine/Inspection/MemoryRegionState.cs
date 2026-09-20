namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>The Windows virtual-memory state copied from a Cheat Engine memory-region record.</summary>
/// <remarks>
///     CE 7.7 returns the native <c>MEMORY_BASIC_INFORMATION.State</c> value in the <c>State</c> field documented by
///     <c>enumMemoryRegions</c>. Values follow the Windows <c>MEM_*</c> encoding; unknown future values are preserved by
///     the enum's underlying <see cref="uint" /> rather than normalized.
/// </remarks>
public enum MemoryRegionState : uint
{
    /// <summary>Pages are committed (<c>MEM_COMMIT</c>).</summary>
    Committed = 0x1000,

    /// <summary>Pages are reserved (<c>MEM_RESERVE</c>).</summary>
    Reserved = 0x2000,

    /// <summary>The address range is free (<c>MEM_FREE</c>).</summary>
    Free = 0x10000,
}
