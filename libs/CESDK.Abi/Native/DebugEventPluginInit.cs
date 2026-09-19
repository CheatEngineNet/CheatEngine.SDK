using System.Runtime.InteropServices;

namespace CESDK.Abi.Native;

/// <summary>
///     Registration record for <see cref="PluginType.OnDebugEvent" /> (upstream type 2): a filter that sees debug
///     events before Cheat Engine's own debugger handles them.
/// </summary>
/// <remarks>
///     <para><b>Layout (64-bit): 8 bytes.</b> <see cref="Callback" /> 0.</para>
///     <para>
///         <b>Evidence (verified, two sources agree):</b> the type-2 init structure and callback of <c>cepluginsdk.h</c>
///         and of <c>cepluginsdk.pas</c> (CE 7.7.0.10621): one pointer-sized field; callback taking a pointer to the
///         Win32 debug-event structure and returning a 32-bit integer, <c>stdcall</c>.
///     </para>
///     <para>
///         Passed by address to the <c>RegisterFunction</c> slot of the classic table. Native load path only.
///     </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct DebugEventPluginInit
{
    /// <summary>
    ///     Invoked for each debug event (offset 0). Argument: pointer to the operating system's <c>DEBUG_EVENT</c>
    ///     structure (an OS type, deliberately not mapped here). Result: 0 lets Cheat Engine handle the event; 1
    ///     means the plugin handled it and is then responsible for continuing the debug event itself (stated by the
    ///     official C sample plugin).
    /// </summary>
    /// <remarks>
    ///     Runs on a thread other than the main thread (stated by the official C sample plugin): no GUI work. Must
    ///     stay valid until the function is unregistered. Must not let an exception escape.
    /// </remarks>
    public delegate* unmanaged[Stdcall]<void*, int> Callback;
}
