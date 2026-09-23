using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Objects;

/// <summary>
///     Ownership: explicit destroy through the protected call, exactly once, the transfer API, and the origin policy that
///     refuses to destroy in another Lua universe.
/// </summary>
public sealed class OwnedTests
{
	[Fact]
	public void A_null_handle_cannot_be_owned()
	{
		ArgumentException exception = Assert.Throws<ArgumentException>(() => new Owned<CEObject>(CEObject.Null));
		Assert.Equal("value", exception.ParamName);
	}

	[Fact]
	public void Construction_is_internal_so_a_consumer_cannot_wrap_an_arbitrary_borrowed_handle()
	{
		Assert.Empty(typeof(Owned<CEObject>).GetConstructors());
	}

	[Fact]
	public void Transfer_moves_the_owner_and_abandon_returns_only_a_borrowed_handle()
	{
		CEObject handle = new(0x1234);
		Owned<CEObject> source = new(handle);

		Assert.False(source.IsDisposed);
		Assert.Equal(handle, source.Value);
		Assert.Equal(handle, source.Handle);
		Assert.Equal(handle, source.ToBorrowed());
		Assert.Equal("Owned(CEObject@0x1234)", source.ToString());

		Owned<CEObject> destination = source.Transfer();
		Assert.True(source.IsDisposed);
		Assert.Equal("Owned(disposed)", source.ToString());
		Assert.Throws<ObjectDisposedException>(() => source.Value);
		Assert.Throws<ObjectDisposedException>(() => source.Handle);
		Assert.Throws<ObjectDisposedException>(() => source.ToBorrowed());
		Assert.Throws<ObjectDisposedException>(() => source.Transfer());

		Assert.False(destination.IsDisposed);
		Assert.Equal(handle, destination.Abandon());
		Assert.True(destination.IsDisposed);
		Assert.Throws<ObjectDisposedException>(() => destination.Abandon());
	}

	[Fact]
	public void Dispose_while_detached_throws_and_retains_the_owner()
	{
		LuaRuntime.Detach();
		Owned<CEObject> owned = new(new CEObject(0x1234));

		Assert.Throws<InvalidOperationException>(owned.Dispose);

		Assert.False(owned.IsDisposed);
		Assert.Equal(new CEObject(0x1234), owned.Value);
		Assert.Equal(new CEObject(0x1234), owned.Abandon());
	}

