using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Abi.Native;

/// <summary>
///     Registration record for <see cref="PluginType.DisassemblerContext" /> (upstream type 6): an entry of the
///     disassembler view's context menu, with a second callback that runs when the menu opens.
/// </summary>
/// <remarks>
///     <para>
///         <b>Layout (64-bit): 32 bytes.</b> <see cref="Name" /> 0, <see cref="Callback" /> 8,
///         <see cref="CallbackOnPopup" /> 16, <see cref="Shortcut" /> 24.
///     </para>
///     <para>
///         <b>Evidence.</b> The four pointer-sized init-record fields are source-indexed and their x64 layout is
///         validated by the compiled C-header transcription. The C-header callback declarations are not a live ABI
///         qualification: the historical Pascal declarations differ in their address/boolean representations. In
///         particular, the header declares the popup's third argument <c>BOOL*</c>, while pinned upstream
///         <c>plugin.pas</c> implements the host callback with a Pascal <c>PBool</c>. The effective CE 7.7 x64 width is
///         therefore <b>Unknown</b> until the required live canary proves it; this SDK does not infer a one-byte
///         representation from source text alone and keeps both callback slots opaque.
///     </para>
///     <para>
///         Passed by address to the <c>RegisterFunction</c> slot of the classic table; the record only has to live for
///         the duration of that call. Native load path only.
///     </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct DisassemblerContextPluginInit
{
	/// <summary>NUL-terminated ANSI caption of the menu entry (offset 0).</summary>
	/// <remarks>The 7.5 host copies the text during registration (<i>inferred</i> for 7.7).</remarks>
	public byte* Name;

	/// <summary>Opaque address of the click callback (offset 8).</summary>
	/// <remarks>
	///     The C header uses a pointer-sized address and a four-byte <c>BOOL</c> result, while the historical Pascal
	///     declaration uses a different boolean representation. Keeping this slot untyped preserves the record layout
	///     without publishing a callback signature that no exact CE 7.7 host profile has qualified.
	/// </remarks>
	public void* Callback;

	/// <summary>
	///     Opaque address of the popup callback (offset 16).
	/// </summary>
	/// <remarks>
	///     The installed header declares <c>BOOL (stdcall *)(UINT_PTR, char**, BOOL*)</c>; the pinned Pascal host uses
	///     <c>PBool</c> for the final argument. Since neither establishes the actual CE 7.7 x64 pointee width, the slot
	///     has no callable managed signature. Do not assign or invoke it in a production plugin until the required live
	///     canary establishes the write boundary, callback result, and caption lifetime.
	/// </remarks>
	public void* CallbackOnPopup;

	/// <summary>NUL-terminated ANSI shortcut in text form (offset 24).</summary>
	/// <remarks>
	///     <para>
	///         Both upstream declarations type the field as a C string and the header describes the parsing as best-effort
	///         (<i>verified</i>); the shipped C example does not register this plugin type.
	///     </para>
	///     <para>
	///         Whether the host tolerates a null pointer here is <b>not established</b>: no local file says so and the 7.7
	///         host is closed source. Do not pass null. To register without a shortcut, point at an empty NUL-terminated
	///         string: that stays inside the declared contract (its effect, expected to be "no shortcut", is unverified).
	///     </para>
	/// </remarks>
	public byte* Shortcut;
}
