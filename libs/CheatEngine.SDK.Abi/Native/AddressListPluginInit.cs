using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Abi.Native;

/// <summary>
///     Registration record for <see cref="PluginType.AddressList" /> (upstream type 0): a named menu entry that acts
///     on the selected address-list record.
/// </summary>
/// <remarks>
///     <para><b>Layout (64-bit): 16 bytes.</b> <see cref="Name" /> 0, <see cref="Callback" /> 8.</para>
///     <para>
///         <b>Evidence.</b> The type-0 init structure's two pointer-sized fields are source-indexed and its x64 layout
///         is validated by the compiled C-header transcription. The C callback declaration is compiled only as part of
///         that transcription; it does not qualify a live callback boundary.
///     </para>
///     <para>
///         <b>The selection record the callback receives.</b> Its oracle is the host type
///         <c>TPlugin0_SelectedRecord</c> of the pinned <c>plugin.pas</c> (lines 726-735, <c>address: ptrUint</c>,
///         <c>ispointer: BOOL</c>), which agrees with the C header and is mirrored by the internal
///         <c>PluginType0Record</c>. The Pascal kit unit's two variants (<c>address: dword</c>, and a one-byte
///         <c>ispointer: boolean</c>) are known-wrong for x64 (see <c>libs/CheatEngine.SDK.Abi/README.md</c>). What the CE
///         7.7.0.10621
///         binary passes is still <b>not observed</b> (no managed-hostfxr route registers a type-0 function), so the
///         callback stays <c>void*</c>: no callable signature is published.
///     </para>
///     <para>
///         Passed by address to the <c>RegisterFunction</c> slot of the classic table; the record itself only has to
///         live for the duration of that call. Native load path only.
///     </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct AddressListPluginInit
{
	/// <summary>NUL-terminated ANSI caption of the menu entry (offset 0).</summary>
	/// <remarks>
	///     The 7.5 host copies the text during registration (<i>inferred</i> for 7.7); keeping the buffer alive longer is
	///     harmless.
	/// </remarks>
	public byte* Name;

	/// <summary>Opaque address of the address-list callback (offset 8).</summary>
	/// <remarks>
	///     The C declaration suggests a <c>stdcall</c> callback taking a selected-record pointer and returning a
	///     four-byte <c>BOOL</c>. The historical Pascal declaration disagrees about the selected-record address width.
	///     Keeping this slot untyped preserves the record layout while preventing an unsupported callback invocation.
	///     Do not assign or invoke it until a CE 7.7 host canary qualifies the record and callback together.
	/// </remarks>
	public void* Callback;
}
