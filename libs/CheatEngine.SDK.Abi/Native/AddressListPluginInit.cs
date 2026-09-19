using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Abi.Native;

/// <summary>
///     Registration record for <see cref="PluginType.AddressList" /> (upstream type 0): a named menu entry that acts
///     on the selected address-list record.
/// </summary>
/// <remarks>
///     <para><b>Layout (64-bit): 16 bytes.</b> <see cref="Name" /> 0, <see cref="Callback" /> 8.</para>
///     <para>
///         <b>Evidence.</b> Record layout: the type-0 init structure of <c>cepluginsdk.h</c> and the matching record of
///         <c>cepluginsdk.pas</c> (CE 7.7.0.10621) agree on two pointer-sized fields in this order - <i>verified</i>.
///         Callback shape (one record pointer in, 4-byte boolean out, <c>stdcall</c>): both files agree -
///         <i>verified</i>.
///     </para>
///     <para>
///         <b>Not mapped: the selection record the callback receives.</b> The header declares its address field
///         pointer-sized, the Pascal unit's callback record declares it 32-bit (and the unit carries a second variant with
///         a 1-byte pointer flag). The two layouts differ on 64-bit from the second field on and it is not established
///         which one the host follows, so the parameter stays <c>void*</c>.
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

    /// <summary>
    ///     Invoked when the user picks the menu entry (offset 8). Argument: the selection record (layout disputed,
    ///     see the type remarks). Result: true when the callback changed the record and the host should apply the
    ///     change to the table (stated by the official C sample plugin).
    /// </summary>
    /// <remarks>Must stay valid until the function is unregistered. Must not let an exception escape.</remarks>
    public delegate* unmanaged[Stdcall]<void*, Bool32> Callback;
}
