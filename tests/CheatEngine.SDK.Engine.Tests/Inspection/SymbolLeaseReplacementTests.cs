using System.Text;

using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Inspection;

/// <summary>
///     A symbol registration lease verifies, before unregistering by name, that the name still resolves to the leased
///     address, so it never removes a newer third-party definition; its release kinds start at <c>Unknown</c>.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class SymbolLeaseReplacementTests
{
	private static readonly SymbolName SName = new("Player.Health");

	[Fact]
	public void Symbol_lease_unregisters_when_the_name_still_maps_to_the_leased_address()
	{
		using Fixture fixture = new();

		SymbolRegistrationAcquireOutcome acquired = SymbolRegistry.TryRegisterOwned(SName, 0x140001000UL);
		SymbolRegistrationReleaseOutcome released = acquired.Lease!.Release();

		Assert.Equal(SymbolRegistrationReleaseKind.Released, released.Kind);
		Assert.True(released.Status!.Value.IsSuccess);
		Assert.True(released.IsTerminal);
		fixture.Execute("assert(removals == 1 and registered_symbols['Player.Health'] == nil)");
		fixture.Execute("assert(lookups == 1)");
		Assert.Equal(0, fixture.State.Top);
	}

	[Fact]
	public void Symbol_lease_skips_unregister_when_the_name_was_replaced()
	{
		using Fixture fixture = new();
		SymbolRegistrationAcquireOutcome acquired = SymbolRegistry.TryRegisterOwned(SName, 0x140001000UL);

		// A third-party script re-registers the name at another address, outside the SDK coordinator.
		fixture.Execute("registered_symbols['Player.Health'] = 0x150000000");
		SymbolRegistrationReleaseOutcome outcome = acquired.Lease!.Release();

		Assert.Equal(SymbolRegistrationReleaseKind.Replaced, outcome.Kind);
		Assert.True(outcome.Status!.Value.IsSuccess);
		Assert.True(outcome.IsTerminal);
		Assert.True(acquired.Lease.IsTerminal);
		Assert.Equal(SymbolRegistrationReleaseKind.AlreadyReleased, acquired.Lease.Release().Kind);
		fixture.Execute("assert(removals == 0 and registered_symbols['Player.Health'] == 0x150000000)");
		Assert.Equal(0, fixture.State.Top);
	}

	[Fact]
	public void Symbol_lease_reports_externally_removed_when_the_name_no_longer_resolves()
	{
		using Fixture fixture = new();
		SymbolRegistrationAcquireOutcome acquired = SymbolRegistry.TryRegisterOwned(SName, 0x140001000UL);

		fixture.Execute("registered_symbols['Player.Health'] = nil");
		SymbolRegistrationReleaseOutcome outcome = acquired.Lease!.Release();

		Assert.Equal(SymbolRegistrationReleaseKind.ExternallyRemoved, outcome.Kind);
		Assert.Equal(LuaOperationStatusKind.NilResult, outcome.Status!.Value.Kind);
		Assert.True(outcome.IsTerminal);
		fixture.Execute("assert(removals == 0)");
		Assert.Equal(0, fixture.State.Top);
	}

	[Fact]
	public void Symbol_lease_lookup_failure_keeps_the_lease_retryable_without_unregistering()
	{
		using Fixture fixture = new();
		SymbolRegistrationAcquireOutcome acquired = SymbolRegistry.TryRegisterOwned(SName, 0x140001000UL);

		fixture.Execute("lookup_mode = 'raise'");
		SymbolRegistrationReleaseOutcome failed = acquired.Lease!.Release();
		fixture.Execute("lookup_mode = 'malformed'");
		SymbolRegistrationReleaseOutcome malformed = acquired.Lease.Release();
		fixture.Execute("lookup_mode = nil");
		SymbolRegistrationReleaseOutcome released = acquired.Lease.Release();

		Assert.Equal(SymbolRegistrationReleaseKind.CleanupUnavailable, failed.Kind);
		Assert.Equal(LuaOperationStatusKind.LuaFailure, failed.Status!.Value.Kind);
		Assert.False(failed.IsTerminal);
		Assert.Equal(SymbolRegistrationReleaseKind.CleanupUnavailable, malformed.Kind);
		Assert.Equal(LuaOperationStatusKind.InvalidResult, malformed.Status!.Value.Kind);
		Assert.False(malformed.IsTerminal);
		Assert.Equal(SymbolRegistrationReleaseKind.Released, released.Kind);
		fixture.Execute("assert(removals == 1 and lookups == 3)");
		Assert.Equal(0, fixture.State.Top);
	}

	[Fact]
	public void Symbol_lease_without_a_lookup_global_keeps_the_lease_retryable_without_unregistering()
	{
		using Fixture fixture = new();
		fixture.Execute("getAddressSafe = nil");
		SymbolRegistrationAcquireOutcome acquired = SymbolRegistry.TryRegisterOwned(SName, 0x140001000UL);

		SymbolRegistrationReleaseOutcome outcome = acquired.Lease!.Release();

		Assert.Equal(SymbolRegistrationReleaseKind.CleanupUnavailable, outcome.Kind);
		Assert.Equal(LuaOperationStatusKind.GlobalUnavailable, outcome.Status!.Value.Kind);
		Assert.False(outcome.IsTerminal);
		Assert.False(acquired.Lease.IsTerminal);
		fixture.Execute("assert(removals == 0)");
		Assert.Equal(0, fixture.State.Top);
	}

	[Fact]
	public void Symbol_registration_over_an_existing_name_never_unregisters_the_previous_definition()
	{
		using Fixture fixture = new();

		// A host that refuses an existing name: no lease, nothing removed.
		fixture.Execute("""
		                registered_symbols['Player.Health'] = 0x150000000
		                refuse_existing = true
		                """);
		SymbolRegistrationAcquireOutcome refused = SymbolRegistry.TryRegisterOwned(SName, 0x140001000UL);

		Assert.False(refused.HasLease);
		Assert.Equal(LuaOperationStatusKind.LuaFailure, refused.Status.Kind);
		fixture.Execute("assert(removals == 0 and registered_symbols['Player.Health'] == 0x150000000)");

		// A host that overwrites: the lease is ours until a third party registers the name again.
		fixture.Execute("refuse_existing = false");
		SymbolRegistrationAcquireOutcome overwrote = SymbolRegistry.TryRegisterOwned(SName, 0x140001000UL);
		fixture.Execute("registered_symbols['Player.Health'] = 0x160000000");
		SymbolRegistrationReleaseOutcome release = overwrote.Lease!.Release();

		Assert.Equal(SymbolRegistrationReleaseKind.Replaced, release.Kind);
		fixture.Execute("assert(removals == 0 and registered_symbols['Player.Health'] == 0x160000000)");
		Assert.Equal(0, fixture.State.Top);
	}

	[Fact]
	public void Failed_publication_compensation_skips_a_name_replaced_in_the_meantime()
	{
		using Fixture fixture = new();
		LuaState L = fixture.State;

		SymbolRegistrationHandoffException exception = Assert.Throws<SymbolRegistrationHandoffException>(() =>
			SymbolRegistry.TryRegisterOwnedCore(SName, 0x140001000UL, default, (_, _, _, _) =>
			{
				EngineTest.Run(L, "registered_symbols['Player.Health'] = 0x170000000"u8);
				throw new InvalidOperationException("injected lease factory failure");
			}, static (_, _) => throw new InvalidOperationException("The publisher must not run.")));

		Assert.Equal(SymbolRegistrationReleaseKind.Replaced, exception.CleanupOutcome.Kind);
		fixture.Execute("assert(removals == 0 and registered_symbols['Player.Health'] == 0x170000000)");
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Release_kind_default_is_unknown_and_not_terminal()
	{
		SymbolRegistrationReleaseOutcome lease = default;
		SymbolListRegistrationReleaseOutcome list = default;

		Assert.Equal(SymbolRegistrationReleaseKind.Unknown, lease.Kind);
		Assert.Equal(0, (int) SymbolRegistrationReleaseKind.Unknown);
		Assert.False(lease.IsTerminal);
		Assert.Equal(SymbolRegistrationReleaseKind.Unknown, list.UnregisterKind);
		Assert.False(list.IsTerminal);
		Assert.Equal(1, (int) SymbolRegistrationReleaseKind.Released);
		Assert.Equal(7, (int) SymbolRegistrationReleaseKind.Replaced);
		Assert.Equal(8, (int) SymbolRegistrationReleaseKind.ExternallyRemoved);
	}

	[Fact]
	public void Lease_exposes_its_name_address_and_origin()
	{
		using Fixture fixture = new();

		SymbolRegistrationAcquireOutcome acquired =
			SymbolRegistry.TryRegisterOwned(SName, 0xFFFF_FFFF_FFFF_FFF0UL);
		SymbolRegistrationLease lease = acquired.Lease!;

		Assert.Equal(SName, lease.Name);
		Assert.Equal(new Address(0xFFFF_FFFF_FFFF_FFF0UL), lease.Address);
		Assert.Equal(LuaRuntime.CurrentStateIdentity, lease.Origin.Runtime);
		Assert.Null(lease.Origin.Target);
		Assert.True(lease.Origin.IsCurrentRuntime);
		Assert.Equal(SymbolRegistrationReleaseKind.Released, lease.Release().Kind);
		Assert.Equal(0, fixture.State.Top);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void Persistence_option_is_forwarded_unchanged(bool doNotSave)
	{
		using Fixture fixture = new();

		SymbolRegistrationAcquireOutcome acquired =
			SymbolRegistry.TryRegisterOwned(SName, 0x140001000UL, new SymbolRegistrationOptions(doNotSave));

		Assert.Equal(doNotSave, acquired.Lease!.Options.DoNotSave);
		fixture.Execute(doNotSave ? "assert(last_do_not_save == true)" : "assert(last_do_not_save == false)");
		fixture.Execute("assert(last_register_arguments == 3)");
		acquired.Lease.Dispose();
		Assert.Equal(0, fixture.State.Top);
	}

	private sealed class Fixture : IDisposable
	{
		private readonly NativeLuaState _nativeState;
		private readonly HostScope _scope;

		public Fixture()
		{
			EngineTest.RequireNativeLua();
			_nativeState = new NativeLuaState();
			_scope = new HostScope(_nativeState);
			State = _scope.State;
			EngineTest.Run(State, """
			                      registered_symbols = {}
			                      removals = 0
			                      lookups = 0
			                      refuse_existing = false
			                      registerSymbol = function(...)
			                        last_register_arguments = select("#", ...)
			                        local name, address, doNotSave = ...
			                        if refuse_existing and registered_symbols[name] ~= nil then error(name .. " already exists") end
			                        last_do_not_save = doNotSave
			                        registered_symbols[name] = address
			                      end
			                      unregisterSymbol = function(name)
			                        removals = removals + 1
			                        registered_symbols[name] = nil
			                      end
			                      getAddressSafe = function(name, isLocal, shallow)
			                        lookups = lookups + 1
			                        if lookup_mode == "raise" then error("symbol handler busy") end
			                        if lookup_mode == "malformed" then return {} end
			                        return registered_symbols[name]
			                      end
			                      """u8);
		}

		public LuaState State
		{
			get;
		}

		public void Dispose()
		{
			_scope.Dispose();
			_nativeState.Dispose();
		}

		public void Execute(string source)
		{
			EngineTest.Run(State, Encoding.UTF8.GetBytes(source));
		}
	}
}
