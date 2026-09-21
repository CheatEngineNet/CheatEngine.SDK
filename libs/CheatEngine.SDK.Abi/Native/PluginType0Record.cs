using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Abi.Native;

/// <summary>
///     The host-owned address-list selection record passed to a classic type-0 callback.
/// </summary>
/// <remarks>
///     <para>
///         This is deliberately internal. Its fields describe memory owned by Cheat Engine and the public type-0
///         callback remains <c>void*</c> until a hosting facade can make the borrowed lifetime and the C/Pascal
///         callback-record divergence explicit.
///     </para>
///     <para>
///         <b>Evidence status: source-indexed C header plus compiled-transcription fixture for x64 layout.</b> The
///         declaration is <c>PLUGINTYPE0_RECORD</c> in the pinned historical <c>cepluginsdk.h</c>. The MSVC x64
///         fixture validates this 48-byte transcription and its offsets, but does not establish which divergent Pascal
///         callback record a live host supplies. The corresponding Pascal SDK exposes a one-byte <c>boolean</c> pointer
///         flag, so it remains corroborating context rather than authority for a callable projection.
///     </para>
///     <para>
///         Strings are host-owned, NUL-terminated bytes. <see cref="Offsets" /> is host-owned and contains exactly
///         <see cref="CountOffsets" /> 32-bit values when that count is non-negative. The SDK neither retains nor
///         frees any pointer in this record.
///     </para>
/// </remarks>
[SuppressMessage("Meziantou.Analyzer", "MA0182",
    Justification =
        "This intentionally internal C-header mirror is retained as the type-0 callback ABI contract, exercised by friend-assembly layout tests, and verified against the native-fixture contract. It remains opaque until a safe hosting facade can own the borrowed record lifetime.")]
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PluginType0Record
{
    /// <summary>Host-owned NUL-terminated interpreted address text (offset 0).</summary>
    public byte* InterpretedAddress;

    /// <summary>Read-only pointer-sized target address (offset 8 on x64).</summary>
    public nuint Address;

    /// <summary>Read-only Win32 <c>BOOL</c> pointer flag (offset 16 on x64).</summary>
    public Bool32 IsPointer;

    /// <summary>Read-only count of <see cref="Offsets" /> entries (offset 20 on x64).</summary>
    public int CountOffsets;

    /// <summary>Host-owned read-only array of 32-bit offsets (offset 24 on x64).</summary>
    public uint* Offsets;

    /// <summary>Host-owned NUL-terminated description text (offset 32 on x64).</summary>
    public byte* Description;

    /// <summary>Host value-type discriminator (offset 40 on x64); its numeric semantics remain outside this ABI type.</summary>
    public byte ValueType;

    /// <summary>Host string- or bit-length byte (offset 41 on x64).</summary>
    public byte Size;
}
