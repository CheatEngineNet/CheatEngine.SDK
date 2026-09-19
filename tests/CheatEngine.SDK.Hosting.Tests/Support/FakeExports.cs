using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CheatEngine.SDK.Abi;
using CheatEngine.SDK.Abi.Managed;
using CheatEngine.SDK.Lua.Interop.Types;
using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

namespace CheatEngine.SDK.Hosting.Tests.Support;

/// <summary>
///     Stand-ins for the five functions of Cheat Engine's managed exports record, as <c>[UnmanagedCallersOnly]</c>
///     <c>stdcall</c> statics: <c>GetLuaState</c> returns the fixture state a test installed, <c>LuaPushClassInstance</c>
///     pushes the object pointer as a light userdata, <c>ProcessMessages</c> and <c>CheckSynchronize</c> count their
///     calls, and the <c>LuaRegister</c> slot points at a function that must never be called.
/// </summary>
internal static unsafe class FakeExports
{
    private static lua_State* s_state;
    private static int s_getLuaStateCalls;
    private static int s_processMessagesCalls;
    private static int s_checkSynchronizeCalls;
    private static int s_lastTimeout;
    private static int s_luaRegisterCalls;
    private static int s_pusherCalls;
    private static nint s_lastPushedObject;
    private static byte s_checkSynchronizeRawResult = 1;

    public static int GetLuaStateCalls => Volatile.Read(ref s_getLuaStateCalls);

    public static int ProcessMessagesCalls => Volatile.Read(ref s_processMessagesCalls);

    public static int CheckSynchronizeCalls => Volatile.Read(ref s_checkSynchronizeCalls);

    public static int LastTimeout => Volatile.Read(ref s_lastTimeout);

    public static int LuaRegisterCalls => Volatile.Read(ref s_luaRegisterCalls);

    public static int PusherCalls => Volatile.Read(ref s_pusherCalls);

    public static nint LastPushedObject => Volatile.Read(ref s_lastPushedObject);

    public static nint GetLuaStateAddress => (nint)(delegate* unmanaged[Stdcall]<void*>)&GetLuaState;

    public static nint PusherAddress => (nint)(delegate* unmanaged[Stdcall]<void*, void*, void>)&PushClassInstance;

    /// <summary>
    ///     The raw byte the <c>CheckSynchronize</c> double returns; 1 by default, set to 0 for false or 0xFF to prove
    ///     truthiness.
    /// </summary>
    public static byte CheckSynchronizeRawResult
    {
        get => Volatile.Read(ref s_checkSynchronizeRawResult);
        set => Volatile.Write(ref s_checkSynchronizeRawResult, value);
    }

    /// <summary>
    ///     Points <c>GetLuaState</c> at <paramref name="state" /> (null makes it return no state) and clears the
    ///     counters.
    /// </summary>
    public static void UseState(lua_State* state)
    {
        s_state = state;
        Reset();
    }

    public static void Reset()
    {
        s_getLuaStateCalls = 0;
        s_processMessagesCalls = 0;
        s_checkSynchronizeCalls = 0;
        s_lastTimeout = -1;
        s_luaRegisterCalls = 0;
        s_pusherCalls = 0;
        s_lastPushedObject = 0;
        s_checkSynchronizeRawResult = 1;
    }

    /// <summary>Builds the record the host would pass, with every slot pointing at a double.</summary>
    /// <param name="reportedSize">The value of <c>sizeofExportedFunctions</c>; defaults to the real size (48).</param>
    /// <param name="withPusher">False leaves the <c>LuaPushClassInstance</c> slot null.</param>
    /// <param name="withPump">False leaves the <c>ProcessMessages</c> and <c>CheckSynchronize</c> slots null.</param>
    public static ManagedExportedFunctions Create(int reportedSize = 0, bool withPusher = true, bool withPump = true)
    {
        ManagedExportedFunctions exports = default;
        exports.SizeOfExportedFunctions = reportedSize == 0 ? sizeof(ManagedExportedFunctions) : reportedSize;
        exports.GetLuaState = &GetLuaState;
        exports.LuaRegister = (delegate* unmanaged[Stdcall]<void>)&LuaRegisterNeverCalled;
        exports.LuaPushClassInstance = withPusher ? &PushClassInstance : null;
        exports.ProcessMessages = withPump ? &ProcessMessages : null;
        exports.CheckSynchronize = withPump ? &CheckSynchronize : null;
        return exports;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void* GetLuaState()
    {
        Interlocked.Increment(ref s_getLuaStateCalls);
        return s_state;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void PushClassInstance(void* L, void* nativeObject)
    {
        Interlocked.Increment(ref s_pusherCalls);
        Volatile.Write(ref s_lastPushedObject, (nint)nativeObject);
        lua_pushlightuserdata((lua_State*)L, nativeObject);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void ProcessMessages()
    {
        Interlocked.Increment(ref s_processMessagesCalls);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static Bool8 CheckSynchronize(int timeout)
    {
        Interlocked.Increment(ref s_checkSynchronizeCalls);
        Volatile.Write(ref s_lastTimeout, timeout);
        return new Bool8(s_checkSynchronizeRawResult);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void LuaRegisterNeverCalled()
    {
        Interlocked.Increment(ref s_luaRegisterCalls);
    }
}
