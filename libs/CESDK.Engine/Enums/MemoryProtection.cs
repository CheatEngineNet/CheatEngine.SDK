using System;

namespace CESDK.Engine.Enums;

/// <summary>
///     Page protection of a memory region, in the Windows <c>PAGE_*</c> encoding that <c>defines.lua</c> exposes:
///     the <c>Protect</c> and <c>AllocationProtect</c> fields of the regions returned by <c>enumMemoryRegions()</c>,
///     and the <c>Protection</c> argument of <c>allocateMemory</c>.
/// </summary>
/// <remarks>
///     <para>
///         Values verified against <c>defines.lua</c> of Cheat Engine 7.7.0.10621, which lists the seven access values and
///         none of the modifier bits (<c>PAGE_GUARD</c>, <c>PAGE_NOCACHE</c>, <c>PAGE_WRITECOMBINE</c>) nor
///         <c>PAGE_NOACCESS</c>; a region can still report those bits, which then appear as an undefined combination. The
///         enum is a flags enum because the operating system combines an access value with modifier bits;
///         <see cref="None" /> is the C# zero member (a free region reports 0), not a CE define.
///     </para>
///     <para>
///         Not to be confused with the <c>protectionflags</c> <i>string</i> of <c>firstScan</c> and <c>AOBScan</c>
///         (<c>"+W-C"</c>: writable, not copy-on-write), which is a scan preference, not a page protection.
///     </para>
/// </remarks>
[Flags]
public enum MemoryProtection : uint
{
    /// <summary>No protection value reported (a free or reserved region). Not a CE define.</summary>
    None = 0,

    /// <summary>Read-only. CE: <c>PAGE_READONLY</c>.</summary>
    ReadOnly = 2,

    /// <summary>Read and write. CE: <c>PAGE_READWRITE</c>.</summary>
    ReadWrite = 4,

    /// <summary>Copy-on-write. CE: <c>PAGE_WRITECOPY</c>.</summary>
    WriteCopy = 8,

    /// <summary>Execute only. CE: <c>PAGE_EXECUTE</c>.</summary>
    Execute = 16,

    /// <summary>Execute and read. CE: <c>PAGE_EXECUTE_READ</c>.</summary>
    ExecuteRead = 32,

    /// <summary>Execute, read and write. CE: <c>PAGE_EXECUTE_READWRITE</c>.</summary>
    ExecuteReadWrite = 64,

    /// <summary>Execute and copy-on-write. CE: <c>PAGE_EXECUTE_WRITECOPY</c>.</summary>
    ExecuteWriteCopy = 128
}
