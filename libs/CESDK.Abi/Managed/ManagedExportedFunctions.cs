using System.Runtime.InteropServices;

namespace CESDK.Abi.Managed;

/// <summary>
///     The exported-functions record Cheat Engine passes to the enable callback of a <b>managed</b> plugin
///     (<see cref="PluginInitRecord.EnablePlugin" />). It is <b>not</b> the classic native table: it has six fields, and
///     everything else a managed plugin needs is reached through Lua.
/// </summary>
/// <remarks>
///     <para>
///         <b>Layout (64-bit): 48 bytes, natural alignment.</b> <see cref="SizeOfExportedFunctions" /> 0 (followed by 4
///         bytes of padding), <see cref="GetLuaState" /> 8, <see cref="LuaRegister" /> 16,
///         <see cref="LuaPushClassInstance" /> 24, <see cref="ProcessMessages" /> 32, <see cref="CheckSynchronize" /> 40.
///     </para>
///     <para>
///         <b>Evidence.</b> Field order, field count and widths: the public exported-functions structure of the official
///         managed bootstrap (<c>c# template/SDK/CESDK.cs</c>, CE 7.7.0.10621), declared sequential without packing -
///         <i>verified</i>. Host-side meaning and conventions of each slot: public 7.5 host source - <i>inferred</i>
///         for 7.7, whose host is closed source. The <c>stdcall</c> convention of slots 1, 3, 4 and 5 is additionally
///         <i>verified</i> by the delegate declarations of the official Lua binding
///         (<c>c# template/SDK/CESDKLua.cs</c>) and bootstrap.
///     </para>
///     <para>
///         <b>Usage contract.</b> The pointer received by the enable callback refers to a local variable of the host:
///         copy the record by value during the call and never retain the pointer. Honour
///         <see cref="SizeOfExportedFunctions" />: a record shorter than this structure must be rejected, bytes beyond
///         it are unknown future fields and must be ignored. After the copy the function pointers stay valid for the
///         lifetime of the process (they are addresses of host code).
///     </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ManagedExportedFunctions
{
    /// <summary>
    ///     Byte size of the record as the host sees it (offset 0). 48 for the only known revision on 64-bit.
    /// </summary>
    public int SizeOfExportedFunctions;

    /// <summary>
    ///     Returns the <c>lua_State*</c> that belongs to the <b>calling OS thread</b> (offset 8). No arguments.
    /// </summary>
    /// <remarks>
    ///     The result is typed <c>void*</c> because this assembly references nothing; cast it to the Lua state type of
    ///     <c>CESDK.Lua.Interop</c>. The host creates one Lua thread per OS thread on demand (<i>inferred</i> from the
    ///     7.5 host source; consistent with the official Lua binding, which re-queries the state on every access), so
    ///     the value must never be cached across threads: fetch it once per operation, and inside a Lua callback use
    ///     the state the callback received. Callable from any thread. Borrowed: never close the returned state.
    /// </remarks>
    public delegate* unmanaged[Stdcall]<void*> GetLuaState;

    /// <summary>
    ///     Host helper that registers a global Lua C function (offset 16). <b>Do not call.</b> Kept untyped on
    ///     purpose.
    /// </summary>
    /// <remarks>
    ///     In the 7.5 host source this slot points at a Pascal routine declared without an explicit convention, i.e.
    ///     the compiler's default register-based one, while the official managed binding calls it as <c>stdcall</c>.
    ///     The two coincide on x64 only. The slot is also redundant: pushing a C closure and setting a global through
    ///     the Lua C API does the same thing with a known convention. Leaving the field as a plain pointer makes an
    ///     accidental call impossible to write without a cast.
    /// </remarks>
    public void* LuaRegister;

    /// <summary>
    ///     Pushes the Lua userdata wrapper of a native Cheat Engine object onto a Lua stack (offset 24).
    ///     Arguments: the <c>lua_State*</c>, then the native object pointer.
    /// </summary>
    /// <remarks>
    ///     Pushes exactly one value. The object pointer is borrowed, the host does not take ownership through this
    ///     call. Use the state that belongs to the current thread. Whether it may raise a Lua error for an invalid
    ///     object is unknown: only pass pointers obtained from Cheat Engine.
    /// </remarks>
    public delegate* unmanaged[Stdcall]<void*, void*, void> LuaPushClassInstance;

    /// <summary>
    ///     Pumps the pending window messages of Cheat Engine's GUI (offset 32). No arguments, no result.
    /// </summary>
    /// <remarks>
    ///     Main thread only: it exists so that long-running main-thread work can keep the GUI responsive. Re-entrancy
    ///     applies: handlers of the pumped messages run inside the call.
    /// </remarks>
    public delegate* unmanaged[Stdcall]<void> ProcessMessages;

    /// <summary>
    ///     Runs the calls that other threads have queued for the main thread, waiting up to the given number of
    ///     milliseconds for one to arrive (offset 40). Returns true when at least one queued call was executed.
    /// </summary>
    /// <remarks>
    ///     Main thread only: used while the main thread is blocked waiting for a worker that itself needs to
    ///     synchronise with the GUI. The result is a 1-byte Pascal boolean, see <see cref="Bool8" /> for the evidence
    ///     and for why it must not be read as 4 bytes. The meaning of the result is <i>inferred</i> from the 7.5 host
    ///     source.
    /// </remarks>
    public delegate* unmanaged[Stdcall]<int, Bool8> CheckSynchronize;
}
