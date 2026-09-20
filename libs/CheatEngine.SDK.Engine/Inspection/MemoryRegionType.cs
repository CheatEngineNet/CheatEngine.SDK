namespace CheatEngine.SDK.Engine.Inspection;

/// <summary>The Windows virtual-memory backing type copied from a Cheat Engine memory-region record.</summary>
/// <remarks>
///     CE 7.7 returns the native <c>MEMORY_BASIC_INFORMATION.Type</c> value in the <c>Type</c> field documented by
///     <c>enumMemoryRegions</c>. Values follow the Windows <c>MEM_*</c> encoding; unknown future values remain intact.
/// </remarks>
public enum MemoryRegionType : uint
{
    /// <summary>Private committed pages (<c>MEM_PRIVATE</c>).</summary>
    Private = 0x20000,

    /// <summary>Mapped-file pages (<c>MEM_MAPPED</c>).</summary>
    Mapped = 0x40000,

    /// <summary>Image-mapped pages (<c>MEM_IMAGE</c>).</summary>
    Image = 0x1000000,
}
