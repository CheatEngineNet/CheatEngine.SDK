using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using CheatEngine.SDK.Hosting.Diagnostics;
using CheatEngine.SDK.Lua.Callbacks;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Hosting.Threading;

/// <summary>
///     The cross-thread half of <see cref="MainThread" />: hands a <see cref="MainThreadWorkItem" /> to Cheat Engine's Lua
///     global <c>synchronize(function, ...)</c>, which runs the function on the main thread and returns when it has
///     completed. The function is a C closure over the dispatch thunk with the work item's <c>GCHandle&lt;object&gt;</c>
///     as its only upvalue; the thunk verifies that the host actually invoked it on the captured main thread before it
///     runs the item, then returns no Lua values.
/// </summary>
/// <remarks>
///     Cost per dispatch: one work item, one lifetime-managed Lua callback and one protected call, plus the host's own
///     synchronization. This is a thread hop, never a hot path. The thunk rejects a host that runs the closure on the
///     wrong managed thread, so an inline stand-in cannot accidentally make a worker-thread execution look valid. Live
///     verification of Cheat Engine 7.7's actual scheduling contract remains a separate opt-in probe.
/// </remarks>
internal static unsafe class MainThreadDispatcher
{
    // The simulated host uses this narrow internal seam to queue the exact work item that Dispatch would otherwise
    // hand to Lua. It is reset before every test and never reaches the public API or a production host path.
    private static Action<MainThreadWorkItem>? s_dispatchOverrideForTests;

    internal static Action<MainThreadWorkItem>? DispatchOverrideForTests
    {
        get => Volatile.Read(ref s_dispatchOverrideForTests);
        set => Volatile.Write(ref s_dispatchOverrideForTests, value);
    }

    /// <summary>Runs <paramref name="item" /> through the host's <c>synchronize</c> global on the calling thread's Lua state.</summary>
    /// <param name="item">
    ///     The work; its outcome is available through <see cref="MainThreadWorkItem.ThrowIfFailed" />
    ///     afterwards.
    /// </param>
    /// <exception cref="InvalidOperationException">
    ///     The plugin is not enabled, the host has no <c>synchronize</c> function, or
    ///     the call failed on the Lua side.
    /// </exception>
    internal static void Dispatch(MainThreadWorkItem item)
    {
        var dispatchOverride = Volatile.Read(ref s_dispatchOverrideForTests);
        if (dispatchOverride is not null)
        {
            dispatchOverride(item);
            return;
        }

        using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
        var l = operation.State;
        using LuaFrame frame = new(l);

        var status = l.TryGetGlobal("synchronize"u8);
        if (!status.IsOk)
            throw new InvalidOperationException("The host's 'synchronize' global could not be read: " +
                                                LuaError.FromStack(l, status).Message);

        if (!l.IsFunction(-1))
            throw new InvalidOperationException(
                "The host defines no 'synchronize' function; main-thread dispatch needs Cheat Engine's Lua environment.");

        status = LuaCallback.TryCreate(l, new LuaNativeFunction(&Thunk), item, out var callback);
        if (!status.IsOk)
            throw new InvalidOperationException("The dispatch callback could not be created: " +
                                                LuaError.FromStack(l, status).Message);

        using (callback)
        {
            if (!callback!.TryPush(l))
                throw new InvalidOperationException("The plugin was disabled before the dispatch callback could run.");
            status = l.TryCall(1, 0);
        }

        if (!status.IsOk)
            throw new InvalidOperationException("The host's 'synchronize' call failed: " +
                                                LuaError.FromStack(l, status).Message);
    }

    // lua_CFunction: runs the work item carried by upvalue 1. Returns no values; failures are captured in the item.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Thunk(nint l)
    {
        try
        {
            if (!LuaThunk.TryGetState(new LuaState(l), out MainThreadWorkItem? item))
            {
                HostLog.Error("The main-thread dispatch thunk received no managed work item.");
                return 0;
            }

            ExecuteOnHostMainThread(item);
        }
        catch (Exception exception)
        {
            HostLog.Error("The main-thread dispatch thunk failed.", exception);
        }

        return 0;
    }

    internal static void ExecuteQueuedWorkForTests(MainThreadWorkItem item)
    {
        ExecuteOnHostMainThread(item);
    }

    internal static void ResetForTests()
    {
        Volatile.Write(ref s_dispatchOverrideForTests, null);
    }

    private static void ExecuteOnHostMainThread(MainThreadWorkItem item)
    {
        if (!MainThread.IsMainThread)
        {
            item.Reject(new InvalidOperationException(
                "The host's synchronize callback ran dispatched work on a thread other than the enabled plugin main thread."));
            return;
        }

        item.Execute();
    }
}
