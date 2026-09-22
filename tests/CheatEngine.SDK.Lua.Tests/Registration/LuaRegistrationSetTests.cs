using CheatEngine.SDK.Lua.Registration;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Lua.Tests.Callbacks;
using CheatEngine.SDK.Lua.Tests.Support;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Lua.Tests.Registration;

/// <summary>Native-Lua contract tests for ownership-aware generated-global registration leases.</summary>
[Trait("Category", "NativeLua")]
public sealed class LuaRegistrationSetTests
{
	[Fact]
	public void Default_outcomes_expose_an_empty_failure_list()
	{
		LuaRegistrationReleaseOutcome release = default;
		LuaRegistrationResult registration = default;

		Assert.Empty(release.Failures);
		Assert.Empty(registration.Rollback.Failures);
	}

	[Fact]
	[Trait("Qualification", "Q16")]
	public void Reject_existing_preflights_without_replacing_the_effective_global()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, "sdk009_collision = 42"u8);

		LuaRegistrationResult result = LuaRegistrationSet.Register(L, [Entry("sdk009_collision")]);

		Assert.Equal(LuaRegistrationResultKind.Collision, result.Kind);
		Assert.Equal("sdk009_collision", result.Failure?.Name);
		Assert.Null(result.Lease);
		Assert.Equal(42, ReadInteger(L, "return sdk009_collision"u8));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q16")]
	public void Replace_existing_restores_the_prior_value_only_while_the_lease_still_owns_the_global()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, "sdk009_replace = 42"u8);

		LuaRegistrationResult result = LuaRegistrationSet.Register(L, [Entry("sdk009_replace")],
			LuaRegistrationCollisionPolicy.ReplaceExisting);
		LuaRegistrationLease lease = Assert.IsType<LuaRegistrationLease>(result.Lease);
		Assert.True(result.IsSuccess);

		Assert.Equal(3, ReadInteger(L, "return sdk009_replace(1, 2)"u8));
		LuaRegistrationReleaseOutcome released = lease.ReleaseWithOutcome(L);
		Assert.Equal(LuaRegistrationReleaseKind.Released, released.Kind);
		Assert.Equal(1, released.RestoredCount);
		Assert.Equal(42, ReadInteger(L, "return sdk009_replace"u8));
		Assert.Equal(LuaRegistrationReleaseKind.AlreadyReleased, lease.ReleaseWithOutcome(L).Kind);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q16")]
	public void Release_preserves_a_later_replacement_and_reports_it_without_writing()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);

		LuaRegistrationResult result = LuaRegistrationSet.Register(L, [Entry("sdk009_replaced")]);
		LuaRegistrationLease lease = Assert.IsType<LuaRegistrationLease>(result.Lease);
		LuaTest.Run(L, "sdk009_replaced = function() return 99 end"u8);

		LuaRegistrationReleaseOutcome released = lease.ReleaseWithOutcome(L);

		Assert.Equal(LuaRegistrationReleaseKind.Released, released.Kind);
		Assert.Equal(1, released.ReplacementCount);
		Assert.Equal(99, ReadInteger(L, "return sdk009_replaced()"u8));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Publication_failure_rolls_back_later_entries_when_an_earlier_cleanup_fails()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, """
		               local values = {}
		               setmetatable(_G, {
		                 __index = function(_, key) return values[key] end,
		                 __newindex = function(_, key, value)
		                   if key == 'sdk009_third' and value ~= nil then error('third assignment rejected') end
		                   if key == 'sdk009_first' and value == nil then error('first cleanup rejected') end
		                   values[key] = value
		                 end,
		               })
		               """u8);

		LuaRegistrationResult result = LuaRegistrationSet.Register(L,
			[Entry("sdk009_first"), Entry("sdk009_second"), Entry("sdk009_third")]);

		Assert.Equal(LuaRegistrationResultKind.PublicationFailed, result.Kind);
		Assert.Equal("sdk009_third", result.Failure?.Name);
		Assert.Equal(LuaRegistrationReleaseKind.PartiallyReleased, result.Rollback.Kind);
		Assert.Equal(1, result.Rollback.RemovedCount);
		LuaRegistrationReleaseFailure failure = Assert.Single(result.Rollback.Failures);
		Assert.Equal("sdk009_first", failure.Name);
		Assert.NotNull(result.Lease);
		Assert.Equal("function", ReadString(L, "return type(sdk009_first)"u8));
		Assert.Equal("nil", ReadString(L, "return type(sdk009_second)"u8));
		result.Lease.Dispose();
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Release_after_state_reset_is_stale_and_does_not_clear_a_new_value()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaRegistrationResult result = LuaRegistrationSet.Register(L, [Entry("sdk009_stale")]);
		LuaRegistrationLease lease = Assert.IsType<LuaRegistrationLease>(result.Lease);

		using (LuaRuntime.BeginStateReset())
		{
		}

		LuaTest.Run(L, "sdk009_stale = function() return 77 end"u8);
		LuaRegistrationReleaseOutcome released = lease.ReleaseWithOutcome(L);

		Assert.Equal(LuaRegistrationReleaseKind.Stale, released.Kind);
		Assert.Equal(1, released.RemainingCount);
		Assert.Equal(77, ReadInteger(L, "return sdk009_stale()"u8));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Release_reports_an_unconfirmed_entry_when_its_protected_cleanup_fails()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		LuaState L = LuaTest.View(state);
		using RuntimeScope scope = new(state);
		LuaTest.Run(L, """
		               local values = {}
		               setmetatable(_G, {
		                 __index = function(_, key) return values[key] end,
		                 __newindex = function(_, key, value)
		                   if key == 'sdk009_release_failure' and value == nil then error('cleanup rejected') end
		                   values[key] = value
		                 end,
		               })
		               """u8);
		LuaRegistrationResult result = LuaRegistrationSet.Register(L, [Entry("sdk009_release_failure")]);
		LuaRegistrationLease lease = Assert.IsType<LuaRegistrationLease>(result.Lease);

		LuaRegistrationReleaseOutcome released = lease.ReleaseWithOutcome(L);

		Assert.Equal(LuaRegistrationReleaseKind.PartiallyReleased, released.Kind);
		Assert.Equal(1, released.RemainingCount);
		Assert.Equal("sdk009_release_failure", Assert.Single(released.Failures).Name);
		Assert.Equal("function", ReadString(L, "return type(sdk009_release_failure)"u8));
		Assert.Equal(0, L.Top);
	}

	private static LuaRegistrationEntry Entry(string name)
	{
		return new LuaRegistrationEntry(name, Thunks.Add);
	}

	private static long ReadInteger(LuaState state, ReadOnlySpan<byte> source)
	{
		using LuaFrame frame = new(state);
		LuaTest.Run(state, source, 1);
		Assert.True(state.TryReadInteger(-1, out long value));
		return value;
	}

	private static string ReadString(LuaState state, ReadOnlySpan<byte> source)
	{
		using LuaFrame frame = new(state);
		LuaTest.Run(state, source, 1);
		return LuaTest.ReadString(state, -1);
	}
}
