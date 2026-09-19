using System.Runtime.InteropServices;

namespace CESDK.Abi.Native;

/// <summary>
///     Registration record for <see cref="PluginType.MainMenu" /> (upstream type 5): a named entry in the plugin menu
///     of the main window, with a keyboard shortcut given as text.
/// </summary>
/// <remarks>
///     <para>
///         <b>Layout (64-bit): 24 bytes.</b> <see cref="Name" /> 0, <see cref="Callback" /> 8, <see cref="Shortcut" /> 16.
///     </para>
///     <para>
///         <b>Evidence (verified, two sources agree):</b> the type-5 init structure and callback of <c>cepluginsdk.h</c>
///         and of <c>cepluginsdk.pas</c> (CE 7.7.0.10621): three pointer-sized fields; callback without arguments and
///         without result, <c>stdcall</c>.
///     </para>
///     <para>
///         Passed by address to the <c>RegisterFunction</c> slot of the classic table; the record only has to live for
///         the duration of that call. Native load path only.
///     </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct MainMenuPluginInit
{
    /// <summary>NUL-terminated ANSI caption of the menu entry (offset 0).</summary>
    /// <remarks>The 7.5 host copies the text during registration (<i>inferred</i> for 7.7).</remarks>
    public byte* Name;

    /// <summary>Invoked when the user picks the menu entry (offset 8).</summary>
    /// <remarks>Must stay valid until the function is unregistered. Must not let an exception escape.</remarks>
    public delegate* unmanaged[Stdcall]<void> Callback;

    /// <summary>NUL-terminated ANSI shortcut in text form, for example <c>Ctrl+R</c> (offset 16).</summary>
    /// <remarks>
    ///     <para>
    ///         Both upstream declarations type the field as a C string and the header describes the parsing as best-effort
    ///         (<i>verified</i>); the shipped C example always passes text.
    ///     </para>
    ///     <para>
    ///         Whether the host tolerates a null pointer here is <b>not established</b>: no local file says so and the 7.7
    ///         host is closed source. Do not pass null. To register without a shortcut, point at an empty NUL-terminated
    ///         string: that stays inside the declared contract (its effect, expected to be "no shortcut", is unverified).
    ///     </para>
    /// </remarks>
    public byte* Shortcut;
}
