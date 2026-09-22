using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Abi.Native;

/// <summary>
///     Registration record for <see cref="PluginType.ProcessWatcherEvent" /> (upstream type 3): notification of
///     process creation and termination seen by the kernel-mode process watcher.
/// </summary>
/// <remarks>
///     <para><b>Layout (64-bit): 8 bytes.</b> <see cref="Callback" /> 0.</para>
///     <para>
///         <b>Evidence.</b> Record layout (one pointer-sized field): <c>cepluginsdk.h</c> and <c>cepluginsdk.pas</c>
///         (CE 7.7.0.10621) agree - <i>verified</i>.
///     </para>
///     <para>
///         <b>Callback signature: disputed, therefore untyped.</b> The header declares no result and a 32-bit second
///         argument; the Pascal unit declares a 32-bit integer result and a pointer-sized second argument. Both agree on
///         <c>stdcall</c>, on a 32-bit process id first and a 4-byte boolean "created" flag last. It is not established
///         which declaration the host follows, so the slot is a plain pointer: the caller casts its function pointer
///         knowingly.
///     </para>
///     <para>Passed by address to the <c>RegisterFunction</c> slot of the classic table. Native load path only.</para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ProcessWatcherPluginInit
{
	/// <summary>Address of the <c>stdcall</c> callback (offset 0). Untyped, see the type remarks.</summary>
	/// <remarks>
	///     Runs on a thread other than the main thread (stated by the official C sample plugin): no GUI work. Must
	///     stay valid until the function is unregistered. Must not let an exception escape.
	/// </remarks>
	public void* Callback;
}