	[Fact]
	public void TryDestroy_while_detached_throws_before_touching_the_state_and_retains_the_owner()
	{
		LuaRuntime.Detach();
		Owned<CEObject> owned = new(new CEObject(0x1234));

		// The null state view is never dereferenced: the runtime admission reports detached state first.
		Assert.Throws<InvalidOperationException>(() => owned.TryDestroy(default));

		Assert.False(owned.IsDisposed);
		Assert.Equal(new CEObject(0x1234), owned.Abandon());
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	public void A_detached_owner_is_refused_after_reattach_and_consumed_without_destroy()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		CEObject handle = FakeHost.CreateObject(L, "Probe");
		Owned<CEObject> owned = new(handle);
		EngineResourceOrigin origin = owned.Origin;

		LuaRuntime.Detach();
		Assert.Throws<InvalidOperationException>(owned.Dispose);
		Assert.False(owned.IsDisposed);

		LuaRuntime.Attach(scope.Binding);
		Assert.False(owned.Origin.IsCurrentRuntime);
		owned.Dispose();

		Assert.True(owned.IsDisposed);
		Assert.Equal(origin, owned.Origin);
		Assert.Equal(TargetReleaseStatus.RefusedRuntimeChanged, owned.LastReleaseOutcome.Status);
		Assert.True(owned.LastReleaseOutcome.RequiresManualRecovery);
		Assert.False(FakeHost.IsDestroyed(L, handle));
		Assert.Equal(0, FakeHost.DestroyedCount(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	[Trait("Qualification", "Q17")]
	public void Dispose_after_a_controlled_state_replacement_consumes_the_owner_without_destroy()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		CEObject handle = FakeHost.CreateObject(L, "Probe");
		Owned<CEObject> owned = new(handle);
		LuaStateIdentity created = LuaRuntime.CurrentStateIdentity;

		FakeHost.ReplaceStateGeneration();

		Assert.Equal(created.AttachEpoch, LuaRuntime.CurrentStateIdentity.AttachEpoch);
		Assert.Equal(created.StateGeneration + 1, LuaRuntime.CurrentStateIdentity.StateGeneration);
		Assert.False(owned.Origin.IsCurrentRuntime);
		owned.Dispose();

		Assert.True(owned.IsDisposed);
		Assert.Equal(created, owned.Origin.Runtime);
		Assert.Equal(TargetReleaseStatus.RefusedRuntimeChanged, owned.LastReleaseOutcome.Status);
		Assert.False(FakeHost.IsDestroyed(L, handle));
		Assert.Equal(0, FakeHost.DestroyedCount(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	public void TryDestroy_after_a_runtime_identity_change_throws_and_consumes_the_owner_without_destroy()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		CEObject handle = FakeHost.CreateObject(L, "Probe");
		Owned<CEObject> owned = new(handle);

		FakeHost.ReplaceStateGeneration();
		InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => owned.TryDestroy(L));

		Assert.Contains("previous Lua runtime identity", exception.Message, StringComparison.Ordinal);
		Assert.True(owned.IsDisposed);
		Assert.Equal(TargetReleaseStatus.RefusedRuntimeChanged, owned.LastReleaseOutcome.Status);
		Assert.False(FakeHost.IsDestroyed(L, handle));
		Assert.Equal(0, L.Top);
		Assert.True(owned.TryDestroy(L).IsOk);
		Assert.Equal(0, FakeHost.DestroyedCount(L));
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	public void ReleaseWithOutcome_reports_released_after_exactly_one_destroy()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		CEObject handle = FakeHost.CreateObject(L, "Probe");
		Owned<CEObject> owned = new(handle);

		TargetReleaseOutcome outcome = owned.ReleaseWithOutcome();

		Assert.Equal(TargetReleaseStatus.Released, outcome.Status);
		Assert.False(outcome.RequiresManualRecovery);
		Assert.Equal(outcome, owned.LastReleaseOutcome);
		Assert.True(owned.IsDisposed);
		Assert.True(FakeHost.IsDestroyed(L, handle));
		Assert.Equal(outcome, owned.ReleaseWithOutcome());
		owned.Dispose();
		Assert.Equal(1, FakeHost.DestroyedCount(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	public void ReleaseWithOutcome_when_destroy_raises_reports_unconfirmed_and_never_retries()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		CEObject handle = FakeHost.CreateObject(L, "Probe");
		EngineTest.Run(L, "destroy_attempts = 0"u8);
		FakeHost.RunOnObject(L, handle, """
		                                o.getters.destroy = function()
		                                  return function() destroy_attempts = destroy_attempts + 1 error("refuses to be destroyed") end
		                                end
		                                """);
		Owned<CEObject> owned = new(handle);

		TargetReleaseOutcome outcome = owned.ReleaseWithOutcome();
		TargetReleaseOutcome again = owned.ReleaseWithOutcome();
		owned.Dispose();

		Assert.Equal(TargetReleaseStatus.UnconfirmedAfterInvocation, outcome.Status);
		Assert.Equal(EngineFailureKind.ProtectedLuaFailure, outcome.FailureKind);
		Assert.Equal(outcome, again);
		Assert.True(owned.IsDisposed);
		EngineTest.Run(L, "assert(destroy_attempts == 1)"u8);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	public void ReleaseWithOutcome_while_detached_reports_not_invoked_and_consumes_the_owner()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		CEObject handle = FakeHost.CreateObject(L, "Probe");
		Owned<CEObject> owned = new(handle);

		LuaRuntime.Detach();
		TargetReleaseOutcome outcome = owned.ReleaseWithOutcome();

		Assert.Equal(TargetReleaseStatus.NotInvoked, outcome.Status);
		Assert.Equal(EngineFailureKind.BindingFailure, outcome.FailureKind);
		Assert.True(owned.IsDisposed);
		Assert.False(FakeHost.IsDestroyed(L, handle));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	public void ReleaseWithOutcome_with_a_binding_that_has_no_pusher_reports_not_invoked()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state, false);
		LuaState L = scope.State;
		CEObject handle = FakeHost.CreateObject(L, "Probe");
		Owned<CEObject> owned = new(handle);

		TargetReleaseOutcome outcome = owned.ReleaseWithOutcome();

		Assert.Equal(TargetReleaseStatus.NotInvoked, outcome.Status);
		Assert.Equal(EngineFailureKind.BindingFailure, outcome.FailureKind);
		Assert.True(owned.IsDisposed);
		Assert.False(FakeHost.IsDestroyed(L, handle));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	public void Transfer_preserves_the_origin_of_the_source_owner()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		CEObject handle = FakeHost.CreateObject(L, "Probe");
		Owned<CEObject> source = new(handle);
		EngineResourceOrigin origin = source.Origin;

		FakeHost.ReplaceStateGeneration();
		Owned<CEObject> destination = source.Transfer();
		destination.Dispose();

		Assert.Equal(origin, destination.Origin);
		Assert.NotEqual(LuaRuntime.CurrentStateIdentity, destination.Origin.Runtime);
		Assert.Equal(TargetReleaseStatus.RefusedRuntimeChanged, destination.LastReleaseOutcome.Status);
		Assert.False(FakeHost.IsDestroyed(L, handle));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	public void Owned_child_destroyed_by_its_parent_before_dispose_reports_unconfirmed_and_never_retries()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		CEObject child = FakeHost.CreateObject(L, "Probe");
		Owned<CEObject> owned = new(child);
		EngineTest.Run(L, "destroy_attempts = 0"u8);
		// The parent destroys its child behind the owner's back; the child's destroy then fails as CE's does.
		FakeHost.RunOnObject(L, child, """
		                               o.destroyed = true
		                               o.getters.destroy = function()
		                                 return function() destroy_attempts = destroy_attempts + 1 error("object already destroyed") end
		                               end
		                               """);

		owned.Dispose();
		owned.Dispose();

		Assert.True(owned.IsDisposed);
		Assert.Equal(TargetReleaseStatus.UnconfirmedAfterInvocation, owned.LastReleaseOutcome.Status);
		Assert.Equal(EngineFailureKind.ProtectedLuaFailure, owned.LastReleaseOutcome.FailureKind);
		EngineTest.Run(L, "assert(destroy_attempts == 1)"u8);
		Assert.Equal(0, FakeHost.DestroyedCount(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	public void An_owner_captures_the_runtime_identity_that_created_it()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		using Owned<CEObject> owned = new(FakeHost.CreateObject(scope.State, "Probe"));

		Assert.Equal(LuaRuntime.CurrentStateIdentity, owned.Origin.Runtime);
		Assert.Null(owned.Origin.Target);
		Assert.False(owned.Origin.IsTargetBound);
		Assert.True(owned.Origin.IsCurrentRuntime);
		Assert.Equal(TargetReleaseStatus.Unspecified, owned.LastReleaseOutcome.Status);
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	public void Dispose_with_a_binding_that_has_no_pusher_throws_and_retains_the_owner()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state, false);
		LuaState L = scope.State;
		CEObject handle = FakeHost.CreateObject(L, "Probe");
		Owned<CEObject> owned = new(handle);

		Assert.Throws<InvalidOperationException>(owned.Dispose);

		Assert.False(owned.IsDisposed);
		Assert.False(FakeHost.IsDestroyed(L, handle));
		Assert.Equal(0, FakeHost.DestroyedCount(L));
		Assert.Equal(0, L.Top);
		Assert.Equal(handle, owned.Abandon());
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	public void TryDestroy_with_a_binding_that_has_no_pusher_throws_and_retains_the_owner()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state, false);
		LuaState L = scope.State;
		CEObject handle = FakeHost.CreateObject(L, "Probe");
		Owned<CEObject> owned = new(handle);

		Assert.Throws<InvalidOperationException>(() => owned.TryDestroy(L));

		Assert.False(owned.IsDisposed);
		Assert.False(FakeHost.IsDestroyed(L, handle));
		Assert.Equal(0, L.Top);
		Assert.Throws<InvalidOperationException>(() => owned.TryDestroy(L));
		Assert.Equal(0, L.Top);
		Assert.Equal(handle, owned.Abandon());
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	public void Dispose_destroys_the_object_exactly_once()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		CEObject handle = FakeHost.CreateObject(L, "Probe");
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
		LuaState L = scope.State;
		CEObject handle = FakeHost.CreateObject(L, "Probe", "o.props.Count = 4");

		using (Owned<CEObject> owned = new(handle))
		{
			Assert.True(owned.Value.TryGetProperty<Int32Marshaller, int>("Count"u8, out int count));
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
		LuaState L = scope.State;
		using LuaFrame frame = new(L);
		Owned<CEObject> stubborn = new(FakeHost.CreateObject(L, "Stubborn"));

		LuaStatus status = stubborn.TryDestroy(L);

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
		LuaState L = scope.State;
		CEObject handle = FakeHost.CreateObject(L, "Probe");
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
		LuaState L = scope.State;
		Owned<CEObject> stubborn = new(FakeHost.CreateObject(L, "Stubborn"));

		stubborn.Dispose();

		Assert.True(stubborn.IsDisposed);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	public void Transfer_moves_ownership_so_only_the_destination_owner_destroys()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		CEObject handle = FakeHost.CreateObject(L, "Probe");
		Owned<CEObject> first = new(handle);

		Owned<CEObject> second = first.Transfer();
		first.Dispose();
		Assert.True(first.IsDisposed);
		Assert.False(FakeHost.IsDestroyed(L, handle));
		Assert.Equal(0, FakeHost.DestroyedCount(L));

		using (second)
		{
			Assert.Equal(handle, second.Handle);
		}

		Assert.True(FakeHost.IsDestroyed(L, handle));
		Assert.Equal(1, FakeHost.DestroyedCount(L));
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	public void Dispose_after_the_plugin_is_disabled_throws_and_retains_the_owner_for_explicit_abandonment()
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
		Assert.Throws<InvalidOperationException>(owned.Dispose);

		Assert.False(owned.IsDisposed);
		Assert.False(FakeHost.IsDestroyed(L, handle));
		Assert.Equal(0, FakeHost.DestroyedCount(L));
		Assert.Equal(0, L.Top);
		Assert.Equal(handle, owned.Abandon());
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	public void A_borrowed_view_stays_usable_while_the_owner_lives()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using Owned<CEObject> owned = new(FakeHost.CreateObject(L, "Probe", "o.props.Count = 8"));

		CEObject borrowed = owned.ToBorrowed();
		Assert.True(borrowed.TryGetProperty<Int32Marshaller, int>("Count"u8, out int count));
		Assert.Equal(8, count);
		Assert.True(borrowed.TryCallMethod<Int32Marshaller, int>("getCount"u8, out count));
		Assert.Equal(8, count);
	}
}
