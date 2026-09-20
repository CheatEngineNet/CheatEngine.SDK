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
///         <b>Evidence.</b> The four pointer-sized init-record fields and the click callback's 4-byte result are
///         <i>ExactInstalledFile</i>: the CE 7.7.0.10621 <c>cepluginsdk.h</c> (SHA-256
///         <c>9C0E31BB753D782CE20710D19828F4E97B4371C8733ABD0C5C6F7F485306FB28</c>) and its Pascal SDK agree on that
///         record layout. The header declares the popup's third argument <c>BOOL*</c>, while pinned upstream
///         <c>plugin.pas</c> implements the host callback with a Pascal <c>PBool</c>. The effective CE 7.7 x64 width is
///         therefore <b>Unknown</b> until the required live canary proves it; this SDK does not infer a one-byte
///         representation from source text alone and keeps <see cref="CallbackOnPopup" /> opaque.
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

    /// <summary>
    ///     Invoked when the user picks the menu entry (offset 8). Argument: in/out, host-owned address currently
    ///     selected in the disassembler. The meaning of the result is not documented upstream.
    /// </summary>
    /// <remarks>Must stay valid until the function is unregistered. Must not let an exception escape.</remarks>
    public delegate* unmanaged[Stdcall]<nuint*, Bool32> Callback;

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
