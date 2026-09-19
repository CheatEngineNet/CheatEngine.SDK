using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.Tests.Support;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Lua.Tests.Runtime;

/// <summary>
///     Attach and detach with an <c>[UnmanagedCallersOnly]</c> host double, and what the runtime hands out in
///     between.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed unsafe class LuaRuntimeTests
{
    [Fact]
    public void Attach_publishes_the_binding_and_advances_the_epoch_once()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        LuaRuntime.Detach();
        var epoch = LuaRuntime.Epoch;

        using (RuntimeScope scope = new(state))
        {
            Assert.True(LuaRuntime.IsAttached);
            Assert.Equal(epoch + 1, LuaRuntime.Epoch);
            Assert.Equal(scope.Binding, LuaRuntime.CurrentBinding);
            Assert.Equal(HostDouble.ProviderAddress, LuaRuntime.CurrentBinding.StateProvider);
            Assert.Equal(HostDouble.PusherAddress, LuaRuntime.CurrentBinding.HostObjectPusher);
            Assert.True(LuaRuntime.IsMainThread);
        }

        Assert.False(LuaRuntime.IsAttached);
        Assert.Equal(epoch + 1, LuaRuntime.Epoch);
        Assert.False(LuaRuntime.IsMainThread);
    }

    [Fact]
    public void AcquireState_calls_the_provider_once_per_call_and_returns_its_state()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        using RuntimeScope scope = new(state);

        var first = LuaRuntime.AcquireState();
        var second = LuaRuntime.AcquireState();
        Assert.True(LuaRuntime.TryAcquireState(out var third));

        Assert.Equal(state.Pointer, first.Handle);
        Assert.Equal(first, second);
        Assert.Equal(first, third);
        Assert.Equal(3, HostDouble.ProviderCalls);

        first.PushInteger(11);
        Assert.Equal(1, LuaTest.View(state).Top);
    }

    [Fact]
    public void A_provider_that_returns_no_state_is_reported()
    {
        LuaTest.RequireNativeLua();
        LuaRuntime.Detach();
        var binding = HostDouble.CreateBinding(null);
        LuaRuntime.Attach(in binding);
        try
        {
            Assert.False(LuaRuntime.TryAcquireState(out var state));
            Assert.True(state.IsNull);
            var exception = Assert.Throws<InvalidOperationException>(() => LuaRuntime.AcquireState());
            Assert.Contains("no Lua state", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            LuaRuntime.Detach();
        }
    }

    [Fact]
    public void IsMainThread_is_false_on_another_thread()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        using RuntimeScope scope = new(state);
        var onMain = LuaRuntime.IsMainThread;
        var onWorker = true;

        Thread worker = new(() => onWorker = LuaRuntime.IsMainThread);
        worker.Start();
        worker.Join();

        Assert.True(onMain);
        Assert.False(onWorker);
    }

    [Fact]
    public void PushHostObject_calls_the_pusher_with_the_object_pointer()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        using RuntimeScope scope = new(state);
        var L = LuaRuntime.AcquireState();

        LuaRuntime.PushHostObject(L, 0xBEEF);

        Assert.Equal(1, HostDouble.PusherCalls);
        Assert.Equal(0xBEEF, HostDouble.LastPushedObject);
        Assert.Equal(1, L.Top);
        Assert.True(L.IsLightUserdata(-1));
        Assert.Equal(0xBEEF, L.ToUserdata(-1));
    }

    [Fact]
    public void PushHostObject_without_a_pusher_throws_instead_of_jumping_to_zero()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        using RuntimeScope scope = new(state, false);
        var L = LuaRuntime.AcquireState();

        Assert.Equal(0, scope.Binding.HostObjectPusher);
        Assert.Throws<InvalidOperationException>(() => LuaRuntime.PushHostObject(L, 1));
        Assert.Equal(0, L.Top);
    }

    [Fact]
    public void Attach_while_attached_replaces_the_binding_and_advances_the_epoch_again()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        LuaRuntime.Detach();
        var epoch = LuaRuntime.Epoch;
        var first = HostDouble.CreateBinding(state.L, false);
        LuaRuntime.Attach(in first);
        try
        {
            var second = HostDouble.CreateBinding(state.L);
            LuaRuntime.Attach(in second);

            Assert.Equal(epoch + 2, LuaRuntime.Epoch);
            Assert.Equal(second, LuaRuntime.CurrentBinding);
            Assert.NotEqual(first, second);
        }
        finally
        {
            LuaRuntime.Detach();
        }
    }
}
