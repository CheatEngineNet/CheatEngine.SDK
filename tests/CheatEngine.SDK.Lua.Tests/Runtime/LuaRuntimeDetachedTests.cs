using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.Tests.Support;

namespace CheatEngine.SDK.Lua.Tests.Runtime;

/// <summary>The runtime while no host is attached: every accessor fails cleanly. No Lua library involved.</summary>
public sealed class LuaRuntimeDetachedTests
{
    [Fact]
    public void Detached_runtime_reports_itself_as_such()
    {
        LuaRuntime.Detach();

        Assert.False(LuaRuntime.IsAttached);
        Assert.False(LuaRuntime.IsMainThread);
        Assert.Equal(default, LuaRuntime.CurrentBinding);
        Assert.False(LuaRuntime.TryAcquireState(out var state));
        Assert.True(state.IsNull);
    }

    [Fact]
    public void Detach_is_idempotent_and_does_not_move_the_epoch()
    {
        LuaRuntime.Detach();
        var epoch = LuaRuntime.Epoch;

        LuaRuntime.Detach();
        LuaRuntime.Detach();

        Assert.Equal(epoch, LuaRuntime.Epoch);
    }

    [Fact]
    public void AcquireState_throws_while_detached()
    {
        LuaRuntime.Detach();

        var exception = Assert.Throws<InvalidOperationException>(() => LuaRuntime.AcquireState());
        Assert.Contains("not enabled", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PushHostObject_throws_while_detached()
    {
        LuaRuntime.Detach();

        Assert.Throws<InvalidOperationException>(() => LuaRuntime.PushHostObject(default, 0x1234));
    }

    [Fact]
    public void Attach_rejects_a_binding_without_a_state_provider()
    {
        LuaRuntime.Detach();
        LuaHostBinding invalid = new(0, HostDouble.PusherAddress, Environment.CurrentManagedThreadId);
        var epoch = LuaRuntime.Epoch;

        var exception = Assert.Throws<ArgumentException>(() => LuaRuntime.Attach(in invalid));

        Assert.Equal("binding", exception.ParamName);
        Assert.False(invalid.IsValid);
        Assert.False(LuaRuntime.IsAttached);
        Assert.Equal(epoch, LuaRuntime.Epoch);
    }

    [Fact]
    public void Binding_equality_covers_all_three_fields()
    {
        LuaHostBinding a = new(0x10, 0x20, 7);
        LuaHostBinding b = new(0x10, 0x20, 7);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.True(a != new LuaHostBinding(0x10, 0x20, 8));
        Assert.True(a != new LuaHostBinding(0x10, 0x21, 7));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.True(a.IsValid);
        Assert.Equal(0x10, a.StateProvider);
        Assert.Equal(0x20, a.HostObjectPusher);
        Assert.Equal(7, a.MainThreadId);
    }
}
