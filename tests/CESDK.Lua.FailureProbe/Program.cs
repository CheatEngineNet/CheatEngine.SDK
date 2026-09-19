using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CESDK.Lua.Callbacks;
using CESDK.Lua.Calls;
using CESDK.Lua.Interop.Api;
using CESDK.Lua.Interop.Types;
using CESDK.Lua.State;

namespace CESDK.Lua.FailureProbe;

internal static unsafe class Program
{
    private static delegate* unmanaged[Cdecl]<void*, void*, nuint, nuint, void*> s_originalAllocator;
    private static void* s_originalAllocatorData;
    private static int s_rejectAllocations;

    public static int Main(string[] arguments)
    {
        if (arguments.Length != 1) return Fail("expected the Lua DLL path as the only argument");

        nint module = 0;
        lua_State* nativeState = null;
        try
        {
            module = NativeLibrary.Load(Path.GetFullPath(arguments[0]));
            if (!LuaApi.TryInitialize(module, out var bindFailure)) return Fail("could not bind Lua: " + bindFailure);

            nativeState = LuaApi.luaL_newstate();
            if (nativeState is null) return Fail("luaL_newstate returned null");
            LuaApi.luaL_openlibs(nativeState);

            void* allocatorData = null;
            s_originalAllocator = LuaApi.lua_getallocf(nativeState, &allocatorData);
            s_originalAllocatorData = allocatorData;
            LuaApi.lua_setallocf(nativeState, &RejectingAllocator, null);
            return RunProbe(new LuaState((nint)nativeState));
        }
        catch (Exception exception)
        {
            Volatile.Write(ref s_rejectAllocations, 0);
            return Fail(exception.ToString());
        }
        finally
        {
            Volatile.Write(ref s_rejectAllocations, 0);
            if (nativeState is not null)
            {
                if (s_originalAllocator != null)
                    LuaApi.lua_setallocf(nativeState, s_originalAllocator, s_originalAllocatorData);
                LuaApi.lua_close(nativeState);
            }

            if (module != 0) NativeLibrary.Free(module);
        }
    }

    private static int RunProbe(LuaState state)
    {
        var message = new byte[4096];
        Array.Fill(message, (byte)'x');

        if (ProbeStringAllocation(state, message) != 0) return 1;
        if (ProbeThunkFailure(state, message) != 0) return 1;
        if (ProbeTableAllocation(state) != 0) return 1;
        if (ProbeUserdataAllocation(state) != 0) return 1;
        if (ProbeRawSetAllocation(state) != 0) return 1;
        if (ProbeReferenceAllocation(state) != 0) return 1;
        if (ProbeCallbackAllocation(state) != 0) return 1;
        if (ProbeFailingFinalizer(state, message) != 0) return 1;

        state.PushString("still alive"u8);
        if (state.Top != 1) return Fail("the Lua state was not usable after protected failures");
        state.Pop(1);

        Console.WriteLine("PASS native allocation and finalizer boundaries");
        return 0;
    }

    private static int ProbeStringAllocation(LuaState state, byte[] message)
    {
        LuaStatus status;
        Volatile.Write(ref s_rejectAllocations, 1);
        try { status = state.TryPushString(message); }
        finally { Volatile.Write(ref s_rejectAllocations, 0); }
        if (status != LuaStatus.MemoryError) return Fail("TryPushString did not return LUA_ERRMEM");
        if (state.Top != 1) return Fail("TryPushString did not leave one error value");
        state.Pop(1);
        return 0;
    }

    private static int ProbeThunkFailure(LuaState state, byte[] message)
    {
        int results;
        Volatile.Write(ref s_rejectAllocations, 1);
        try { results = LuaThunk.Fail(state, message); }
        finally { Volatile.Write(ref s_rejectAllocations, 0); }
        if (results != LuaThunk.FailureResultCount) return Fail("LuaThunk.Fail returned the wrong result count");
        if (state.Top != LuaThunk.FailureResultCount) return Fail("LuaThunk.Fail did not leave two results");
        state.Pop(LuaThunk.FailureResultCount);
        return 0;
    }

