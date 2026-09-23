using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Inspection;

/// <summary>Fixture-backed contracts for the user-symbol registry and address-name Lua globals.</summary>
/// <remarks>
///     A lease verifies its name with <c>getAddressSafe</c> before it unregisters, so the fixtures that release a lease
///     keep a <c>registered_symbols</c> table that <c>registerSymbol</c> fills and <c>getAddressSafe</c> reads.
/// </remarks>
[Trait("Category", "NativeLua")]
public sealed class SymbolRegistryTests
{
	/// <summary>A symbol handler double: registration, removal and name lookup share one table.</summary>
	internal static ReadOnlySpan<byte> SymbolHandler => """
	                                                    registered_symbols = {}
	                                                    registrations = 0
	                                                    removals = 0
	                                                    registerSymbol = function(name, address, doNotSave)
	                                                      registrations = registrations + 1
	                                                      registered_symbols[name] = address
	                                                    end
	                                                    unregisterSymbol = function(name)
	                                                      removals = removals + 1
	                                                      registered_symbols[name] = nil
	                                                    end
	                                                    getAddressSafe = function(name, isLocal, shallow)
	                                                      return registered_symbols[name]
	                                                    end
	                                                    """u8;

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
		EngineTest.Run(L, SymbolHandler);
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
	public void Release_outcomes_that_made_no_host_call_never_report_a_status_that_reads_as_success()
	{
		// A08-26: Superseded, AlreadyReleased and StaleRuntime never send a CE call, so Status must be null rather
		// than default(LuaOperationStatus): a numeric default that happened to equal LuaOperationStatusKind.Success
		// would otherwise make IsSuccess read true for a release that never touched Cheat Engine.
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		EngineTest.Run(L, SymbolHandler);
		SymbolName name = new("Player.Health");

		SymbolRegistrationAcquireOutcome first = SymbolRegistry.TryRegisterOwned(name, 0x140001000UL);
		SymbolRegistrationAcquireOutcome second = SymbolRegistry.TryRegisterOwned(name, 0x140002000UL);
		SymbolRegistrationReleaseOutcome superseded = first.Lease!.Release();
		SymbolRegistrationReleaseOutcome alreadyReleased = first.Lease.Release();

		Assert.Equal(SymbolRegistrationReleaseKind.Superseded, superseded.Kind);
		Assert.Null(superseded.Status);
		Assert.Equal(SymbolRegistrationReleaseKind.AlreadyReleased, alreadyReleased.Kind);
		Assert.Null(alreadyReleased.Status);
		_ = second.Lease!.Release();
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Owned_registration_factory_failure_compensates_once_and_preserves_the_primary_cause()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		EngineTest.Run(L, SymbolHandler);
		InvalidOperationException cause = new("injected lease factory failure");

		SymbolRegistrationHandoffException exception = Assert.Throws<SymbolRegistrationHandoffException>(() =>
			SymbolRegistry.TryRegisterOwnedCore(new SymbolName("Player.Health"), 0x140001000UL, default,
				(_, _, _, _) => throw cause,
				static (_, _) => throw new InvalidOperationException("The publisher must not run.")));

		Assert.Same(cause, exception.InnerException);
		Assert.Equal(SymbolRegistrationReleaseKind.Released, exception.CleanupOutcome.Kind);
		Assert.True(exception.CleanupOutcome.Status!.Value.IsSuccess);
		EngineTest.Run(L, "assert(registrations == 1 and removals == 1)"u8);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Owned_registration_publish_failure_compensates_once_and_preserves_the_primary_cause()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		EngineTest.Run(L, SymbolHandler);
		InvalidOperationException cause = new("injected lease publication failure");

		SymbolRegistrationHandoffException exception = Assert.Throws<SymbolRegistrationHandoffException>(() =>
			SymbolRegistry.TryRegisterOwnedCore(new SymbolName("Player.Health"), 0x140001000UL, default,
				static (name, address, options, identity) =>
					new SymbolRegistrationLease(name, address, options, identity),
				(_, _) => throw cause));

		Assert.Same(cause, exception.InnerException);
		Assert.Equal(SymbolRegistrationReleaseKind.Released, exception.CleanupOutcome.Kind);
		Assert.True(exception.CleanupOutcome.Status!.Value.IsSuccess);
		EngineTest.Run(L, "assert(registrations == 1 and removals == 1)"u8);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Owned_registration_publication_failure_after_detach_preserves_the_primary_cause_without_unregistering()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		EngineTest.Run(L, SymbolHandler);
		InvalidOperationException cause = new("injected lease publication failure after detach");

		SymbolRegistrationHandoffException exception;
		try
		{
			exception = Assert.Throws<SymbolRegistrationHandoffException>(() =>
				SymbolRegistry.TryRegisterOwnedCore(new SymbolName("Player.Health"), 0x140001000UL, default,
					static (name, address, options, identity) =>
						new SymbolRegistrationLease(name, address, options, identity),
					(_, _) =>
					{
						LuaRuntime.Detach();
						throw cause;
					}));
		}
		finally
		{
			LuaRuntime.Attach(scope.Binding);
		}

		Assert.Same(cause, exception.InnerException);
		Assert.Equal(SymbolRegistrationReleaseKind.StaleRuntime, exception.CleanupOutcome.Kind);
		Assert.Null(exception.CleanupOutcome.Status);
		Assert.True(exception.CleanupOutcome.IsTerminal);
		EngineTest.Run(L, "assert(registrations == 1 and removals == 0)"u8);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Owned_registration_publication_failure_with_unavailable_unregister_preserves_the_primary_cause()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		EngineTest.Run(L, SymbolHandler);
		EngineTest.Run(L, "unregisterSymbol = nil"u8);
		InvalidOperationException cause = new("injected lease publication failure");

		SymbolRegistrationHandoffException exception = Assert.Throws<SymbolRegistrationHandoffException>(() =>
			SymbolRegistry.TryRegisterOwnedCore(new SymbolName("Player.Health"), 0x140001000UL, default,
				static (name, address, options, identity) =>
					new SymbolRegistrationLease(name, address, options, identity),
				(_, _) => throw cause));

		Assert.Same(cause, exception.InnerException);
		Assert.Equal(SymbolRegistrationReleaseKind.CleanupUnavailable, exception.CleanupOutcome.Kind);
		Assert.Equal(LuaOperationStatusKind.GlobalUnavailable, exception.CleanupOutcome.Status!.Value.Kind);
		Assert.False(exception.CleanupOutcome.IsTerminal);
		EngineTest.Run(L, "assert(registrations == 1)"u8);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Owned_registration_compensation_failure_is_indeterminate_and_never_retries()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		EngineTest.Run(L, SymbolHandler);
		EngineTest.Run(L, """
		                  unregisterSymbol = function(name)
		                    removals = removals + 1
		                    error('cleanup started then failed')
		                  end
		                  """u8);
		InvalidOperationException cause = new("injected lease factory failure");

		SymbolRegistrationHandoffException exception = Assert.Throws<SymbolRegistrationHandoffException>(() =>
			SymbolRegistry.TryRegisterOwnedCore(new SymbolName("Player.Health"), 0x140001000UL, default,
				(_, _, _, _) => throw cause,
				static (_, _) => throw new InvalidOperationException("The publisher must not run.")));

		Assert.Same(cause, exception.InnerException);
		Assert.Equal(SymbolRegistrationReleaseKind.CleanupIndeterminate, exception.CleanupOutcome.Kind);
		Assert.Equal(LuaOperationStatusKind.LuaFailure, exception.CleanupOutcome.Status!.Value.Kind);
		Assert.True(exception.CleanupOutcome.IsTerminal);
		EngineTest.Run(L, "assert(registrations == 1 and removals == 1)"u8);
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
			EngineTest.Run(L, SymbolHandler);
			EngineTest.Run(L, "unregisterSymbol = function(name) error('must not run after detach') end"u8);
			SymbolRegistrationAcquireOutcome acquired =
				SymbolRegistry.TryRegisterOwned(new SymbolName("Player.Health"), 0x140001000UL);
			Assert.True(acquired.HasLease);
			lease = acquired.Lease!;
		}

