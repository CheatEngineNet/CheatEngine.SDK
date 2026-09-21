using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CheatEngine.SDK.Lua.Callbacks;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Interop.Api;
using CheatEngine.SDK.Lua.Interop.Types;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Lua.FailureProbe;

internal static unsafe class Program
{
    private const string CheckStackGrowthMode = "--checkstack-growth";

    private const string GeneratedFunctionAllocationMode = "--generated-function-allocation";

    private const int CheckStackGrowthSlots = 4096;

    // Exact C11 CHEATENGINE_SDK_NO_ERROR sentinel. The internal production alias is LuaProtectedApi.NoErrorStatus.
    private const int BridgeNoErrorStatus = -100;
    private const int PushBytesOperation = 0;
    private const int PushHostObjectOperation = 10;
    private const int ProtectedExportCount = 20;
    private const long StackSentinel = 0x1CEB_00DA_5EED_1234;

    private const string UncheckedFunctionReservationFailureMessage =
        "Lua could not reserve one stack slot for the bare C function; the stack is unchanged.";

    private static delegate* unmanaged[Cdecl]<void*, void*, nuint, nuint, void*> s_originalAllocator;
    private static void* s_originalAllocatorData;
    private static lua_State* s_runtimeState;
    private static int s_rejectAllocations;

