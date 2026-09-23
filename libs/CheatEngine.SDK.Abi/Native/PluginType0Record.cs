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
///         <b>Oracle: the host type actually called.</b> The pinned host source passes a
///         <c>TPlugin0_SelectedRecord</c> declared in <c>plugin.pas</c> lines 726-735 (commit <c>ec45d5f</c>, SHA-256
///         <c>358f51a39ad14d00ecba3c9137f440152d4ab85f1d2498068fa81fca906d09db</c>): <c>address: ptrUint</c> and
///         <c>ispointer: BOOL</c>. It agrees field by field with the C header <c>PLUGINTYPE0_RECORD</c>
///         (<c>cepluginsdk.h</c> lines 27-37), and this record matches both: offsets 0/8/16/20/24/32/40/41, widths
///         8/8/4/4/8/8/1/1, 48 bytes (evidence <c>Deduced</c> from the declarations).
///     </para>
///     <para>
///         <b>Known-wrong mirrors.</b> The Pascal kit unit <c>cepluginsdk.pas</c> (lines 161-170) declares a record
///         of the same name with <c>address: dword</c>, which moves <c>ispointer</c> to 12 and <c>countoffsets</c> to
///         16 while keeping 48 bytes, and a <c>TSelectedRecord</c> (lines 147-156) with a one-byte
///         <c>ispointer: boolean</c> at the host offsets. Neither is x64 authority; a size check alone cannot tell them
///         apart (per-field table in <c>libs/CheatEngine.SDK.Abi/README.md</c>, tests <c>SelectedRecordOracleTests</c> and
///         the
///         compiled fixture facts <c>host_plugin0_selected_record</c>, <c>pascal_dword_mirror_selected_record</c>,
///         <c>pascal_boolean_mirror_selected_record</c>).
///     </para>
///     <para>
///         <b>Not observed on the CE 7.7 binary.</b> No managed-hostfxr route reaches a type-0 registration, so which
///         record the 7.7.0.10621 binary passes stays <c>NotObserved</c>; the type-0
///         <see cref="AddressListPluginInit.Callback" /> therefore remains <c>void*</c> and no callable signature exists.
///     </para>
///     <para>
///         Strings are host-owned, NUL-terminated bytes. <see cref="Offsets" /> is host-owned and contains exactly
///         <see cref="CountOffsets" /> 32-bit values when that count is non-negative. The SDK neither retains nor
///         frees any pointer in this record.
///     </para>
/// </remarks>
[SuppressMessage("Meziantou.Analyzer", "MA0182",
	Justification =
		"This intentionally internal record is the type-0 callback ABI contract (host type of plugin.pas, equal to the C header), exercised by friend-assembly layout and oracle tests, and verified against the native-fixture contract. It remains opaque until a safe hosting facade can own the borrowed record lifetime.")]
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