		SymbolRegistrationReleaseOutcome release = lease.Release();

		Assert.Equal(SymbolRegistrationReleaseKind.StaleRuntime, release.Kind);
		Assert.Null(release.Status);
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
		EngineTest.Run(L, SymbolHandler);
		EngineTest.Run(L, "saved_unregister = unregisterSymbol; unregisterSymbol = nil"u8);

		SymbolRegistrationAcquireOutcome retryable = SymbolRegistry.TryRegisterOwned(new SymbolName("Player.Health"),
			0x140001000UL,
			new SymbolRegistrationOptions(true));

		Assert.True(retryable.HasLease);
		Assert.True(retryable.Lease!.Options.DoNotSave);
		SymbolRegistrationReleaseOutcome unavailable = retryable.Lease.Release();
		Assert.Equal(SymbolRegistrationReleaseKind.CleanupUnavailable, unavailable.Kind);
		Assert.False(unavailable.IsTerminal);
		Assert.False(retryable.Lease.IsTerminal);

		EngineTest.Run(L, "unregisterSymbol = saved_unregister"u8);
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
		EngineTest.Run(L, SymbolHandler);
		EngineTest.Run(L,
			"unregisterSymbol = function(name) removals = removals + 1; error('cleanup started then failed') end"u8);
		SymbolRegistrationAcquireOutcome failed =
			SymbolRegistry.TryRegisterOwned(new SymbolName("Player.Mana"), 0x140002000UL);
		SymbolRegistrationReleaseOutcome indeterminate = failed.Lease!.Release();

		Assert.Equal(SymbolRegistrationReleaseKind.CleanupIndeterminate, indeterminate.Kind);
		Assert.True(indeterminate.IsTerminal);
		Assert.False(indeterminate.Status!.Value.IsSuccess);
		Assert.True(failed.Lease.IsTerminal);
		Assert.Equal(SymbolRegistrationReleaseKind.AlreadyReleased, failed.Lease.Release().Kind);
		failed.Lease.Dispose();
		failed.Lease.Dispose();
		EngineTest.Run(L, "assert(removals == 1)"u8);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Owned_registration_dispose_is_no_throw_idempotent_and_unregisters_once()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		EngineTest.Run(L, SymbolHandler);
		SymbolRegistrationAcquireOutcome acquired =
			SymbolRegistry.TryRegisterOwned(new SymbolName("Player.Health"), 0x140001000UL);

		Assert.True(acquired.HasLease);
		acquired.Lease!.Dispose();
		acquired.Lease.Dispose();

		Assert.True(acquired.Lease.IsTerminal);
		Assert.Equal(SymbolRegistrationReleaseKind.AlreadyReleased, acquired.Lease.Release().Kind);
		EngineTest.Run(L, "assert(removals == 1)"u8);
		Assert.Equal(0, L.Top);
	}
}
