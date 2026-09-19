using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Abi.Native;

/// <summary>
///     Registration record for <see cref="PluginType.MemoryView" /> (upstream type 1): a named menu entry of the
///     memory view window, with a keyboard shortcut given as text.
/// </summary>
/// <remarks>
///     <para>
///         <b>Layout (64-bit): 24 bytes.</b> <see cref="Name" /> 0, <see cref="Callback" /> 8, <see cref="Shortcut" /> 16.
///     </para>
///     <para>
///         <b>Evidence (verified, two sources agree):</b> the type-1 init structure and callback of <c>cepluginsdk.h</c>
///         and of <c>cepluginsdk.pas</c> (CE 7.7.0.10621): three pointer-sized fields; callback taking three pointers to
///         pointer-sized unsigned integers and returning a 4-byte boolean, <c>stdcall</c>.
///     </para>
///     <para>
///         Passed by address to the <c>RegisterFunction</c> slot of the classic table; the record only has to live for
///         the duration of that call. Native load path only.
///     </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct MemoryViewPluginInit
{
    /// <summary>NUL-terminated ANSI caption of the menu entry (offset 0).</summary>
    /// <remarks>The 7.5 host copies the text during registration (<i>inferred</i> for 7.7).</remarks>
    public byte* Name;

    /// <summary>
    ///     Invoked when the user picks the menu entry (offset 8). Arguments, all in/out and host-owned: the
    ///     disassembler view's top address, the disassembler view's selected address, the hex view's address.
    /// </summary>
    /// <remarks>
    ///     Writing through the pointers moves the views; whether the result gates that update is not documented
    ///     upstream. Must stay valid until the function is unregistered. Must not let an exception escape.
    /// </remarks>
    public delegate* unmanaged[Stdcall]<nuint*, nuint*, nuint*, Bool32> Callback;

    /// <summary>NUL-terminated ANSI shortcut in text form, for example <c>Ctrl+Q</c> (offset 16).</summary>
    /// <remarks>
    ///     <para>
    ///         Both upstream declarations type the field as a C string and the header describes the parsing as best-effort
    ///         (<i>verified</i>); the shipped C example always passes text. The 7.5 host reads this field only when the
    ///         plugin reports a version above 1 (<i>inferred</i> for 7.7).
    ///     </para>
    ///     <para>
    ///         Whether the host tolerates a null pointer here is <b>not established</b>: no local file says so and the 7.7
    ///         host is closed source. Do not pass null. To register without a shortcut, point at an empty NUL-terminated
    ///         string: that stays inside the declared contract (its effect, expected to be "no shortcut", is unverified).
    ///     </para>
    /// </remarks>
    public byte* Shortcut;
}
