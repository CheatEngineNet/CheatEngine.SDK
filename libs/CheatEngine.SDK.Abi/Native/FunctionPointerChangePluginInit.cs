using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Abi.Native;

/// <summary>
///     Registration record for <see cref="PluginType.FunctionPointerChange" /> (upstream type 4): notification that
///     one of the API hook slots of the classic exported-functions table was reassigned.
/// </summary>
/// <remarks>
///     <para><b>Layout (64-bit): 8 bytes.</b> <see cref="Callback" /> 0.</para>
///     <para>
///         <b>Evidence.</b> Record layout (one pointer-sized field): <c>cepluginsdk.h</c> and <c>cepluginsdk.pas</c>
///         (CE 7.7.0.10621) agree - <i>verified</i>.
///     </para>
///     <para>
///         <b>Callback signature: disputed, therefore untyped.</b> Both files agree on <c>stdcall</c> and on a single
///         32-bit integer argument; the header declares no result, the Pascal unit declares a 1-byte boolean result.
///         It is not established which declaration the host follows, so the slot is a plain pointer: the caller casts
///         its function pointer knowingly.
///     </para>
///     <para>Passed by address to the <c>RegisterFunction</c> slot of the classic table. Native load path only.</para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct FunctionPointerChangePluginInit
{
    /// <summary>Address of the <c>stdcall</c> callback (offset 0). Untyped, see the type remarks.</summary>
    /// <remarks>Must stay valid until the function is unregistered. Must not let an exception escape.</remarks>
    public void* Callback;
}
