using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Objects;

/// <summary>Ownership: explicit destroy through the protected call, exactly once, and the transfer API.</summary>
public sealed class OwnedTests
{
    [Fact]
    public void A_null_handle_cannot_be_owned()
    {
        var exception = Assert.Throws<ArgumentException>(() => new Owned<CEObject>(CEObject.Null));
        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void The_wrapper_exposes_the_handle_until_it_is_released()
    {
        CEObject handle = new(0x1234);
        Owned<CEObject> owned = new(handle);

        Assert.False(owned.IsDisposed);
        Assert.Equal(handle, owned.Value);
        Assert.Equal(handle, owned.Handle);
        Assert.Equal(handle, owned.ToBorrowed());
        Assert.Equal("Owned(CEObject@0x1234)", owned.ToString());

        Assert.Equal(handle, owned.Release());
        Assert.True(owned.IsDisposed);
        Assert.Equal("Owned(disposed)", owned.ToString());
        Assert.Throws<ObjectDisposedException>(() => owned.Value);
        Assert.Throws<ObjectDisposedException>(() => owned.Handle);
        Assert.Throws<ObjectDisposedException>(() => owned.ToBorrowed());
        Assert.Throws<ObjectDisposedException>(() => owned.Release());
    }

    [Fact]
    public void Dispose_while_detached_marks_the_wrapper_disposed_without_touching_anything()
    {
        LuaRuntime.Detach();
        Owned<CEObject> owned = new(new CEObject(0x1234));

        owned.Dispose();

        Assert.True(owned.IsDisposed);
        owned.Dispose();
        Assert.True(owned.IsDisposed);
    }

    [Fact]
    public void TryDestroy_while_detached_throws_before_touching_the_state_and_marks_the_wrapper_disposed()
    {
        LuaRuntime.Detach();
        Owned<CEObject> owned = new(new CEObject(0x1234));

        // The null state view is never dereferenced: the push reports the detached runtime first.
        Assert.Throws<InvalidOperationException>(() => owned.TryDestroy(default));

        Assert.True(owned.IsDisposed);
        Assert.True(owned.TryDestroy(default).IsOk);
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void Destroying_from_a_worker_thread_trips_the_debug_guard_before_anything_is_touched()
    {
        EngineTest.RequireNativeLua();
        Assert.SkipUnless(EngineTest.IsDebugBuild,
            "The main-thread guard of Owned<T> is a Debug assertion; Release builds have no runtime check.");
        using NativeLuaState state = new();
        using HostScope scope = new(state); // the binding names this thread as the main thread
        var L = scope.State;
        var handle = FakeHost.CreateObject(L, "Probe");
        Owned<CEObject> owned = new(handle);

        var fromDispose = EngineTest.RunOnWorker(owned.Dispose);
        var fromTryDestroy = EngineTest.RunOnWorker(() => owned.TryDestroy(L));

        var guard = Assert.IsType<DebugAssertFailedException>(fromDispose);
        Assert.Contains("main thread", guard.Message, StringComparison.Ordinal);
        Assert.IsType<DebugAssertFailedException>(fromTryDestroy);
        Assert.False(owned.IsDisposed);
        Assert.False(FakeHost.IsDestroyed(L, handle));
        Assert.Equal(0, FakeHost.DestroyedCount(L));
        Assert.Equal(0, L.Top);

        // Back on the main thread the same wrapper destroys normally.
        owned.Dispose();
        Assert.True(owned.IsDisposed);
        Assert.True(FakeHost.IsDestroyed(L, handle));
        Assert.Equal(0, L.Top);
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void Dispose_with_a_binding_that_has_no_pusher_leaks_the_object_without_throwing()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state, false);
        var L = scope.State;
        var handle = FakeHost.CreateObject(L, "Probe");
        Owned<CEObject> owned = new(handle);

        owned.Dispose();

        Assert.True(owned.IsDisposed);
        Assert.False(FakeHost.IsDestroyed(L, handle));
        Assert.Equal(0, FakeHost.DestroyedCount(L));
        Assert.Equal(0, L.Top);
        owned.Dispose();
        Assert.True(owned.IsDisposed);
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void TryDestroy_with_a_binding_that_has_no_pusher_throws_and_marks_the_wrapper_disposed()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state, false);
        var L = scope.State;
        var handle = FakeHost.CreateObject(L, "Probe");
        Owned<CEObject> owned = new(handle);

        Assert.Throws<InvalidOperationException>(() => owned.TryDestroy(L));

        Assert.True(owned.IsDisposed);
        Assert.False(FakeHost.IsDestroyed(L, handle));
        Assert.Equal(0, L.Top);
        Assert.True(owned.TryDestroy(L).IsOk);
        Assert.Equal(0, L.Top);
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void Dispose_destroys_the_object_exactly_once()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        var handle = FakeHost.CreateObject(L, "Probe");
        Owned<CEObject> owned = new(handle);

        owned.Dispose();

        Assert.True(owned.IsDisposed);
        Assert.True(FakeHost.IsDestroyed(L, handle));
        Assert.Equal(1, FakeHost.DestroyedCount(L));
        Assert.Equal(0, L.Top);

        owned.Dispose();
        Assert.Equal(1, FakeHost.DestroyedCount(L));
        Assert.Equal(0, L.Top);
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void The_using_pattern_destroys_at_the_end_of_the_block()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        var handle = FakeHost.CreateObject(L, "Probe", "o.props.Count = 4");

        using (Owned<CEObject> owned = new(handle))
        {
            Assert.True(owned.Value.TryGetProperty<Int32Marshaller, int>("Count"u8, out var count));
            Assert.Equal(4, count);
            Assert.False(FakeHost.IsDestroyed(L, handle));
        }

        Assert.True(FakeHost.IsDestroyed(L, handle));
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void TryDestroy_returns_the_status_and_marks_disposed_even_when_destroy_raises()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        using LuaFrame frame = new(L);
        Owned<CEObject> stubborn = new(FakeHost.CreateObject(L, "Stubborn"));

        var status = stubborn.TryDestroy(L);

        Assert.Equal(LuaStatus.RuntimeError, status);
        Assert.Contains("refuses to be destroyed", EngineTest.ErrorMessage(L, status), StringComparison.Ordinal);
        Assert.Equal(frame.Top + 1, L.Top);
        Assert.True(stubborn.IsDisposed);
        Assert.Equal(0, FakeHost.DestroyedCount(L));

        // Never retried: a second attempt is a no-op that pushes nothing.
        Assert.True(stubborn.TryDestroy(L).IsOk);
        Assert.Equal(frame.Top + 1, L.Top);
        stubborn.Dispose();
        Assert.Equal(frame.Top + 1, L.Top);
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void TryDestroy_reports_success_and_a_disposed_wrapper_is_inert()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        var handle = FakeHost.CreateObject(L, "Probe");
        Owned<CEObject> owned = new(handle);

        Assert.True(owned.TryDestroy(L).IsOk);
        Assert.Equal(0, L.Top);
        Assert.True(owned.IsDisposed);
        Assert.True(FakeHost.IsDestroyed(L, handle));
        Assert.True(owned.TryDestroy(L).IsOk);
        Assert.Equal(1, FakeHost.DestroyedCount(L));
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void Dispose_swallows_a_raising_destroy_and_restores_the_stack()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        Owned<CEObject> stubborn = new(FakeHost.CreateObject(L, "Stubborn"));

        stubborn.Dispose();

        Assert.True(stubborn.IsDisposed);
        Assert.Equal(0, L.Top);
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void Release_transfers_ownership_so_only_the_new_owner_destroys()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        var handle = FakeHost.CreateObject(L, "Probe");
        Owned<CEObject> first = new(handle);

        var released = first.Release();
        first.Dispose();
        Assert.Equal(handle, released);
        Assert.False(FakeHost.IsDestroyed(L, handle));
        Assert.Equal(0, FakeHost.DestroyedCount(L));

        using (Owned<CEObject> second = new(released))
        {
            Assert.Equal(handle, second.Handle);
        }

        Assert.True(FakeHost.IsDestroyed(L, handle));
        Assert.Equal(1, FakeHost.DestroyedCount(L));
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void Dispose_after_the_plugin_is_disabled_leaks_the_object_instead_of_calling_into_nothing()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        LuaState L;
        CEObject handle;
        Owned<CEObject> owned;
        using (HostScope scope = new(state))
        {
            L = scope.State;
            handle = FakeHost.CreateObject(L, "Probe");
            owned = new Owned<CEObject>(handle);
        }

        Assert.False(LuaRuntime.IsAttached);
        owned.Dispose();

        Assert.True(owned.IsDisposed);
        Assert.False(FakeHost.IsDestroyed(L, handle));
        Assert.Equal(0, FakeHost.DestroyedCount(L));
        Assert.Equal(0, L.Top);
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void A_borrowed_view_stays_usable_while_the_owner_lives()
    {
        EngineTest.RequireNativeLua();
        using NativeLuaState state = new();
        using HostScope scope = new(state);
        var L = scope.State;
        using Owned<CEObject> owned = new(FakeHost.CreateObject(L, "Probe", "o.props.Count = 8"));

        var borrowed = owned.ToBorrowed();
        Assert.True(borrowed.TryGetProperty<Int32Marshaller, int>("Count"u8, out var count));
        Assert.Equal(8, count);
        Assert.True(borrowed.TryCallMethod<Int32Marshaller, int>("getCount"u8, out count));
        Assert.Equal(8, count);
    }
}
