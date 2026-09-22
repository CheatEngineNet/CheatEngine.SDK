using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Abi.Native;

/// <summary>
///     Registration record for <see cref="PluginType.DisassemblerRenderLine" /> (upstream type 7): a hook that can
///     replace the text columns and the colour of each line the disassembler view draws.
/// </summary>
/// <remarks>
///     <para><b>Layout (64-bit): 8 bytes.</b> <see cref="Callback" /> 0.</para>
///     <para>
///         <b>Evidence (verified, two sources agree on the ABI):</b> the type-7 init structure and callback of
///         <c>cepluginsdk.h</c> and of <c>cepluginsdk.pas</c> (CE 7.7.0.10621): one pointer-sized field; callback
///         <c>stdcall</c>, no result, six arguments (a pointer-sized address by value, four pointers to string pointers,
///         one pointer to a 32-bit colour). The Pascal unit leaves the four string-pointer parameters untyped; the binary
///         shape is identical.
///     </para>
///     <para>Passed by address to the <c>RegisterFunction</c> slot of the classic table. Native load path only.</para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct DisassemblerRenderLinePluginInit
{
	/// <summary>
	///     Invoked for every rendered line (offset 0). Arguments: the line's address; in/out pointers to the ANSI
	///     texts of the address, bytes, opcode and extra columns; in/out pointer to the 32-bit text colour.
	/// </summary>
	/// <remarks>
	///     Hot path of the GUI: runs on the main thread for each visible line on each repaint (<i>inferred</i>).
	///     Replacement strings stay owned by the plugin and have to outlive the call. Must stay valid until the
	///     function is unregistered. Must not let an exception escape.
	/// </remarks>
	public delegate* unmanaged[Stdcall]<nuint, byte**, byte**, byte**, byte**, uint*, void> Callback;
}
