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
///         <b>Evidence status: ExactInstalledFile for fields; InferredUntilFixture for x64 offsets.</b> The declaration is
///         <c>PLUGINTYPE0_RECORD</c> in the
///         <c>cepluginsdk.h</c> distributed with Cheat Engine 7.7.0.10621 x64 (SHA-256
///         <c>9C0E31BB753D782CE20710D19828F4E97B4371C8733ABD0C5C6F7F485306FB28</c>). The file has natural C layout;
///         the asserted x64 offsets use the Windows x64 ABI. The corresponding Pascal SDK also has a selected-record
///         shape, but exposes a one-byte <c>boolean</c> pointer flag and a separately divergent callback record, so it
///         is corroborating context rather than the authority for this internal C-header mirror.
///     </para>
///     <para>
///         Strings are host-owned, NUL-terminated bytes. <see cref="Offsets" /> is host-owned and contains exactly
///         <see cref="CountOffsets" /> 32-bit values when that count is non-negative. The SDK neither retains nor
///         frees any pointer in this record.
///     </para>
/// </remarks>
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
