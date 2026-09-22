using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Inspection;

/// <summary>Fixture-backed contracts for the user-symbol registry and address-name Lua globals.</summary>
[Trait("Category", "NativeLua")]
public sealed class SymbolRegistryTests
{
	[Fact]
	public void TryGetName_forwards_only_the_source_mapped_address()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		EngineTest.Run(L, """
		                  getNameFromAddress = function(address, second)
		                    if address == 0x140001000 and second == nil then
		                      return 'one-argument-default'
		                    end
		                    return 42
		                  end
		                  """u8);

		int top = L.Top;
		LuaOperationStatus status = SymbolRegistry.TryGetName(0x140001000UL, out string? defaultName);

		Assert.Equal(LuaOperationStatusKind.Success, status.Kind);
		Assert.Equal("one-argument-default", defaultName);
		Assert.Equal(top, L.Top);
	}

	[Fact]
	public void Register_and_unregister_forward_typed_values_and_preserve_the_stack()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		EngineTest.Run(L, """
		                  registered = nil
		                  removed = nil
		                  registerSymbol = function(name, address, doNotSave)
		                    registered = { name = name, address = address, doNotSave = doNotSave }
		                  end
		                  unregisterSymbol = function(name) removed = name end
		                  """u8);

		int top = L.Top;
		SymbolName name = new("Player.Health");
		LuaOperationStatus registration = SymbolRegistry.Register(name, 0x140001234UL,
			new SymbolRegistrationOptions(true));

		Assert.Equal(LuaOperationStatusKind.Success, registration.Kind);
		Assert.Equal(top, L.Top);
		EngineTest.Run(L,
			"assert(registered.name == 'Player.Health' and registered.address == 0x140001234 and registered.doNotSave)"u8);
		Assert.Equal(top, L.Top);

		LuaOperationStatus release = SymbolRegistry.Unregister(name);

		Assert.Equal(LuaOperationStatusKind.Success, release.Kind);
		Assert.Equal(top, L.Top);
		EngineTest.Run(L, "assert(removed == 'Player.Health')"u8);
		Assert.Equal(top, L.Top);
	}

	[Fact]
	public void Registry_distinguishes_missing_globals_lua_failures_and_invalid_name_results()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		int top = L.Top;

		LuaOperationStatus status = SymbolRegistry.TryGetName(0x140001000UL, out string? unavailableName);

		Assert.Equal(LuaOperationStatusKind.GlobalUnavailable, status.Kind);
		Assert.Null(unavailableName);
		Assert.Equal(top, L.Top);

		EngineTest.Run(L, """
		                  getNameFromAddress = function() return nil end
		                  registerSymbol = function() error('registration rejected') end
		                  unregisterSymbol = function() error('removal rejected') end
		                  """u8);

		status = SymbolRegistry.TryGetName(0x140001000UL, out string? malformedName);

		Assert.Equal(LuaOperationStatusKind.NilResult, status.Kind);
		Assert.Null(malformedName);
		Assert.Equal(top, L.Top);

		SymbolName name = new("Player.Health");
		Assert.Equal(LuaOperationStatusKind.LuaFailure, SymbolRegistry.Register(name, 0x140001000UL).Kind);
		Assert.Equal(top, L.Top);
		Assert.Equal(LuaOperationStatusKind.LuaFailure, SymbolRegistry.Unregister(name).Kind);
		Assert.Equal(top, L.Top);
	}

	[Fact]
	public void Symbol_name_rejects_missing_text_and_keeps_ordinal_identity()
	{
		Assert.Throws<ArgumentException>(() => new SymbolName(""));
		Assert.Throws<ArgumentException>(() => new SymbolName(" \t"));

		SymbolName upper = new("Player.Health");
		SymbolName same = new("Player.Health");
		SymbolName differentCase = new("player.health");

		Assert.Equal(upper, same);
		Assert.NotEqual(upper, differentCase);
		Assert.Equal("Player.Health", upper.ToString());
	}

	[Fact]
	public void Owned_registration_lease_never_unregisters_a_newer_coordinated_registration()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		EngineTest.Run(L, """
		                  registrations = 0
		                  removals = 0
		                  registerSymbol = function(name, address, doNotSave) registrations = registrations + 1 end
		                  unregisterSymbol = function(name) removals = removals + 1 end
		                  """u8);
		SymbolName name = new("Player.Health");

		SymbolRegistrationAcquireOutcome first = SymbolRegistry.TryRegisterOwned(name, 0x140001000UL);
		SymbolRegistrationAcquireOutcome second = SymbolRegistry.TryRegisterOwned(name, 0x140002000UL);

		Assert.True(first.HasLease);
		Assert.True(second.HasLease);
		Assert.Equal(SymbolRegistrationReleaseKind.Superseded, first.Lease!.Release().Kind);
		Assert.Equal(SymbolRegistrationReleaseKind.AlreadyReleased, first.Lease.Release().Kind);
		Assert.Equal(SymbolRegistrationReleaseKind.Released, second.Lease!.Release().Kind);
		Assert.Equal(SymbolRegistrationReleaseKind.AlreadyReleased, second.Lease.Release().Kind);
		EngineTest.Run(L, "assert(registrations == 2 and removals == 1)"u8);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Symbol_mutations_that_raise_after_starting_supersede_tracked_leases()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		EngineTest.Run(L, """
		                  registerFailures = 0
		                  unregisterFailures = 0
		                  registerSymbol = function(name, address, doNotSave)
		                    if address == 0x140002000 then
		                      registerFailures = registerFailures + 1
		                      error('raised after registering')
		                    end
		                  end
		                  unregisterSymbol = function(name)
		                    unregisterFailures = unregisterFailures + 1
		                    error('raised after unregistering')
		                  end
		                  """u8);
		SymbolName name = new("Player.Health");

		SymbolRegistrationAcquireOutcome first = SymbolRegistry.TryRegisterOwned(name, 0x140001000UL);
		SymbolRegistrationAcquireOutcome failedOwned = SymbolRegistry.TryRegisterOwned(name, 0x140002000UL);

		Assert.True(first.HasLease);
		Assert.False(failedOwned.HasLease);
		Assert.Equal(LuaOperationStatusKind.LuaFailure, failedOwned.Status.Kind);
		Assert.Equal(SymbolRegistrationReleaseKind.Superseded, first.Lease!.Release().Kind);

		SymbolRegistrationAcquireOutcome second = SymbolRegistry.TryRegisterOwned(name, 0x140001000UL);
		LuaOperationStatus failedDirectRegistration = SymbolRegistry.Register(name, 0x140002000UL);

		Assert.True(second.HasLease);
		Assert.Equal(LuaOperationStatusKind.LuaFailure, failedDirectRegistration.Kind);
		Assert.Equal(SymbolRegistrationReleaseKind.Superseded, second.Lease!.Release().Kind);

		SymbolRegistrationAcquireOutcome third = SymbolRegistry.TryRegisterOwned(name, 0x140001000UL);
		LuaOperationStatus failedDirectUnregistration = SymbolRegistry.Unregister(name);

		Assert.True(third.HasLease);
		Assert.Equal(LuaOperationStatusKind.LuaFailure, failedDirectUnregistration.Kind);
		Assert.Equal(SymbolRegistrationReleaseKind.Superseded, third.Lease!.Release().Kind);
		EngineTest.Run(L, "assert(registerFailures == 2 and unregisterFailures == 1)"u8);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Owned_registration_becomes_stale_without_unregistering_after_runtime_detaches()
	{
		EngineTest.RequireNativeLua();
		SymbolRegistrationLease lease;
		using (NativeLuaState state = new())
		using (HostScope scope = new(state))
		{
			LuaState L = scope.State;
			EngineTest.Run(L, """
			                  registerSymbol = function(name, address, doNotSave) end
			                  unregisterSymbol = function(name) error('must not run after detach') end
			                  """u8);
			SymbolRegistrationAcquireOutcome acquired =
				SymbolRegistry.TryRegisterOwned(new SymbolName("Player.Health"), 0x140001000UL);
			Assert.True(acquired.HasLease);
			lease = acquired.Lease!;
		}

		SymbolRegistrationReleaseOutcome release = lease.Release();

		Assert.Equal(SymbolRegistrationReleaseKind.StaleRuntime, release.Kind);
		Assert.True(lease.IsTerminal);
	}

	[Fact]
	public void Owned_registration_reports_failed_acquisition_without_creating_a_lease()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);

		SymbolRegistrationAcquireOutcome outcome =
			SymbolRegistry.TryRegisterOwned(new SymbolName("Player.Health"), 0x140001000UL);

		Assert.False(outcome.HasLease);
		Assert.Null(outcome.Lease);
		Assert.Equal(LuaOperationStatusKind.GlobalUnavailable, outcome.Status.Kind);
		Assert.Equal(0, scope.State.Top);
	}

	[Fact]
	public void Owned_registration_cleanup_can_retry_unavailability_and_dispose_only_once()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		EngineTest.Run(L, "registerSymbol = function(name, address, doNotSave) end"u8);

		SymbolRegistrationAcquireOutcome retryable = SymbolRegistry.TryRegisterOwned(new SymbolName("Player.Health"),
			0x140001000UL,
			new SymbolRegistrationOptions(true));

		Assert.True(retryable.HasLease);
		Assert.True(retryable.Lease!.Options.DoNotSave);
		SymbolRegistrationReleaseOutcome unavailable = retryable.Lease.Release();
		Assert.Equal(SymbolRegistrationReleaseKind.CleanupUnavailable, unavailable.Kind);
		Assert.False(unavailable.IsTerminal);
		Assert.False(retryable.Lease.IsTerminal);

		EngineTest.Run(L, "unregisterSymbol = function(name) end"u8);
		SymbolRegistrationReleaseOutcome released = retryable.Lease.Release();
		Assert.Equal(SymbolRegistrationReleaseKind.Released, released.Kind);
		Assert.True(released.IsTerminal);
		retryable.Lease.Dispose();
		Assert.Equal(SymbolRegistrationReleaseKind.AlreadyReleased, retryable.Lease.Release().Kind);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Owned_registration_cleanup_after_a_lua_failure_is_indeterminate_and_terminal()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;

		EngineTest.Run(L,
			"registerSymbol = function(name, address, doNotSave) end; unregisterSymbol = function(name) error('cleanup started then failed') end"u8);
		SymbolRegistrationAcquireOutcome failed =
			SymbolRegistry.TryRegisterOwned(new SymbolName("Player.Mana"), 0x140002000UL);
		SymbolRegistrationReleaseOutcome indeterminate = failed.Lease!.Release();

		Assert.Equal(SymbolRegistrationReleaseKind.CleanupIndeterminate, indeterminate.Kind);
		Assert.True(indeterminate.IsTerminal);
		Assert.False(indeterminate.Status.IsSuccess);
		Assert.True(failed.Lease.IsTerminal);
		Assert.Equal(SymbolRegistrationReleaseKind.AlreadyReleased, failed.Lease.Release().Kind);
		Assert.Equal(0, L.Top);
	}
}