    public static int Main(string[] arguments)
    {
        if (arguments.Length is < 1 or > 2 ||
            (arguments.Length == 2 &&
             !string.Equals(arguments[1], CheckStackGrowthMode, StringComparison.Ordinal) &&
             !string.Equals(arguments[1], GeneratedFunctionAllocationMode, StringComparison.Ordinal)))
            return Fail("expected the Lua DLL path, optionally followed by --checkstack-growth or --generated-function-allocation");

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
            var state = new LuaState((nint)nativeState);
            if (arguments.Length == 2)
            {
                return string.Equals(arguments[1], CheckStackGrowthMode, StringComparison.Ordinal)
                    ? RunCheckStackGrowthProbe(state)
                    : RunGeneratedFunctionAllocationProbe(state);
            }

            return RunProbe(state, module);
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

    private static int RunProbe(LuaState state, nint luaModule)
    {
        var message = new byte[4096];
        Array.Fill(message, (byte)'x');

        if (ProbeStringAllocation(state, message) != 0) return 1;
        if (ProbeThunkFailure(state, message) != 0) return 1;
        if (ProbeTableAllocation(state) != 0) return 1;
        if (ProbeByteTableAllocation(state) != 0) return 1;
        if (ProbeUserdataAllocation(state) != 0) return 1;
        if (ProbeRawSetAllocation(state) != 0) return 1;
        if (ProbeRawSetIndexAllocation(state) != 0) return 1;
        if (ProbeRawSetPointerAllocation(state) != 0) return 1;
        if (ProbeReferenceAllocation(state) != 0) return 1;
        if (ProbePrivateReferenceReleaseAllocation(state) != 0) return 1;
        if (ProbeCallbackAllocation(state) != 0) return 1;
        if (ProbeGeneratedFunctionAllocation(state) != 0) return 1;
        if (ProbeFailingFinalizer(state, message) != 0) return 1;
        if (ProbeHostObjectPusherLongJump(state, luaModule) != 0) return 1;

        var recoveryStatus = state.TryExecute("return 6 * 7"u8, 1, "=post-failure-recovery"u8);
        if (!recoveryStatus.IsOk)
            return Fail("the Lua state could not execute a new protected call after the failures");
        if (!state.TryReadInteger(-1, out var recoveryValue) || recoveryValue != 42)
            return Fail("the Lua state returned an unexpected post-failure recovery value");
        state.Pop(1);

        Console.WriteLine("PASS native protected allocation, finalizer, and host-object longjmp boundaries");
        return 0;
    }

    private static int RunGeneratedFunctionAllocationProbe(LuaState state)
    {
        if (ProbeGeneratedFunctionAllocation(state) != 0) return 1;
        WriteMarker("PASS generated function closure allocation failure returns status and restores stack");
        return 0;
    }

    private static int ProbeStringAllocation(LuaState state, byte[] message)
    {
        if (PushSentinel(state, "TryPushString") != 0) return 1;
        LuaStatus status;
        Volatile.Write(ref s_rejectAllocations, 1);
        try
        {
            status = state.TryPushString(message);
        }
        finally
        {
            Volatile.Write(ref s_rejectAllocations, 0);
        }

        if (status != LuaStatus.MemoryError) return Fail("TryPushString did not return LUA_ERRMEM");
        return AssertErrorThenRestoreSentinel(state, "TryPushString");
    }

    private static int RunCheckStackGrowthProbe(LuaState state)
    {
        if (state.Top != 0) return Fail("lua_checkstack probe started with a non-empty Lua stack");

        WriteMarker("MARK lua_checkstack-direct-growth-before-reject");
        bool reserved;
        Volatile.Write(ref s_rejectAllocations, 1);
        // Deliberately call the current managed binding without LuaProtectedApi: this is the pre-bridge path whose
        // fixture behavior we must observe under an allocation failure.
        try
        {
            reserved = state.TryEnsureStack(CheckStackGrowthSlots);
        }
        finally
        {
            Volatile.Write(ref s_rejectAllocations, 0);
        }

        if (reserved)
            return Fail("lua_checkstack unexpectedly reserved 4096 slots while the allocator rejected growth");
        if (state.Top != 0) return Fail("lua_checkstack changed the stack after rejected growth");
        WriteMarker("MARK lua_checkstack-direct-growth-returned-zero");

        if (!state.TryEnsureStack(CheckStackGrowthSlots))
            return Fail("lua_checkstack could not reserve the same 4096 slots after allocator recovery");
        if (state.Top != 0) return Fail("lua_checkstack recovery changed the stack");
        WriteMarker("MARK lua_checkstack-direct-growth-recovery-reserved");

        if (ProbeBridgeNoErrorWithFullStack(state) != 0) return 1;
        if (ProbeUncheckedFunctionWithFullStack(state) != 0) return 1;

        var recoveryStatus = state.TryExecute("return 6 * 7"u8, 1, "=checkstack-recovery"u8);
        if (!recoveryStatus.IsOk) return Fail("the Lua state could not execute after rejected lua_checkstack growth");
        if (!state.TryReadInteger(-1, out var recoveryValue) || recoveryValue != 42)
            return Fail("the Lua state returned an unexpected checkstack recovery value");
        state.Pop(1);

        WriteMarker("PASS lua_checkstack direct rejected-growth returns 0 and the state recovers");
        return 0;
    }

    private static int ProbeBridgeNoErrorWithFullStack(LuaState state)
    {
        var bridgePath = Path.Combine(AppContext.BaseDirectory, "cheatengine-sdk-lua-bridge.dll");
        if (!File.Exists(bridgePath)) return Fail("the native Lua bridge was not copied beside the checkstack probe");

        var bridge = NativeLibrary.Load(bridgePath);
        try
        {
            var protectedOperation =
                (delegate* unmanaged[Cdecl]<lua_State*, nint*, int, int, void*, nuint, nint, nint, int>)NativeLibrary
                    .GetExport(
                        bridge,
                        "cheatengine_sdk_lua_protected");
            var exports = stackalloc nint[ProtectedExportCount];
            PopulateProtectedExports(LuaApi.ModuleHandle, exports);

            var initialTop = state.Top;
            int status;
            WriteMarker("MARK lua_checkstack-bridge-fill-before-reject");
            Volatile.Write(ref s_rejectAllocations, 1);
            try
            {
                if (!TryFillStackUntilGrowthIsRejected(state, out var fullTop))
                    return Fail("lua_checkstack could not fill any already-reserved stack slot");
                WriteMarker("MARK lua_checkstack-bridge-stack-full");

                // PushBytes with an empty payload is a valid zero-input operation. The bridge must return its native
                // no-error sentinel before pushing the closure, rather than modifying this full Lua stack.
                status = protectedOperation(
                    (lua_State*)state.Handle,
                    exports,
                    PushBytesOperation,
                    0,
                    null,
                    0,
                    0,
                    0);
                if (status != BridgeNoErrorStatus)
                    return Fail("the full-stack bridge call did not return its NoErrorStatus sentinel");
                if (state.Top != fullTop)
                    return Fail("the full-stack bridge call changed the Lua stack before returning NoErrorStatus");
                WriteMarker("MARK lua_checkstack-bridge-returned-no-error-status");
            }
            finally
            {
                Volatile.Write(ref s_rejectAllocations, 0);
            }

            state.SetTop(initialTop);
            if (state.Top != initialTop) return Fail("the full-stack bridge probe could not restore the Lua stack");
            if (!state.TryEnsureStack(1))
                return Fail("lua_checkstack could not reserve a slot after bridge-stack recovery");
            WriteMarker("MARK lua_checkstack-bridge-stack-restored");
            return 0;
        }
        finally
        {
            NativeLibrary.Free(bridge);
        }
    }

    private static bool TryFillStackUntilGrowthIsRejected(LuaState state, out int fullTop)
    {
        var pushes = 0;
        // Every push is preceded by the direct binding. The first false proves the stack needs an allocation that the
        // fixture rejects; PushInteger itself is allocation-free while the reservation is true.
        while (state.TryEnsureStack(1))
        {
            state.PushInteger(pushes);
            pushes++;
        }

        fullTop = state.Top;
        return pushes != 0;
    }

    private static int ProbeUncheckedFunctionWithFullStack(LuaState state)
    {
        var initialTop = state.Top;
        var function = new LuaNativeFunction(&NoOp);
        WriteMarker("MARK lua_pushuncheckedfunction-fill-before-reject");
        Volatile.Write(ref s_rejectAllocations, 1);
        try
        {
            if (!TryFillStackUntilGrowthIsRejected(state, out var fullTop))
                return Fail("PushUncheckedFunction could not fill any already-reserved stack slot");
            WriteMarker("MARK lua_pushuncheckedfunction-stack-full");

            try
            {
                state.PushUncheckedFunction(function);
                return Fail("PushUncheckedFunction unexpectedly pushed on a full Lua stack");
            }
            catch (InvalidOperationException exception)
            {
                if (!string.Equals(exception.Message, UncheckedFunctionReservationFailureMessage,
                        StringComparison.Ordinal))
                    return Fail("PushUncheckedFunction returned an unstable reservation failure message");
            }

            if (state.Top != fullTop)
                return Fail("PushUncheckedFunction changed the full Lua stack after reservation rejection");
            WriteMarker("MARK lua_pushuncheckedfunction-reservation-rejected");
        }
        finally
        {
            Volatile.Write(ref s_rejectAllocations, 0);
        }

        state.SetTop(initialTop);
        if (state.Top != initialTop)
            return Fail("PushUncheckedFunction could not restore the Lua stack after reservation rejection");
        if (!state.TryEnsureStack(1))
            return Fail("lua_checkstack could not reserve a slot after PushUncheckedFunction recovery");

        state.PushUncheckedFunction(function);
        if (state.Top != initialTop + 1)
            return Fail("PushUncheckedFunction did not recover after allocator rejection");
        state.Pop(1);
        WriteMarker("MARK lua_pushuncheckedfunction-stack-restored");
        return 0;
    }

    private static int ProbeThunkFailure(LuaState state, byte[] message)
    {
        if (PushSentinel(state, "LuaThunk.Fail") != 0) return 1;
        int results;
        Volatile.Write(ref s_rejectAllocations, 1);
        try
        {
            results = LuaThunk.Fail(state, message);
        }
        finally
        {
            Volatile.Write(ref s_rejectAllocations, 0);
        }

        if (results != LuaThunk.FailureResultCount) return Fail("LuaThunk.Fail returned the wrong result count");
        return AssertResultsThenRestoreSentinel(state, LuaThunk.FailureResultCount, "LuaThunk.Fail");
    }

    private static int ProbeTableAllocation(LuaState state)
    {
        if (PushSentinel(state, "CreateTable") != 0) return 1;
        var status = CaptureMemoryException(() => state.CreateTable());
        if (status != LuaStatus.MemoryError) return Fail("CreateTable did not throw LUA_ERRMEM");
        return AssertOnlySentinelRemains(state, "CreateTable");
    }

    private static int ProbeByteTableAllocation(LuaState state)
    {
        if (PushSentinel(state, "PushByteTable") != 0) return 1;
        var bytes = new byte[4096];
        var status = CaptureMemoryException(() => state.PushByteTable(bytes));
        if (status != LuaStatus.MemoryError) return Fail("PushByteTable did not throw LUA_ERRMEM");
        if (AssertOnlySentinelRemains(state, "PushByteTable") != 0) return 1;
        WriteMarker("MARK PushByteTable protected allocator boundary recovered");
        return 0;
    }

    private static int ProbeUserdataAllocation(LuaState state)
    {
        if (PushSentinel(state, "NewUserdata") != 0) return 1;
        var status = CaptureMemoryException(() => state.NewUserdata(4096));
        if (status != LuaStatus.MemoryError) return Fail("NewUserdata did not throw LUA_ERRMEM");
        return AssertOnlySentinelRemains(state, "NewUserdata");
    }

    private static int ProbeRawSetAllocation(LuaState state)
    {
        if (PushSentinel(state, "TryRawSet") != 0) return 1;
        state.CreateTable();
        state.PushInteger(1);
        state.PushInteger(2);
        var status = CaptureMemoryException(() => state.TryRawSet(2));
        if (status != LuaStatus.MemoryError) return Fail("TryRawSet did not throw LUA_ERRMEM");
        return AssertTableThenRestoreSentinel(state, "TryRawSet");
    }

    private static int ProbeRawSetIndexAllocation(LuaState state)
    {
        if (PushSentinel(state, "RawSetIndex") != 0) return 1;
        state.CreateTable();
        state.PushInteger(2);
        var status = CaptureMemoryException(() => state.RawSetIndex(2, 1));
        if (status != LuaStatus.MemoryError) return Fail("RawSetIndex did not throw LUA_ERRMEM");
        return AssertTableThenRestoreSentinel(state, "RawSetIndex");
    }

    private static int ProbeRawSetPointerAllocation(LuaState state)
    {
        if (PushSentinel(state, "RawSetPointer") != 0) return 1;
        state.CreateTable();
        state.PushInteger(3);
        var status = CaptureMemoryException(() => state.RawSetPointer(2, 0x1CEB));
        if (status != LuaStatus.MemoryError) return Fail("RawSetPointer did not throw LUA_ERRMEM");
        return AssertTableThenRestoreSentinel(state, "RawSetPointer");
    }

    private static int ProbeReferenceAllocation(LuaState state)
    {
        if (PushSentinel(state, "CreateRef") != 0) return 1;
        state.PushInteger(42);
        var status = CaptureMemoryException(() => state.CreateRef());
        if (status != LuaStatus.MemoryError) return Fail("CreateRef did not throw LUA_ERRMEM");
        return AssertOnlySentinelRemains(state, "CreateRef");
    }

    private static int ProbePrivateReferenceReleaseAllocation(LuaState state)
    {
        if (state.Top != 0) return Fail("LuaRef.Release started with a non-empty Lua stack");

        s_runtimeState = (lua_State*)state.Handle;
        delegate* unmanaged[Stdcall]<void*> provider = &ProvideRuntimeState;
        LuaHostBinding binding = new(
            stateProvider: provider,
            hostObjectPusher: null,
            mainThreadId: Environment.CurrentManagedThreadId);
        LuaRuntime.Attach(in binding);
        try
        {
            state.PushInteger(42);
            var reference = state.CreateRef();
            if (!reference.IsCurrent) return Fail("CreateRef did not produce a current private reference");
            if (PushSentinel(state, "LuaRef.Release") != 0) return 1;

            var status = ReleaseWithRejectedAllocator(reference, state);

            if (!status.IsOk && status != LuaStatus.MemoryError)
                return Fail("LuaRef.Release returned an unexpected status while allocator rejection was active: " +
                            status);
            if (reference.IsResolved || state.TryPushRef(reference))
                return Fail("LuaRef.Release left its released private reference usable");
            if (AssertOnlySentinelRemains(state, "LuaRef.Release") != 0) return 1;

            // The release path may be allocation-free (LUA_OK) or may return LUA_ERRMEM after Lua's private free-list
            // bookkeeping. In either case the private table must still accept, return and release a fresh slot once the
            // allocator has recovered.
            state.PushInteger(99);
            var replacement = state.CreateRef();
            try
            {
                if (!replacement.IsCurrent || !state.TryPushRef(replacement))
                    return Fail("the private reference table did not recover after LuaRef.Release");
                if (!state.TryReadInteger(-1, out var value) || value != 99)
                    return Fail("the recovered private reference returned the wrong value");
                state.Pop(1);
            }
            finally
            {
                replacement.Release(state);
            }

            if (state.Top != 0) return Fail("LuaRef.Release recovery left values on the Lua stack");
            WriteMarker("MARK LuaRef.Release protected allocator boundary recovered");
            return 0;
        }
        finally
        {
            LuaRuntime.Detach();
            s_runtimeState = null;
        }
    }

    private static LuaStatus ReleaseWithRejectedAllocator(LuaRef reference, LuaState state)
    {
        Volatile.Write(ref s_rejectAllocations, 1);
        try
        {
            reference.Release(state);
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

    private static int ProbeCallbackAllocation(LuaState state)
    {
        if (PushSentinel(state, "LuaCallback.TryCreate") != 0) return 1;
        LuaStatus status;
        LuaCallback<object>? callback;
        var function = new LuaNativeFunction(&NoOp);
        Volatile.Write(ref s_rejectAllocations, 1);
        try
        {
            status = LuaCallback.TryCreate(state, function, new object(), out callback);
        }
        finally
        {
            Volatile.Write(ref s_rejectAllocations, 0);
        }

        if (status != LuaStatus.MemoryError) return Fail("LuaCallback.TryCreate did not return LUA_ERRMEM");
        if (callback is not null) return Fail("LuaCallback.TryCreate returned a callback after failure");
        return AssertErrorThenRestoreSentinel(state, "LuaCallback.TryCreate");
    }

    private static int ProbeGeneratedFunctionAllocation(LuaState state)
    {
        var function = new LuaNativeFunction(&NoOp);

        // Install the wrapper while allocation is permitted. The guarded closure below is then the only allocating
        // operation, so a rejected allocation is guaranteed to exercise PushClosure rather than helper installation.
        var warmup = LuaRuntime.TryPushGeneratedFunction(state, function);
        if (!warmup.IsOk) return Fail("TryPushGeneratedFunction could not install its wrapper before allocation rejection");
        state.Pop(1);
        if (state.Top != 0) return Fail("TryPushGeneratedFunction warm-up left values on the Lua stack");

        if (PushSentinel(state, "TryPushGeneratedFunction") != 0) return 1;
        LuaStatus status;
        Volatile.Write(ref s_rejectAllocations, 1);
        try
        {
            try
            {
                status = LuaRuntime.TryPushGeneratedFunction(state, function);
            }
            catch (LuaException)
            {
                return Fail("TryPushGeneratedFunction converted the PushClosure failure into LuaException");
            }
        }
        finally
        {
            Volatile.Write(ref s_rejectAllocations, 0);
        }

        if (status != LuaStatus.MemoryError)
            return Fail("TryPushGeneratedFunction did not return PushClosure's LUA_ERRMEM status");
        if (AssertErrorThenRestoreSentinel(state, "TryPushGeneratedFunction") != 0) return 1;
        WriteMarker("MARK TryPushGeneratedFunction PushClosure status, stack, and ownership recovered");

        var recovery = LuaRuntime.TryPushGeneratedFunction(state, function);
        if (!recovery.IsOk) return Fail("TryPushGeneratedFunction did not recover after PushClosure allocation failure");
        state.Pop(1);
        return state.Top == 0
            ? 0
            : Fail("TryPushGeneratedFunction recovery left values on the Lua stack");
    }

    private static int ProbeFailingFinalizer(LuaState state, byte[] message)
    {
        if (PushSentinel(state, "failing __gc") != 0) return 1;
        var setup = state.TryExecute(
            "collectgarbage('stop'); setmetatable({}, { __gc = function() error('expected finalizer failure') end }); collectgarbage('restart')"u8,
            0);
        if (!setup.IsOk) return Fail("could not install the failing finalizer: " + setup);

        for (var attempt = 0; attempt < 100_000; attempt++)
        {
            // Long strings are not interned, so each protected push gives the incremental collector work to do.
            message[0] = (byte)(attempt & 0x7f);
            var status = state.TryPushString(message);
            if (status == LuaStatus.GcMetamethodError) return AssertErrorThenRestoreSentinel(state, "failing __gc");

            if (!status.IsOk)
                return Fail("string allocation returned an unexpected status while awaiting __gc: " + status);
            state.Pop(1);
        }

        return Fail("the protected allocation path did not observe the failing __gc");
    }

    private static int ProbeHostObjectPusherLongJump(LuaState state, nint luaModule)
    {
        var bridgePath = Path.Combine(AppContext.BaseDirectory, "cheatengine-sdk-lua-bridge.dll");
        if (!File.Exists(bridgePath)) return Fail("the native Lua bridge was not copied beside the failure probe");

        var bridge = NativeLibrary.Load(bridgePath);
        try
        {
            var protectedOperation =
                (delegate* unmanaged[Cdecl]<lua_State*, nint*, int, int, void*, nuint, nint, nint, int>)NativeLibrary
                    .GetExport(
                        bridge,
                        "cheatengine_sdk_lua_protected");
            var luaCheckInteger = NativeLibrary.GetExport(luaModule, "luaL_checkinteger");
            var exports = stackalloc nint[ProtectedExportCount];
            PopulateProtectedExports(luaModule, exports);

            // Windows x64 has one native calling convention. luaL_checkinteger is an actual native Lua helper that
            // raises when its first C-function argument is absent. Passing nativeObject=1 selects that missing
            // argument while inputCount=0 proves OP_PUSH_HOST_OBJECT reaches the pusher rather than failing its
            // own input validation. No managed reverse-P/Invoke frame participates in the non-local exit.
            if (PushSentinel(state, "PushHostObject") != 0) return 1;
            var status = protectedOperation(
                (lua_State*)state.Handle,
                exports,
                PushHostObjectOperation,
                0,
                (void*)luaCheckInteger,
                0,
                1,
                0);
            if (status != LuaApi.LUA_ERRRUN)
                return Fail("PushHostObject did not return LUA_ERRRUN after a host-pusher longjmp");
            if (!state.TryReadString(-1, out var error) ||
                !error.Contains("bad argument #1", StringComparison.Ordinal))
                return Fail("PushHostObject did not leave the native luaL_checkinteger failure message on the stack");
            if (AssertErrorThenRestoreSentinel(state, "PushHostObject") != 0) return 1;
            WriteMarker("MARK PushHostObject native pusher longjmp observed");
            return 0;
        }
        finally
        {
            NativeLibrary.Free(bridge);
        }
    }

    private static void PopulateProtectedExports(nint module, nint* exports)
    {
        exports[0] = NativeLibrary.GetExport(module, "lua_gettop");
        exports[1] = NativeLibrary.GetExport(module, "lua_settop");
        exports[2] = NativeLibrary.GetExport(module, "lua_checkstack");
        exports[3] = NativeLibrary.GetExport(module, "lua_rotate");
        exports[4] = NativeLibrary.GetExport(module, "lua_pushlstring");
        exports[5] = NativeLibrary.GetExport(module, "lua_pushinteger");
        exports[6] = NativeLibrary.GetExport(module, "lua_createtable");
        exports[7] = NativeLibrary.GetExport(module, "lua_newuserdata");
        exports[8] = NativeLibrary.GetExport(module, "lua_pushcclosure");
        exports[9] = NativeLibrary.GetExport(module, "lua_pushlightuserdata");
        exports[10] = NativeLibrary.GetExport(module, "lua_rawset");
        exports[11] = NativeLibrary.GetExport(module, "lua_rawseti");
        exports[12] = NativeLibrary.GetExport(module, "lua_rawsetp");
        exports[13] = NativeLibrary.GetExport(module, "lua_rawgetp");
        exports[14] = NativeLibrary.GetExport(module, "lua_rawgeti");
        exports[15] = NativeLibrary.GetExport(module, "lua_type");
        exports[16] = NativeLibrary.GetExport(module, "lua_pcallk");
        exports[17] = NativeLibrary.GetExport(module, "lua_error");
        exports[18] = NativeLibrary.GetExport(module, "luaL_ref");
        exports[19] = NativeLibrary.GetExport(module, "luaL_unref");
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

    private static int PushSentinel(LuaState state, string operation)
    {
        if (state.Top != 0) return Fail(operation + " started with a non-empty Lua stack");
        state.PushInteger(StackSentinel);
        return state.Top == 1 ? 0 : Fail(operation + " could not establish its Lua stack sentinel");
    }

    private static int AssertErrorThenRestoreSentinel(LuaState state, string operation)
    {
        if (state.Top != 2)
            return Fail(operation + " did not leave exactly one error above the pre-existing stack value");
        state.Pop(1);
        return AssertOnlySentinelRemains(state, operation);
    }

    private static int AssertResultsThenRestoreSentinel(LuaState state, int resultCount, string operation)
    {
        if (state.Top != resultCount + 1)
            return Fail(operation + " did not preserve its pre-existing stack value and leave its documented results");
        state.Pop(resultCount);
        return AssertOnlySentinelRemains(state, operation);
    }

    private static int AssertTableThenRestoreSentinel(LuaState state, string operation)
    {
        if (state.Top != 2 || !state.IsTable(2))
            return Fail(operation + " did not preserve its table while consuming its failing inputs");
        state.Pop(1);
        return AssertOnlySentinelRemains(state, operation);
    }

    private static int AssertOnlySentinelRemains(LuaState state, string operation)
    {
        if (state.Top != 1 || !state.TryReadInteger(1, out var value) || value != StackSentinel)
            return Fail(operation + " did not restore the pre-existing Lua stack exactly");
        state.Pop(1);
        return state.Top == 0 ? 0 : Fail(operation + " left values on the Lua stack after restoration");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int NoOp(nint _)
    {
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void* ProvideRuntimeState()
    {
        return s_runtimeState;
    }

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

    private static void WriteMarker(string marker)
    {
        Console.WriteLine(marker);
        Console.Out.Flush();
    }
}
