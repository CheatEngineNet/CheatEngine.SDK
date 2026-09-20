using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
    private static lua_State* s_state;
    private static int s_providerCalls;
    private static int s_pusherCalls;
    private static nint s_lastPushedObject;

    public static int ProviderCalls => Volatile.Read(ref s_providerCalls);

    public static int PusherCalls => Volatile.Read(ref s_pusherCalls);

    public static nint LastPushedObject => Volatile.Read(ref s_lastPushedObject);

    public static nint ProviderAddress => (nint)(delegate* unmanaged[Stdcall]<void*>)&Provide;

    public static nint PusherAddress => (nint)(delegate* unmanaged[Stdcall]<void*, void*, void>)&PushObject;

    /// <summary>
    ///     Points the provider at <paramref name="state" /> (null makes it return no state) and builds a binding for the
    ///     calling thread as main thread.
    /// </summary>
    public static LuaHostBinding CreateBinding(lua_State* state, bool withPusher = true)
    {
        s_state = state;
        s_providerCalls = 0;
        s_pusherCalls = 0;
        s_lastPushedObject = 0;
        delegate* unmanaged[Stdcall]<void*> provider = &Provide;
        delegate* unmanaged[Stdcall]<void*, void*, void> pusher = withPusher ? &PushObject : null;
        return new LuaHostBinding(provider, pusher, Environment.CurrentManagedThreadId);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void* Provide()
    {
        Interlocked.Increment(ref s_providerCalls);
        return s_state;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void PushObject(void* L, void* nativeObject)
    {
        Interlocked.Increment(ref s_pusherCalls);
        Volatile.Write(ref s_lastPushedObject, (nint)nativeObject);
        lua_pushlightuserdata((lua_State*)L, nativeObject);
    }
}
