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

    /// <summary>Opaque address of the address-list callback (offset 8).</summary>
    /// <remarks>
    ///     The C declaration suggests a <c>stdcall</c> callback taking a selected-record pointer and returning a
    ///     four-byte <c>BOOL</c>. The historical Pascal declaration disagrees about the selected-record address width.
    ///     Keeping this slot untyped preserves the record layout while preventing an unsupported callback invocation.
    ///     Do not assign or invoke it until a CE 7.7 host canary qualifies the record and callback together.
    /// </remarks>
    public void* Callback;
}
