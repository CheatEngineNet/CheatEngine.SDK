using CheatEngine.SDK.Lua.Interop.Api;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Lua.Tests.Support;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Lua.Tests.References;

/// <summary>Registry references against a real state: lifecycle, and invalidation when the host epoch advances.</summary>
[Trait("Category", "NativeLua")]
public sealed class LuaRefTests
{
	[Fact]
	public void Create_push_and_release_round_trip()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		L.PushString("kept alive"u8);

		LuaRef reference = L.CreateRef();

		Assert.Equal(0, L.Top);
		Assert.True(reference.IsResolved);
		Assert.True(reference.IsCurrent);
		Assert.Equal(LuaRuntime.Epoch, reference.Epoch);
		Assert.True(reference.Reference > 0); // Slots belong to the private table, not the host registry.

		Assert.True(L.TryPushRef(reference));
		Assert.Equal("kept alive", LuaTest.ReadString(L, -1));
		L.Pop(1);

		reference.Release(L);
		Assert.False(reference.IsResolved);
		Assert.False(L.TryPushRef(reference));
		Assert.Equal(0, L.Top);

		// The slot went back to the registry's free list: the next reference reuses it.
		L.PushInteger(1);
		LuaRef next = L.CreateRef();
		Assert.Equal("LuaRef(unresolved)", reference.ToString());
		Assert.True(next.IsCurrent);
		next.Release(L);
	}

	[Fact]
	public void Release_is_idempotent_and_does_not_free_the_slot_twice()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		L.PushInteger(1);
		LuaRef first = L.CreateRef();
		int slot = first.Reference;

		first.Release(L);
		first.Release(L);

		// Two distinct new references must get two distinct slots; a doubly freed slot would be handed out twice.
		L.PushInteger(2);
		LuaRef a = L.CreateRef();
		L.PushInteger(3);
		LuaRef b = L.CreateRef();
		Assert.Equal(slot, a.Reference);
		Assert.NotEqual(a.Reference, b.Reference);
		a.Release(L);
		b.Release(L);
	}

	[Fact]
	public void A_reference_to_nil_pushes_nil()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		L.PushNil();

		LuaRef reference = L.CreateRef();

		Assert.Equal(LuaApi.LUA_REFNIL, reference.Reference);
		Assert.True(L.TryPushRef(reference));
		Assert.True(L.IsNil(-1));
		reference.Release(L);
	}

	[Fact]
	public void References_are_invalidated_by_detach_and_reattach()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		LuaRef reference;
		int epochBefore;
		using (new RuntimeScope(state))
		{
			epochBefore = LuaRuntime.Epoch;
			L.PushString("epoch value"u8);
			reference = L.CreateRef();
			Assert.True(reference.IsCurrent);
			Assert.Equal(epochBefore, reference.Epoch);
		}

		// Detached: the epoch is unchanged, the reference still counts as current.
		Assert.True(reference.IsCurrent);

		using (new RuntimeScope(state))
		{
			Assert.Equal(epochBefore + 1, LuaRuntime.Epoch);
			Assert.True(reference.IsResolved);
			Assert.False(reference.IsCurrent);
			Assert.False(L.TryPushRef(reference));
			Assert.Equal(0, L.Top);

			// Releasing a stale reference never touches the (possibly foreign) registry slot; it is only forgotten.
			reference.Release(L);
			Assert.False(reference.IsResolved);
		}
	}

	[Fact]
	public void A_reference_from_the_pre_reset_state_never_releases_a_current_generation_slot()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		L.PushString("old generation"u8);
		LuaRef stale = L.CreateRef();
		LuaStateIdentity before = LuaRuntime.CurrentStateIdentity;

		using (LuaRuntime.BeginStateReset())
		{
		}

		LuaStateIdentity after = LuaRuntime.CurrentStateIdentity;
		Assert.Equal(before.AttachEpoch, after.AttachEpoch);
		Assert.Equal(before.StateGeneration + 1, after.StateGeneration);
		Assert.True(stale.IsResolved);
		Assert.False(stale.IsCurrent);
		Assert.False(L.TryPushRef(stale));
		Assert.Equal(0, L.Top);

		L.PushString("current generation"u8);
		LuaRef current = L.CreateRef();
		Assert.True(current.IsCurrent);

		// Model a registry-slot number reused by the replacement state. This stale binding must not be unref'd merely
		// because it shares the current attachment epoch: state generation is the distinguishing component.
		LuaRef collidingStale = new();
		collidingStale.Rebind(current.Reference, before);
		collidingStale.Release(L);

		Assert.False(collidingStale.IsResolved);
		stale.Release(L);

		Assert.False(stale.IsResolved);
		Assert.True(L.TryPushRef(current));
		Assert.Equal("current generation", LuaTest.ReadString(L, -1));
		L.Pop(1);
		Assert.Equal(0, L.Top);
		current.Release(L);
	}

	[Fact]
	public void Dispose_releases_through_the_attached_runtime()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		L.PushInteger(7);
		LuaRef reference = L.CreateRef();
		int slot = reference.Reference;

		reference.Dispose();

		Assert.False(reference.IsResolved);
		L.PushInteger(8);
		LuaRef reused = L.CreateRef();
		Assert.Equal(slot, reused.Reference);
		reused.Dispose();
	}

	[Fact]
	public void A_stale_reference_is_not_released_by_dispose_after_reattach()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		LuaState L = LuaTest.View(state);
		LuaRef stale;
		using (new RuntimeScope(state))
		{
			L.PushInteger(1);
			stale = L.CreateRef();
		}

		using (new RuntimeScope(state))
		{
			// The same slot is still occupied in this state (nothing freed it); a fresh reference must not receive it.
			int staleSlot = stale.Reference;
			stale.Dispose();
			L.PushInteger(2);
			LuaRef fresh = L.CreateRef();
			Assert.NotEqual(staleSlot, fresh.Reference);
			fresh.Release(L);
		}
	}
}
