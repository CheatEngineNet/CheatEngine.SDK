using CESDK.Lua.Interop.Api;
using CESDK.Lua.References;
using CESDK.Lua.Runtime;
using CESDK.Lua.State;

namespace CESDK.Lua.Tests.References;

/// <summary>
///     The epoch logic of <see cref="LuaRef" /> without a Lua library: the runtime is left detached, so
///     <see cref="LuaRuntime.Epoch" /> is whatever the process has reached and nothing here needs a state.
/// </summary>
public sealed class LuaRefEpochTests
{
    [Fact]
    public void A_new_reference_is_unresolved_and_not_current()
    {
        LuaRef reference = new();

        Assert.False(reference.IsResolved);
        Assert.False(reference.IsCurrent);
        Assert.Equal(LuaApi.LUA_NOREF, reference.Reference);
        Assert.Equal(0, reference.Epoch);
        Assert.Equal("LuaRef(unresolved)", reference.ToString());
    }

    [Fact]
    public void A_reference_bound_in_the_current_epoch_is_current()
    {
        LuaRef reference = new();
        var epoch = LuaRuntime.Epoch;

        reference.Rebind(17, epoch);

        Assert.True(reference.IsResolved);
        Assert.True(reference.IsCurrent);
        Assert.Equal(17, reference.Reference);
        Assert.Equal(epoch, reference.Epoch);
        Assert.True(reference.TryGetCurrent(out var slot));
        Assert.Equal(17, slot);
        Assert.Equal($"LuaRef(17, epoch {epoch})", reference.ToString());
    }

    [Fact]
    public void A_reference_from_another_epoch_is_resolved_but_stale()
    {
        LuaRef reference = new();
        reference.Rebind(17, LuaRuntime.Epoch + 1);

        Assert.True(reference.IsResolved);
        Assert.False(reference.IsCurrent);
        Assert.False(reference.TryGetCurrent(out _));
    }

    [Fact]
    public void A_reference_to_nil_is_a_valid_current_reference()
    {
        LuaRef reference = new();
        reference.Rebind(LuaApi.LUA_REFNIL, LuaRuntime.Epoch);

        Assert.True(reference.IsResolved);
        Assert.True(reference.IsCurrent);
    }

    [Fact]
    public void Releasing_a_stale_reference_marks_it_released_without_touching_lua()
    {
        LuaRef reference = new();
        reference.Rebind(17, LuaRuntime.Epoch + 1);

        reference.Release(default);
        reference.Release(default);

        Assert.False(reference.IsResolved);
        Assert.False(reference.IsCurrent);
    }

    [Fact]
    public void Releasing_a_current_reference_with_no_state_only_forgets_it()
    {
        LuaRef reference = new();
        reference.Rebind(17, LuaRuntime.Epoch);

        reference.Release(default);

        Assert.False(reference.IsResolved);
    }

    [Fact]
    public void Dispose_while_detached_forgets_the_reference()
    {
        LuaRuntime.Detach();
        LuaRef reference = new();
        reference.Rebind(17, LuaRuntime.Epoch);

        reference.Dispose();

        Assert.False(reference.IsResolved);
    }

    [Fact]
    public void Pushing_an_unresolved_or_null_reference_pushes_nothing()
    {
        // No state is needed: both checks fail before any C API call is made.
        LuaState none = default;

        Assert.False(none.TryPushRef(new LuaRef()));
        Assert.False(none.TryPushRef(null!));
    }
}