    private static int ProbeTableAllocation(LuaState state)
    {
        var status = CaptureMemoryException(() => state.CreateTable());
        if (status != LuaStatus.MemoryError) return Fail("CreateTable did not throw LUA_ERRMEM");
        return state.Top == 0 ? 0 : Fail("CreateTable did not restore the stack");
    }

    private static int ProbeUserdataAllocation(LuaState state)
    {
        var status = CaptureMemoryException(() => state.NewUserdata(4096));
        if (status != LuaStatus.MemoryError) return Fail("NewUserdata did not throw LUA_ERRMEM");
        return state.Top == 0 ? 0 : Fail("NewUserdata did not restore the stack");
    }

    private static int ProbeRawSetAllocation(LuaState state)
    {
        state.CreateTable();
        state.PushInteger(1);
        state.PushInteger(2);
        var status = CaptureMemoryException(() => state.TryRawSet(1));
        if (status != LuaStatus.MemoryError) return Fail("TryRawSet did not throw LUA_ERRMEM");
        if (state.Top != 1) return Fail("TryRawSet did not preserve its table and consume its inputs");
        state.Pop(1);
        return 0;
    }

    private static int ProbeReferenceAllocation(LuaState state)
    {
        state.PushInteger(42);
        var status = CaptureMemoryException(() => state.CreateRef());
        if (status != LuaStatus.MemoryError) return Fail("CreateRef did not throw LUA_ERRMEM");
        return state.Top == 0 ? 0 : Fail("CreateRef did not consume its input and error");
    }

    private static int ProbeCallbackAllocation(LuaState state)
    {
        LuaStatus status;
        LuaCallback<object>? callback;
        var function = new LuaNativeFunction(&NoOp);
        Volatile.Write(ref s_rejectAllocations, 1);
        try { status = LuaCallback.TryCreate(state, function, new object(), out callback); }
        finally { Volatile.Write(ref s_rejectAllocations, 0); }
        if (status != LuaStatus.MemoryError) return Fail("LuaCallback.TryCreate did not return LUA_ERRMEM");
        if (callback is not null) return Fail("LuaCallback.TryCreate returned a callback after failure");
        if (state.Top != 1) return Fail("LuaCallback.TryCreate did not leave one error value");
        state.Pop(1);
        return 0;
    }

    private static int ProbeFailingFinalizer(LuaState state, byte[] message)
    {
        var setup = state.TryExecute(
            "collectgarbage('stop'); setmetatable({}, { __gc = function() error('expected finalizer failure') end }); collectgarbage('restart')"u8,
            0);
        if (!setup.IsOk) return Fail("could not install the failing finalizer: " + setup);

        for (var attempt = 0; attempt < 100_000; attempt++)
        {
            // Long strings are not interned, so each protected push gives the incremental collector work to do.
            message[0] = (byte)(attempt & 0x7f);
            var status = state.TryPushString(message);
            if (status == LuaStatus.GcMetamethodError)
            {
                if (state.Top != 1) return Fail("a failing __gc did not leave one error value");
                state.Pop(1);
                return 0;
            }

            if (!status.IsOk) return Fail("string allocation returned an unexpected status while awaiting __gc: " + status);
            state.Pop(1);
        }

        return Fail("the protected allocation path did not observe the failing __gc");
    }

    private static LuaStatus CaptureMemoryException(Action operation)
    {
        Volatile.Write(ref s_rejectAllocations, 1);
        try
        {
            operation();
            return LuaStatus.Ok;
        }
        catch (LuaException exception)
        {
            return exception.Status;
        }
        finally
        {
            Volatile.Write(ref s_rejectAllocations, 0);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int NoOp(nint _) => 0;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void* RejectingAllocator(void* _, void* pointer, nuint oldSize, nuint newSize)
    {
        if (newSize > oldSize && Volatile.Read(ref s_rejectAllocations) != 0) return null;
        return s_originalAllocator(s_originalAllocatorData, pointer, oldSize, newSize);
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine("FAIL " + message);
        return 1;
    }
}
