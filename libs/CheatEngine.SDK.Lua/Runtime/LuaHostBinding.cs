using System;
using System.Runtime.InteropServices;
using CheatEngine.SDK.Lua.Interop.Types;

namespace CheatEngine.SDK.Lua.Runtime;

/// <summary>
///     What the host hands to <see cref="LuaRuntime.Attach" />: the per-thread Lua state provider, the host-object pusher
///     and the identity of the host's main thread. Built by <c>CheatEngine.SDK.Hosting</c> from Cheat Engine's exported
///     function table when the plugin is enabled, or by a test from <c>[UnmanagedCallersOnly]</c> doubles.
/// </summary>
/// <remarks>
///     <para>
///         <b>State provider</b> (<c>stdcall</c>, <c>lua_State* ()</c>): Cheat Engine's <c>GetLuaState</c>, which returns
///         the
///         Lua thread that belongs to the calling OS thread. It is called once per operation by
///         <see cref="LuaRuntime.AcquireOperation()" />; a return of null is reported as "no state for this thread".
///     </para>
///     <para>
///         <b>Host-object pusher</b> (<c>stdcall</c>, <c>void (lua_State*, void*)</c>): Cheat Engine's
///         <c>LuaPushClassInstance</c>, which pushes the userdata for a native object pointer. May be zero when the host
///         has
///         none, in which case <see cref="LuaRuntime.PushHostObject" /> fails.
///     </para>
///     <para>
///         <b>Main thread</b>: the <see cref="Environment.CurrentManagedThreadId" /> of the host's GUI thread, captured by
///         the
///         host while it runs the enable callback on that thread; the value behind <see cref="LuaRuntime.IsMainThread" />.
///     </para>
///     <para>
///         The struct stores the pointers as <see cref="nint" /> so that safe code can inspect it. The typed constructor
///         takes the function pointers exactly as <c>CheatEngine.SDK.Abi.Managed.ManagedExportedFunctions</c> declares
///         them (<c>delegate* unmanaged[Stdcall]&lt;void*&gt;</c> and <c>&lt;void*, void*, void&gt;</c>: the ABI layer cannot
///         name
///         <c>lua_State</c>), so the host passes its fields without a cast and the compiler checks the calling convention
///         and arity for real; no <c>lua_State*</c> appears in this assembly's public surface.
///     </para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly unsafe struct LuaHostBinding : IEquatable<LuaHostBinding>
{
    /// <summary>Creates a binding from typed function pointers, in the shape of the host's exported-functions record.</summary>
    /// <param name="stateProvider">The per-thread state provider; must not be null.</param>
    /// <param name="hostObjectPusher">The host-object pusher; null when the host has none.</param>
    /// <param name="mainThreadId">The managed thread id of the host's main thread.</param>
    public LuaHostBinding(
        delegate* unmanaged[Stdcall]<void*> stateProvider,
        delegate* unmanaged[Stdcall]<void*, void*, void> hostObjectPusher,
        int mainThreadId)
    {
        StateProvider = (nint)stateProvider;
        HostObjectPusher = (nint)hostObjectPusher;
        MainThreadId = mainThreadId;
    }

    /// <summary>Creates a binding from raw function addresses, for code that received them as integers.</summary>
    /// <param name="stateProvider">Address of a <c>stdcall</c> function <c>lua_State* ()</c>; must not be zero.</param>
    /// <param name="hostObjectPusher">Address of a <c>stdcall</c> function <c>void (lua_State*, void*)</c>, or zero.</param>
    /// <param name="mainThreadId">The managed thread id of the host's main thread.</param>
    public LuaHostBinding(nint stateProvider, nint hostObjectPusher, int mainThreadId)
    {
        StateProvider = stateProvider;
        HostObjectPusher = hostObjectPusher;
        MainThreadId = mainThreadId;
    }

    /// <summary>Gets the address of the per-thread state provider.</summary>
    public nint StateProvider { get; }

    /// <summary>Gets the address of the host-object pusher, or zero.</summary>
    public nint HostObjectPusher { get; }

    /// <summary>Gets the managed thread id of the host's main thread.</summary>
    public int MainThreadId { get; }

    /// <summary>Gets a value indicating whether the binding can be attached: it has a state provider.</summary>
    public bool IsValid => StateProvider != 0;

    internal delegate* unmanaged[Stdcall]<lua_State*> Provider =>
        (delegate* unmanaged[Stdcall]<lua_State*>)StateProvider;

    internal delegate* unmanaged[Stdcall]<lua_State*, void*, void> Pusher =>
        (delegate* unmanaged[Stdcall]<lua_State*, void*, void>)HostObjectPusher;

    /// <summary>Compares all three fields.</summary>
    /// <param name="left">First binding.</param>
    /// <param name="right">Second binding.</param>
    public static bool operator ==(LuaHostBinding left, LuaHostBinding right)
    {
        return left.Equals(right);
    }

    /// <summary>Compares all three fields.</summary>
    /// <param name="left">First binding.</param>
    /// <param name="right">Second binding.</param>
    public static bool operator !=(LuaHostBinding left, LuaHostBinding right)
    {
        return !left.Equals(right);
    }

    /// <inheritdoc />
    public bool Equals(LuaHostBinding other)
    {
        return StateProvider == other.StateProvider && HostObjectPusher == other.HostObjectPusher &&
               MainThreadId == other.MainThreadId;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is LuaHostBinding other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(StateProvider, HostObjectPusher, MainThreadId);
    }
}
