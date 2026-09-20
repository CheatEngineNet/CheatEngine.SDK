using CheatEngine.SDK.Lua.Interop.Api;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.SDK.Lua.Tests.References;

/// <summary>
///     The state-identity logic of <see cref="LuaRef" /> without a Lua library: the runtime is left detached, so
///     <see cref="LuaRuntime.CurrentStateIdentity" /> is whatever the process has reached and nothing here needs a state.
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
        Assert.Equal(default, reference.Identity);
        Assert.Equal(0, reference.Epoch);
        Assert.Equal(0, reference.StateGeneration);
        Assert.Equal("LuaRef(unresolved)", reference.ToString());
    }

    [Fact]
    public void A_reference_bound_in_the_current_state_identity_is_current()
    {
        LuaRef reference = new();
        var identity = LuaRuntime.CurrentStateIdentity;

        reference.Rebind(17, identity);

        Assert.True(reference.IsResolved);
        Assert.True(reference.IsCurrent);
        Assert.Equal(17, reference.Reference);
        Assert.Equal(identity, reference.Identity);
        Assert.Equal(identity.AttachEpoch, reference.Epoch);
        Assert.Equal(identity.StateGeneration, reference.StateGeneration);
        Assert.True(reference.TryGetCurrent(out var slot));
        Assert.Equal(17, slot);
        Assert.Equal(
            $"LuaRef(17, attach epoch {identity.AttachEpoch}, state generation {identity.StateGeneration})",
            reference.ToString());
    }

    [Fact]
    public void A_reference_from_another_epoch_is_resolved_but_stale()
    {
        LuaRef reference = new();
        var identity = LuaRuntime.CurrentStateIdentity;
        reference.Rebind(17, new LuaStateIdentity(identity.AttachEpoch + 1, identity.StateGeneration));

        Assert.True(reference.IsResolved);
        Assert.False(reference.IsCurrent);
        Assert.False(reference.TryGetCurrent(out _));
    }

    [Fact]
    public void A_reference_from_another_state_generation_is_resolved_but_stale()
    {
        LuaRef reference = new();
        var identity = LuaRuntime.CurrentStateIdentity;
        reference.Rebind(17, new LuaStateIdentity(identity.AttachEpoch, identity.StateGeneration + 1));

        Assert.True(reference.IsResolved);
        Assert.Equal(identity.AttachEpoch, reference.Epoch);
        Assert.False(reference.IsCurrent);
        Assert.False(reference.TryGetCurrent(out _));
    }

    [Fact]
    public void A_reference_to_nil_is_a_valid_current_reference()
    {
        LuaRef reference = new();
        reference.Rebind(LuaApi.LUA_REFNIL, LuaRuntime.CurrentStateIdentity);

        Assert.True(reference.IsResolved);
        Assert.True(reference.IsCurrent);
    }

    [Fact]
    public void Releasing_a_stale_reference_marks_it_released_without_touching_lua()
    {
        LuaRef reference = new();
        var identity = LuaRuntime.CurrentStateIdentity;
        reference.Rebind(17, new LuaStateIdentity(identity.AttachEpoch + 1, identity.StateGeneration));

        reference.Release(default);
        reference.Release(default);

        Assert.False(reference.IsResolved);
        Assert.False(reference.IsCurrent);
    }

    [Fact]
    public void Releasing_a_current_reference_with_no_state_only_forgets_it()
    {
        LuaRef reference = new();
        reference.Rebind(17, LuaRuntime.CurrentStateIdentity);

        reference.Release(default);

        Assert.False(reference.IsResolved);
    }

    [Fact]
    public void Dispose_while_detached_forgets_the_reference()
    {
        LuaRuntime.Detach();
        LuaRef reference = new();
        reference.Rebind(17, LuaRuntime.CurrentStateIdentity);

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
