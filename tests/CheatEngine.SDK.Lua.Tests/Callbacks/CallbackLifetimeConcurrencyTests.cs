using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CheatEngine.SDK.Lua.Callbacks;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Lua.Tests.Support;
using CheatEngine.SDK.Tests.Shared.NativeLua;
using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

namespace CheatEngine.SDK.Lua.Tests.Callbacks;

/// <summary>
///     Lifetime races around a callback's GCHandle. Each Lua call uses a separately rooted coroutine on its own OS
///     thread; while that thread is paused in managed code, the main state can safely neutralize the callback.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class CallbackLifetimeConcurrencyTests
{
    private static CallbackRace? s_race;

    private static unsafe LuaNativeFunction PauseBeforeLookupFunction => new(&PauseBeforeLookup);

    private static unsafe LuaNativeFunction PauseAfterLookupFunction => new(&PauseAfterLookup);

    [Fact]
    public async Task Release_waits_for_a_thunk_before_its_state_lookup_then_the_thunk_reports_released()
    {
        LuaTest.RequireNativeLua();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using NativeLuaState state = new();
        var main = LuaTest.View(state);
        using RuntimeScope scope = new(state);
        using CallbackRace race = new(cancellationToken);
        s_race = race;

        try
        {
            Counter counter = new();
            Assert.True(LuaCallback.TryCreate(main, PauseBeforeLookupFunction, counter, out var callback)
                .IsOk);
            Assert.NotNull(callback);

            using RootedThread worker = RootedThread.Create(main);
            Assert.True(callback.TryPush(worker.State));
            Task<LuaStatus> call = StartCall(worker.State, cancellationToken);
            Assert.True(race.Entered.Wait(TimeSpan.FromSeconds(5), cancellationToken),
                "The callback did not reach its pre-lookup barrier.");

            // The worker has entered the thunk but is not yet allowed to call TryGetState. Holding the same gate makes
            // that lookup wait while this thread neutralizes the closure and frees its handle.
            lock (LuaCallbackRegistry.Gate)
            {
                race.Continue.Set();
                Assert.True(race.LookupAttempted.Wait(TimeSpan.FromSeconds(5), cancellationToken),
                    "The callback did not attempt its state lookup.");
                Assert.False(race.LookupReturned.Wait(TimeSpan.FromMilliseconds(250), cancellationToken),
                    "TryGetState completed while release owned the callback lifetime gate.");
                callback.Release(main);
            }

            LuaStatus status = await call.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            Assert.Equal(LuaStatus.RuntimeError, status);
            Assert.True(callback.IsReleased);
            Assert.Null(callback.StateObject);
            Assert.Equal(0, counter.Value);
        }
        finally
        {
            s_race = null;
        }
    }

    [Fact]
    public async Task A_thunk_that_already_acquired_state_can_finish_after_release_frees_its_handle()
    {
        LuaTest.RequireNativeLua();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using NativeLuaState state = new();
        var main = LuaTest.View(state);
        using RuntimeScope scope = new(state);
        using CallbackRace race = new(cancellationToken);
        s_race = race;

        try
        {
            Counter counter = new();
            Assert.True(LuaCallback.TryCreate(main, PauseAfterLookupFunction, counter, out var callback)
                .IsOk);
            Assert.NotNull(callback);

            using RootedThread worker = RootedThread.Create(main);
            Assert.True(callback.TryPush(worker.State));
            Task<LuaStatus> call = StartCall(worker.State, cancellationToken);
            Assert.True(race.Entered.Wait(TimeSpan.FromSeconds(5), cancellationToken),
                "The callback did not acquire its managed state.");

            callback.Release(main);
            Assert.Null(callback.StateObject);
            race.Continue.Set();

            LuaStatus status = await call.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            Assert.Equal(LuaStatus.Ok, status);
            Assert.Equal(1, counter.Value);
        }
        finally
        {
            s_race = null;
        }
    }

    private static Task<LuaStatus> StartCall(LuaState state, CancellationToken cancellationToken)
    {
        return Task.Factory.StartNew(() => state.TryCall(0, 1), cancellationToken,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int PauseBeforeLookup(nint pointer)
    {
        LuaState state = new(pointer);
        try
        {
            CallbackRace race = s_race ?? throw new InvalidOperationException("No callback race is active.");
            race.Entered.Set();
            if (!race.Continue.Wait(TimeSpan.FromSeconds(5), race.CancellationToken))
                return LuaThunk.Fail(state, "test barrier timed out"u8);

            race.LookupAttempted.Set();
            var found = LuaThunk.TryGetState(state, out Counter? _);
            race.LookupReturned.Set();
            return found ? 0 : LuaThunk.Fail(state, "callback released"u8);
        }
        catch (Exception exception)
        {
            return LuaThunk.Fail(state, exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe int PauseAfterLookup(nint pointer)
    {
        LuaState state = new(pointer);
        try
        {
            CallbackRace race = s_race ?? throw new InvalidOperationException("No callback race is active.");
            if (!LuaThunk.TryGetState(state, out Counter? counter)) return LuaThunk.Fail(state, "callback released"u8);

            race.Entered.Set();
            if (!race.Continue.Wait(TimeSpan.FromSeconds(5), race.CancellationToken))
                return LuaThunk.Fail(state, "test barrier timed out"u8);

            counter.Value++;
            Int32Marshaller.Push(state, counter.Value);
            return 1;
        }
        catch (Exception exception)
        {
            return LuaThunk.Fail(state, exception);
        }
    }

    private sealed class CallbackRace(CancellationToken cancellationToken) : IDisposable
    {
        public ManualResetEventSlim Entered { get; } = new(false);

        public ManualResetEventSlim Continue { get; } = new(false);

        public ManualResetEventSlim LookupAttempted { get; } = new(false);

        public ManualResetEventSlim LookupReturned { get; } = new(false);

        public CancellationToken CancellationToken { get; } = cancellationToken;

        public void Dispose()
        {
            Continue.Set();
            Entered.Dispose();
            Continue.Dispose();
            LookupAttempted.Dispose();
            LookupReturned.Dispose();
        }
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

        public static RootedThread Create(LuaState main)
        {
            var thread = lua_newthread(main.Pointer);
            Assert.NotEqual(nint.Zero, (nint)thread);
            return new RootedThread(new LuaState(thread), main.CreateRef());
        }

        public void Dispose()
        {
            _root.Release(LuaRuntime.AcquireState());
        }
    }
}
