using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CheatEngine.SDK.Lua.Callbacks;
using CheatEngine.SDK.Lua.CompilerServices;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.Tests.Support;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Lua.Tests.CompilerServices;

/// <summary>
///     Regression coverage for the identity captured while a lazy global cache resolves through a Lua metamethod.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class LuaGlobalFunctionsStateIdentityTests
{
    private static GlobalResolutionRace? s_race;

    [Fact]
    [SuppressMessage("Meziantou.Analyzer", "MA0051",
        Justification =
            "The regression test must keep the admission barrier, reset, and cache publication assertions in one ordered scenario.")]
    public async Task Resolve_holds_an_operation_lease_until_rebind_so_a_state_reset_cannot_publish_an_old_slot()
    {
        LuaTest.RequireNativeLua();
        var cancellationToken = TestContext.Current.CancellationToken;
        using NativeLuaState state = new();
        var L = LuaTest.View(state);
        using RuntimeScope scope = new(state);
        using GlobalResolutionRace race = new();
        LuaRef cache = new();
        var before = LuaRuntime.CurrentStateIdentity;
        Volatile.Write(ref s_race, race);
        LuaRuntime.OperationAdmissionClosedForTesting = race.AdmissionClosed.Set;

        L.PushUncheckedFunction(CreatePauseResolution());
        Assert.True(L.TrySetGlobal("pauseResolution"u8).IsOk);
        LuaTest.Run(L, """
                       setmetatable(_G, {
                         __index = function(_, name)
                           if name == 'cachedAfterReset' then
                             pauseResolution()
                             return function() return 42 end
                           end
                         end
                       })
                       """u8);

        try
        {
            var resolver = Task.Factory.StartNew(
                () => LuaGlobalFunctions.TryPush(L, cache, "cachedAfterReset"u8), cancellationToken,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Assert.True(race.ResolverPaused.Wait(TimeSpan.FromSeconds(5), cancellationToken),
                "The global resolution did not reach its Lua barrier.");

            var reset = Task.Factory.StartNew(BeginAndCompleteStateReset, cancellationToken,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Assert.True(race.AdmissionClosed.Wait(TimeSpan.FromSeconds(5), cancellationToken),
                "The state reset did not close operation admission.");
            Assert.False(LuaRuntime.TryAcquireOperation(out var rejected));
            rejected.Dispose();
            Assert.False(reset.IsCompleted,
                "The state reset completed while the admitted global resolution was still paused.");

            race.AllowResolverToComplete.Set();
            Assert.True(await resolver.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken));
            await reset.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

            var after = LuaRuntime.CurrentStateIdentity;
            Assert.Equal(before.AttachEpoch, after.AttachEpoch);
            Assert.Equal(before.StateGeneration + 1, after.StateGeneration);
            Assert.True(cache.IsResolved);
            Assert.Equal(before, cache.Identity);
            Assert.False(cache.IsCurrent);

            using (LuaRuntime.EnterStateOperation(L))
            {
                L.Pop(1);
            }

            Assert.Equal(0, L.Top);
        }
        finally
        {
            LuaRuntime.OperationAdmissionClosedForTesting = null;
            race.AllowResolverToComplete.Set();
            Volatile.Write(ref s_race, null);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int PauseResolution(nint ignored)
    {
        try
        {
            var race = Volatile.Read(ref s_race);
            if (race is null) return 0;

            race.ResolverPaused.Set();
            race.AllowResolverToComplete.Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // A bare lua_CFunction has no managed exception boundary. The test's owning thread reports timeout failures.
        }

        return 0;
    }

    private static unsafe LuaNativeFunction CreatePauseResolution()
    {
        return new LuaNativeFunction(&PauseResolution);
    }

    private static void BeginAndCompleteStateReset()
    {
        using var reset = LuaRuntime.BeginStateReset();
    }

    private sealed class GlobalResolutionRace : IDisposable
    {
        public ManualResetEventSlim ResolverPaused { get; } = new(false);

        public ManualResetEventSlim AllowResolverToComplete { get; } = new(false);

        public ManualResetEventSlim AdmissionClosed { get; } = new(false);

        public void Dispose()
        {
            AllowResolverToComplete.Set();
            ResolverPaused.Dispose();
            AllowResolverToComplete.Dispose();
            AdmissionClosed.Dispose();
        }
    }
}
