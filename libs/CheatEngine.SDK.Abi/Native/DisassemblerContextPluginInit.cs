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
///         <b>Evidence (verified, two sources agree on the ABI):</b> the type-6 init structure and the two callbacks of
///         <c>cepluginsdk.h</c> and of <c>cepluginsdk.pas</c> (CE 7.7.0.10621): four pointer-sized fields; both callbacks
///         <c>stdcall</c> with a 4-byte boolean result. The files name the third field differently and the Pascal unit
///         leaves the popup callback's second parameter untyped; neither difference affects the binary shape.
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
    ///     Invoked when the context menu is about to open (offset 16). Arguments: the selected address (by value), an
    ///     in/out pointer to the ANSI caption to display, an in/out 4-byte boolean deciding whether the entry is shown.
    ///     The meaning of the result is not documented upstream.
    /// </summary>
    /// <remarks>
    ///     A caption written through the second argument stays owned by the plugin and has to outlive the call; how
    ///     long the host keeps reading it is not documented. Must stay valid until the function is unregistered. Must
    ///     not let an exception escape.
    /// </remarks>
    public delegate* unmanaged[Stdcall]<nuint, byte**, Bool32*, Bool32> CallbackOnPopup;

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
