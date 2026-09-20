using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Lua.Tests.Support;
using CheatEngine.SDK.Tests.Shared.NativeLua;
using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

namespace CheatEngine.SDK.Lua.Tests.References;

/// <summary>
///     Registry-reference isolation under the host's multi-threaded Lua model. Every worker receives its own rooted Lua
///     coroutine; workers never share a Lua stack.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class ReferenceConcurrencyTests
{
    [Fact]
    public async Task Concurrent_sdk_references_do_not_disturb_a_host_registry_reference()
    {
        LuaTest.RequireNativeLua();
        var cancellationToken = TestContext.Current.CancellationToken;
        using NativeLuaState state = new(false);
        var main = LuaTest.View(state);
        using RuntimeScope scope = new(state);
        using var first = RootedThread.Create(main);
        using var second = RootedThread.Create(main);
        using var host = RootedThread.Create(main);
        using ManualResetEventSlim start = new(false);
        using ManualResetEventSlim hostCreated = new(false);
        using ManualResetEventSlim releaseSdkReferences = new(false);
        using CountdownEvent sdkCreated = new(2);
        using CountdownEvent sdkReleased = new(2);

        var firstSdk = StartWorker(() => CreateReadAndRelease(first.State, 101, start, hostCreated, sdkCreated,
            releaseSdkReferences, sdkReleased, cancellationToken), cancellationToken);
        var secondSdk = StartWorker(() => CreateReadAndRelease(second.State, 202, start, hostCreated, sdkCreated,
            releaseSdkReferences, sdkReleased, cancellationToken), cancellationToken);
        var hostWorker = StartWorker(() => CreateAndReadHostReference(host.State, start, hostCreated, sdkCreated,
            releaseSdkReferences, sdkReleased, cancellationToken), cancellationToken);

        start.Set();
        var results = await Task.WhenAll(firstSdk, secondSdk, hostWorker)
            .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

        Assert.Equal(101, results[0]);
        Assert.Equal(202, results[1]);
        Assert.Equal(909, results[2]);
    }

    private static Task<int> StartWorker(Func<int> work, CancellationToken cancellationToken)
    {
        return Task.Factory.StartNew(work, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private static int CreateReadAndRelease(LuaState state, int expected, ManualResetEventSlim start,
        ManualResetEventSlim hostCreated, CountdownEvent sdkCreated, ManualResetEventSlim releaseSdkReferences,
        CountdownEvent sdkReleased, CancellationToken cancellationToken)
    {
        LuaRef? reference = null;
        var created = false;
        try
        {
            if (!start.Wait(TimeSpan.FromSeconds(5), cancellationToken))
                throw new TimeoutException("The worker start barrier timed out.");
            if (!hostCreated.Wait(TimeSpan.FromSeconds(5), cancellationToken))
                throw new TimeoutException("The host did not create its reference.");

            state.PushInteger(expected);
            reference = state.CreateRef();
            Assert.True(state.TryPushRef(reference));
            Assert.True(state.TryReadInteger(-1, out var actual));
            state.Pop(1);
            Assert.Equal(expected, actual);
            sdkCreated.Signal();
            created = true;

            if (!releaseSdkReferences.Wait(TimeSpan.FromSeconds(5), cancellationToken))
                throw new TimeoutException("The SDK reference release barrier timed out.");

            reference.Release(state);
            reference = null;
            return checked((int)actual);
        }
        finally
        {
            reference?.Release(state);
            if (!created) sdkCreated.Signal();
            sdkReleased.Signal();
        }
    }

    private static unsafe int CreateAndReadHostReference(LuaState state, ManualResetEventSlim start,
        ManualResetEventSlim hostCreated, CountdownEvent sdkCreated, ManualResetEventSlim releaseSdkReferences,
        CountdownEvent sdkReleased, CancellationToken cancellationToken)
    {
        var reference = LUA_NOREF;
        try
        {
            if (!start.Wait(TimeSpan.FromSeconds(5), cancellationToken))
                throw new TimeoutException("The worker start barrier timed out.");

            state.PushInteger(909);
            reference = luaL_ref(state.Pointer, LUA_REGISTRYINDEX);

            // Seed a host-owned free-list node after its live value. Old SDK references used this very table and would
            // consume the node while they were live; private SDK references leave the host's registry untouched.
            state.PushInteger(1);
            var scratch = luaL_ref(state.Pointer, LUA_REGISTRYINDEX);
            luaL_unref(state.Pointer, LUA_REGISTRYINDEX, scratch);
            var freeHead = ReadRegistryInteger(state, 0);
            hostCreated.Set();
            if (!sdkCreated.Wait(TimeSpan.FromSeconds(5), cancellationToken))
                throw new TimeoutException("The SDK did not create both references.");

            Assert.Equal(freeHead, ReadRegistryInteger(state, 0));

            releaseSdkReferences.Set();
            if (!sdkReleased.Wait(TimeSpan.FromSeconds(5), cancellationToken))
                throw new TimeoutException("The SDK did not release both references.");

            _ = lua_rawgeti(state.Pointer, LUA_REGISTRYINDEX, reference);
            Assert.True(state.TryReadInteger(-1, out var actual));
            state.Pop(1);
            return checked((int)actual);
        }
        finally
        {
            hostCreated.Set();
            releaseSdkReferences.Set();
            if (reference != LUA_NOREF) luaL_unref(state.Pointer, LUA_REGISTRYINDEX, reference);
        }
    }

    private static unsafe long ReadRegistryInteger(LuaState state, int key)
    {
        _ = lua_rawgeti(state.Pointer, LUA_REGISTRYINDEX, key);
        Assert.True(state.TryReadInteger(-1, out var value));
        state.Pop(1);
        return value;
    }

    private sealed unsafe class RootedThread : IDisposable
    {
        private readonly LuaRef _root;

        private RootedThread(LuaState state, LuaRef root)
        {
            State = state;
            _root = root;
        }

        public LuaState State { get; }

        public void Dispose()
        {
            _root.Release(LuaRuntime.AcquireState());
        }

        public static RootedThread Create(LuaState main)
        {
            var thread = lua_newthread(main.Pointer);
            Assert.NotEqual(nint.Zero, (nint)thread);
            return new RootedThread(new LuaState(thread), main.CreateRef());
        }
    }
}
