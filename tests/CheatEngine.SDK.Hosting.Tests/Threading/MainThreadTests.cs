using System.Runtime.CompilerServices;
using CheatEngine.SDK.Hosting.Bootstrap;
using CheatEngine.SDK.Hosting.Tests.Support;
using CheatEngine.SDK.Hosting.Threading;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Interop.Api;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Hosting.Tests.Threading;

/// <summary>
///     Main-thread identity, the two message-loop wrappers over the exports doubles, and dispatch through a Lua
///     stand-in for Cheat Engine's <c>synchronize</c> (which runs the function inline, so the mechanics are exercised
///     without a real thread hop).
/// </summary>
public sealed unsafe class MainThreadTests
{
    [Fact]
    public void Everything_needs_an_enabled_plugin()
    {
        HostingTest.Reset();

        Assert.False(MainThread.IsMainThread);
        Assert.Throws<InvalidOperationException>(MainThread.ProcessMessages);
        Assert.Throws<InvalidOperationException>(() => MainThread.CheckSynchronize(0));
        Assert.Throws<InvalidOperationException>(() => MainThread.Invoke(static _ => { }, 0));
        Assert.Throws<InvalidOperationException>(() => MainThread.Invoke(static x => x, 0));
        Assert.Throws<ArgumentNullException>(() => MainThread.Invoke(null!, 0));
        Assert.Throws<ArgumentNullException>(() => MainThread.Invoke<int, int>(null!, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => MainThread.CheckSynchronize(-1));
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void ProcessMessages_and_CheckSynchronize_call_the_host_slots_on_the_main_thread()
    {
        HostingTest.RequireNativeLua();
        HostingTest.Reset();
        using NativeLuaState state = new();
        using HostSimulator host = new();
        HostingTest.Enable(host, state);

        MainThread.ProcessMessages();
        MainThread.ProcessMessages();
        FakeExports.CheckSynchronizeRawResult = 0xFF;
        var ran = MainThread.CheckSynchronize(250);
        FakeExports.CheckSynchronizeRawResult = 0;
        var idle = MainThread.CheckSynchronize(0);

        Assert.True(MainThread.IsMainThread);
        Assert.Equal(2, FakeExports.ProcessMessagesCalls);
        Assert.Equal(2, FakeExports.CheckSynchronizeCalls);
        Assert.Equal(0, FakeExports.LastTimeout);
        Assert.True(ran);
        Assert.False(idle);
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void The_pump_operations_refuse_a_worker_thread_instead_of_running_there()
    {
        HostingTest.RequireNativeLua();
        HostingTest.Reset();
        using NativeLuaState state = new();
        using HostSimulator host = new();
        HostingTest.Enable(host, state);

        var (isMain, pump, check) = RunOnWorker(static () =>
        {
            var isMain = MainThread.IsMainThread;
            var pump = Record.Exception(MainThread.ProcessMessages);
            var check = Record.Exception(() => MainThread.CheckSynchronize(0));
            return (isMain, pump, check);
        });

        Assert.False(isMain);
        Assert.IsType<InvalidOperationException>(pump);
        Assert.IsType<InvalidOperationException>(check);
        Assert.Equal(0, FakeExports.ProcessMessagesCalls);
        Assert.Equal(0, FakeExports.CheckSynchronizeCalls);
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void Slots_the_host_left_empty_are_reported_not_jumped_to()
    {
        HostingTest.RequireNativeLua();
        HostingTest.Reset();
        using NativeLuaState state = new();
        using HostSimulator host = new();
        HostingTest.UseFixture(state);
        HostingTest.Bootstrap(host);
        var exports = FakeExports.Create(withPusher: false, withPump: false);
        Assert.True(host.CallEnable(&exports, 1).IsTrue);

        Assert.False(PluginHost.Context!.HasProcessMessages);
        Assert.False(PluginHost.Context.HasCheckSynchronize);
        Assert.Equal(0, PluginHost.Context.HostBinding.HostObjectPusher);
        var pump = Assert.Throws<InvalidOperationException>(MainThread.ProcessMessages);
        Assert.Contains("ProcessMessages", pump.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => MainThread.CheckSynchronize(0));
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void Invoke_on_the_main_thread_runs_inline_without_Lua()
    {
        HostingTest.RequireNativeLua();
        HostingTest.Reset();
        using NativeLuaState state = new();
        using HostSimulator host = new();
        HostingTest.Enable(host, state);
        var providerCallsBefore = FakeExports.GetLuaStateCalls;
        var box = new int[1];

        MainThread.Invoke(static b => b[0] = Environment.CurrentManagedThreadId, box);
        var doubled = MainThread.Invoke(static x => x * 2, 21);

        Assert.Equal(42, doubled);
        Assert.Equal(Environment.CurrentManagedThreadId, box[0]);
        Assert.Equal(providerCallsBefore, FakeExports.GetLuaStateCalls);
        Assert.Equal(0, LuaApi.lua_gettop(state.L));
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void Invoke_from_a_worker_without_a_synchronize_global_fails_with_a_clear_message()
    {
        HostingTest.RequireNativeLua();
        HostingTest.Reset();
        using NativeLuaState state = new();
        using HostSimulator host = new();
        HostingTest.Enable(host, state);

        var failure = RunOnWorker(static () => Record.Exception(() => MainThread.Invoke(static _ => { }, 0)));

        var exception = Assert.IsType<InvalidOperationException>(failure);
        Assert.Contains("synchronize", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, LuaApi.lua_gettop(state.L));
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void Invoke_from_a_worker_goes_through_synchronize_and_returns_the_result()
    {
        HostingTest.RequireNativeLua();
        HostingTest.Reset();
        using NativeLuaState state = new();
        using HostSimulator host = new();
        HostingTest.Enable(host, state);
        InstallSynchronizeStandIn(state);

        var (result, workerId, ranOnId) = RunOnWorker(static () =>
        {
            var ranOn = new int[1];
            var result = MainThread.Invoke(
                static box =>
                {
                    box[0] = Environment.CurrentManagedThreadId;
                    return 6 * 7;
                },
                ranOn);
            return (result, Environment.CurrentManagedThreadId, ranOn[0]);
        });

        Assert.Equal(42, result);
        Assert.Equal(workerId, ranOnId); // the stand-in runs the function where it is called; a real host would hop
        Assert.True(PluginHost.IsEnabled);
        Assert.Equal(0, LuaApi.lua_gettop(state.L));
        Assert.Equal(1, ReadGlobalInteger(state, "synchronize_calls"u8));
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void An_exception_thrown_by_the_dispatched_work_is_rethrown_on_the_caller()
    {
        HostingTest.RequireNativeLua();
        var sink = HostingTest.Reset();
        using NativeLuaState state = new();
        using HostSimulator host = new();
        HostingTest.Enable(host, state);
        InstallSynchronizeStandIn(state);

        var failure = RunOnWorker(static () => Record.Exception(() => MainThread.Invoke(
            static message => throw new NotSupportedException(message),
            "from the main thread")));

        var exception = Assert.IsType<NotSupportedException>(failure);
        Assert.Equal("from the main thread", exception.Message);
        Assert.Empty(sink.Errors("dispatch thunk"));
        Assert.Equal(0, LuaApi.lua_gettop(state.L));
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void A_synchronize_that_does_not_run_the_function_is_reported()
    {
        HostingTest.RequireNativeLua();
        HostingTest.Reset();
        using NativeLuaState state = new();
        using HostSimulator host = new();
        HostingTest.Enable(host, state);
        var L = LuaRuntime.AcquireState();
        using (LuaFrame frame = new(L))
        {
            Assert.True(L.TryExecute("function synchronize(f, ...) end"u8, 0).IsOk);
        }

        var failure = RunOnWorker(static () => Record.Exception(() => MainThread.Invoke(static _ => { }, 0)));

        var exception = Assert.IsType<InvalidOperationException>(failure);
        Assert.Contains("without running", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void A_synchronize_that_raises_is_reported_with_the_Lua_message()
    {
        HostingTest.RequireNativeLua();
        HostingTest.Reset();
        using NativeLuaState state = new();
        using HostSimulator host = new();
        HostingTest.Enable(host, state);
        var L = LuaRuntime.AcquireState();
        using (LuaFrame frame = new(L))
        {
            Assert.True(L.TryExecute("function synchronize(f, ...) error('host refused') end"u8, 0).IsOk);
        }

        var failure = RunOnWorker(static () => Record.Exception(() => MainThread.Invoke(static _ => { }, 0)));

        var exception = Assert.IsType<InvalidOperationException>(failure);
        Assert.Contains("host refused", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, LuaApi.lua_gettop(state.L));
    }

    private static void InstallSynchronizeStandIn(NativeLuaState state)
    {
        var L = LuaRuntime.AcquireState();
        using LuaFrame frame = new(L);
        var status = L.TryExecute(
            "synchronize_calls = 0; function synchronize(f, ...) synchronize_calls = synchronize_calls + 1; return f(...) end"u8,
            0);
        Assert.True(status.IsOk, "stand-in install failed: " + LuaError.FromStack(L, status).Message);
        Assert.Equal(0, LuaApi.lua_gettop(state.L));
    }

    private static long ReadGlobalInteger(NativeLuaState state, ReadOnlySpan<byte> name)
    {
        LuaState L = new(state.Pointer);
        using LuaFrame frame = new(L);
        Assert.True(L.TryGetGlobal(name).IsOk);
        Assert.True(L.TryReadInteger(-1, out var value));
        return value;
    }

    private static T RunOnWorker<T>(Func<T> work)
    {
        // Boxes, not captured locals: static analysis (S2583) does not see a lambda's write to a captured local.
        // Join publishes the worker's writes to this thread.
        StrongBox<T?> result = new();
        StrongBox<Exception?> crash = new();
        Thread worker = new(() =>
        {
            try
            {
                result.Value = work();
            }
            catch (Exception exception)
            {
                crash.Value = exception;
            }
        });
        worker.Start();
        worker.Join();
        if (crash.Value is { } failure) throw new InvalidOperationException("The worker crashed.", failure);

        return result.Value!;
    }
}
