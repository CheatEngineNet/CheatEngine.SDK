using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using CheatEngine.SDK.Lua.Interop.Types;
using CheatEngine.SDK.Lua.Runtime;
using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

namespace CheatEngine.SDK.Lua.Tests.Support;

/// <summary>
///     A stand-in for the host's exported functions, in the shape <see cref="LuaHostBinding" /> expects (the same
///     <c>void*</c>-typed <c>stdcall</c> pointers as <c>CheatEngine.SDK.Abi.Managed.ManagedExportedFunctions</c>): a
///     state provider that returns the state a test installed, and an object pusher that pushes the object pointer as a
///     light userdata and
///     counts its calls.
/// </summary>
internal static unsafe class HostDouble
{
    private static readonly Lock SStateGate = new();
    private static readonly Dictionary<int, nint> SStatesByThread = new();
    private static nint s_defaultState;
    private static int s_providerCalls;
    private static int s_pusherCalls;
    private static nint s_lastPushedObject;

    public static int ProviderCalls => Volatile.Read(ref s_providerCalls);

    public static int PusherCalls => Volatile.Read(ref s_pusherCalls);

    public static nint LastPushedObject => Volatile.Read(ref s_lastPushedObject);

    public static nint ProviderAddress => (nint)(delegate* unmanaged[Stdcall]<void*>)&Provide;

    public static nint PusherAddress => (nint)(delegate* unmanaged[Stdcall]<void*, void*, void>)&PushObject;

    /// <summary>
    ///     Points the provider at <paramref name="state" /> for the calling thread (null makes it return no state) and
    ///     builds a binding for the calling thread as main thread. An unmapped worker receives this default state;
    ///     tests can use <see cref="ClearStateForCurrentThread" /> or <see cref="SetStateForCurrentThread" /> to model
    ///     an unavailable state or a rooted worker coroutine.
    /// </summary>
    public static LuaHostBinding CreateBinding(lua_State* state, bool withPusher = true)
    {
        lock (SStateGate)
        {
            SStatesByThread.Clear();
            s_defaultState = (nint)state;
            SStatesByThread.Add(Environment.CurrentManagedThreadId, (nint)state);
        }

        s_providerCalls = 0;
        s_pusherCalls = 0;
        s_lastPushedObject = 0;
        delegate* unmanaged[Stdcall]<void*> provider = &Provide;
        delegate* unmanaged[Stdcall]<void*, void*, void> pusher = withPusher ? &PushObject : null;
        return new LuaHostBinding(provider, pusher, Environment.CurrentManagedThreadId);
    }

    /// <summary>
    ///     Sets the state the provider returns for the current worker. This is a test seam for the host contract that
    ///     creates one Lua coroutine for each OS thread; it does not turn distinct pointers into independent Lua heaps.
    /// </summary>
    public static void SetStateForCurrentThread(nint state)
    {
        lock (SStateGate)
        {
            SStatesByThread[Environment.CurrentManagedThreadId] = state;
        }
    }

    /// <summary>
    ///     Makes the state provider report no state for the current worker only. The default host state remains
    ///     available to every other worker.
    /// </summary>
    public static void ClearStateForCurrentThread()
    {
        SetStateForCurrentThread(nint.Zero);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void* Provide()
    {
        Interlocked.Increment(ref s_providerCalls);
        lock (SStateGate)
        {
            return (void*)(SStatesByThread.TryGetValue(Environment.CurrentManagedThreadId, out var state)
                ? state
                : s_defaultState);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void PushObject(void* L, void* nativeObject)
    {
        Interlocked.Increment(ref s_pusherCalls);
        Volatile.Write(ref s_lastPushedObject, (nint)nativeObject);
        lua_pushlightuserdata((lua_State*)L, nativeObject);
    }
}
