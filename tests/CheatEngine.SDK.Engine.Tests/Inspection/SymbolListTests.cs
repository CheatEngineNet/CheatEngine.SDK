using System.Reflection;
using System.Text;

using CheatEngine.SDK.Annotations.Lifetime;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Inspection;

/// <summary>
///     Plugin-owned symbol lists, the borrowed main list, and the registration lease that unregisters before it
///     destroys, against a <c>SymbolList</c> class double.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class SymbolListTests
{
	[Fact]
	public void Symbol_list_create_returns_an_owner_that_destroys_once()
	{
		using Fixture fixture = new();
		CEObject handle = fixture.StageCreatedList();

		LuaOperationStatus status = SymbolLists.TryCreate(out Owned<SymbolList>? created);

		Assert.True(status.IsSuccess);
		Owned<SymbolList> owner = Assert.IsType<Owned<SymbolList>>(created);
		Assert.Equal(handle, owner.Handle);
		Assert.Equal(LuaRuntime.CurrentStateIdentity, owner.Origin.Runtime);
		fixture.Execute("assert(create_arguments == 0)");
		owner.Dispose();
		owner.Dispose();
		Assert.True(FakeHost.IsDestroyed(fixture.State, handle));
		Assert.Equal(1, FakeHost.DestroyedCount(fixture.State));
		Assert.Equal(TargetReleaseStatus.Released, owner.LastReleaseOutcome.Status);
		Assert.Equal(0, fixture.State.Top);
	}

	[Fact]
	public void Symbol_list_create_distinguishes_missing_global_lua_failure_and_non_object()
	{
		using Fixture fixture = new();

		// A resolved global stays cached for the state identity, so each redefinition starts a new state generation.
		LuaOperationStatus missing = SymbolLists.TryCreate(out Owned<SymbolList>? none);
		fixture.Execute("createSymbolList = function() error('out of memory') end");
		LuaOperationStatus raised = SymbolLists.TryCreate(out _);
		FakeHost.ReplaceStateGeneration();
		fixture.Execute("createSymbolList = function() return nil end");
		LuaOperationStatus nil = SymbolLists.TryCreate(out _);
		FakeHost.ReplaceStateGeneration();
		fixture.Execute("createSymbolList = function() return { not_an_object = true } end");
		LuaOperationStatus invalid = SymbolLists.TryCreate(out Owned<SymbolList>? stillNone);

		Assert.Equal(LuaOperationStatusKind.GlobalUnavailable, missing.Kind);
		Assert.Null(none);
		Assert.Equal(LuaOperationStatusKind.LuaFailure, raised.Kind);
		Assert.Equal(LuaStatus.RuntimeError, raised.LuaStatus);
		Assert.Equal(LuaOperationStatusKind.NilResult, nil.Kind);
		Assert.Equal(LuaOperationStatusKind.InvalidResult, invalid.Kind);
		Assert.Null(stillNone);
		Assert.Equal(0, fixture.State.Top);
	}

	[Fact]
	public void Symbol_list_members_pass_exact_arguments_and_restore_the_stack()
	{
		using Fixture fixture = new();
		CEObject handle = FakeHost.CreateSymbolList(fixture.State);
		SymbolList list = new(handle);

		Assert.True(list.TryAddSymbol("game.exe", "hp\0max", new Address(0x8000_0000_0000_0010UL), 8).IsSuccess);
		FakeHost.RunOnObject(fixture.State, handle, """
		                                            local a = o.last_args
		                                            assert(a.n == 4)
		                                            assert(a[1] == "game.exe" and a[2] == "hp\0max" and #a[2] == 6)
		                                            assert(a[3] == math.mininteger + 0x10 and a[4] == 8)
		                                            """);
		Assert.True(list.TryGetSymbolFromString("hp\0max", out SymbolInfo byName).IsSuccess);
		FakeHost.RunOnObject(fixture.State, handle, "assert(o.last_args.n == 1)");
		Assert.True(list.TryGetSymbolFromAddress(new Address(0x8000_0000_0000_0014UL), out SymbolInfo byAddress)
			.IsSuccess);
		FakeHost.RunOnObject(fixture.State, handle, "assert(o.last_args.n == 1)");
		Assert.True(list.TryDeleteSymbol(new Address(0x8000_0000_0000_0010UL)).IsSuccess);
		FakeHost.RunOnObject(fixture.State, handle, "assert(o.last_args.n == 1 and next(o.symbols) == nil)");
		Assert.True(list.TryAddSymbol("game.exe", "ammo", new Address(0x140000000), 4).IsSuccess);
		Assert.True(list.TryDeleteSymbol("ammo").IsSuccess);
		FakeHost.RunOnObject(fixture.State, handle, "assert(o.last_args.n == 1 and o.last_args[1] == 'ammo')");
		Assert.True(list.TryClear().IsSuccess);
		Assert.True(list.TrySetName("plugin symbols").IsSuccess);
		Assert.True(list.TryGetName(out string? name).IsSuccess);

		SymbolInfo expected = new("game.exe", "hp\0max", new Address(0x8000_0000_0000_0010UL), new MemorySize(8));
		Assert.Equal(expected, byName);
		Assert.Equal(expected, byAddress);
		Assert.Equal("plugin symbols", name);
		Assert.Throws<ArgumentOutOfRangeException>(() => list.TryAddSymbol("m", "k", new Address(1), -1));
		Assert.Throws<ArgumentNullException>(() => list.TryAddSymbol(null!, "k", new Address(1), 1));
		Assert.Equal(0, fixture.State.Top);
	}

	[Fact]
	public void Symbol_lookup_distinguishes_not_found_from_a_malformed_symbol_table()
	{
		using Fixture fixture = new();
		CEObject handle = FakeHost.CreateSymbolList(fixture.State, "o.props.PID = 4242");
		SymbolList list = new(handle);

		LuaOperationStatus notFound = list.TryGetSymbolFromString("missing", out SymbolInfo none);
		LuaOperationStatus noName = list.TryGetName(out string? name);
		LuaOperationStatus processId = list.TryGetProcessId(out int pid);
		FakeHost.RunOnObject(fixture.State, handle, "o.malformed = true; o.props.PID = 'not a pid'");
		LuaOperationStatus malformedString = list.TryGetSymbolFromString("any", out _);
		LuaOperationStatus malformedAddress = list.TryGetSymbolFromAddress(new Address(1), out _);
		LuaOperationStatus badProcessId = list.TryGetProcessId(out _);

		Assert.Equal(LuaOperationStatusKind.NilResult, notFound.Kind);
		Assert.Equal(default, none);
		Assert.Equal(LuaOperationStatusKind.NilResult, noName.Kind);
		Assert.Null(name);
		Assert.True(processId.IsSuccess);
		Assert.Equal(4242, pid);
		Assert.Equal(LuaOperationStatusKind.InvalidResult, malformedString.Kind);
		Assert.Equal(LuaOperationStatusKind.InvalidResult, malformedAddress.Kind);
		Assert.Equal(LuaOperationStatusKind.InvalidResult, badProcessId.Kind);
		Assert.Equal(0, fixture.State.Top);
	}

	[Fact]
	public void Symbol_list_register_then_release_unregisters_before_destroying()
	{
		using Fixture fixture = new();
		CEObject handle = fixture.StageCreatedList();
		Assert.True(SymbolLists.TryCreate(out Owned<SymbolList>? created).IsSuccess);
		Owned<SymbolList> owner = created!;

		LuaOperationStatus registered = SymbolLists.TryRegister(owner, out SymbolListRegistrationLease? lease);

		Assert.True(registered.IsSuccess);
		SymbolListRegistrationLease registration = Assert.IsType<SymbolListRegistrationLease>(lease);
		Assert.True(owner.IsDisposed);
		Assert.True(registration.RegistrationConfirmed);
		Assert.Equal(owner.Origin, registration.Origin);
		Assert.Equal(new SymbolList(handle), registration.List);
		FakeHost.RunOnObject(fixture.State, handle, "assert(o.registered == true and o.register_args == 0)");

		SymbolListRegistrationReleaseOutcome outcome = registration.Release();

		Assert.Equal(SymbolRegistrationReleaseKind.Released, outcome.UnregisterKind);
		Assert.True(outcome.UnregisterStatus!.Value.IsSuccess);
		Assert.Equal(TargetReleaseStatus.Released, outcome.ListRelease.Status);
		Assert.True(outcome.IsTerminal);
		Assert.True(registration.IsTerminal);
		Assert.Throws<ObjectDisposedException>(() => registration.List);
		SymbolListRegistrationReleaseOutcome alreadyReleased = registration.Release();
		Assert.Equal(SymbolRegistrationReleaseKind.AlreadyReleased, alreadyReleased.UnregisterKind);
		Assert.Null(alreadyReleased.UnregisterStatus);
		fixture.Execute("assert(symbol_list_unregister_calls == 1 and symbol_list_destroyed_while_registered == 0)");
		FakeHost.RunOnObject(fixture.State, handle, "assert(o.unregister_args == 0 and o.destroyed == true)");
		Assert.Equal(1, FakeHost.DestroyedCount(fixture.State));
		Assert.Equal(0, fixture.State.Top);
	}

	[Fact]
	public void Symbol_list_unregister_failure_abandons_the_list_without_destroying_it()
	{
		using Fixture fixture = new();
		CEObject handle = fixture.StageCreatedList("o.unregister_raises = true");
		Assert.True(SymbolLists.TryCreate(out Owned<SymbolList>? owner).IsSuccess);
		Assert.True(SymbolLists.TryRegister(owner!, out SymbolListRegistrationLease? lease).IsSuccess);

		SymbolListRegistrationReleaseOutcome outcome = lease!.Release();
		lease.Dispose();

		Assert.Equal(SymbolRegistrationReleaseKind.CleanupIndeterminate, outcome.UnregisterKind);
		Assert.Equal(LuaOperationStatusKind.LuaFailure, outcome.UnregisterStatus!.Value.Kind);
		Assert.Equal(TargetReleaseStatus.NotInvoked, outcome.ListRelease.Status);
		Assert.True(outcome.IsTerminal);
		Assert.False(FakeHost.IsDestroyed(fixture.State, handle));
		fixture.Execute("assert(symbol_list_unregister_calls == 1)");
		Assert.Equal(0, fixture.State.Top);
	}

	[Fact]
	public void Symbol_list_unregister_member_missing_keeps_the_lease_retryable()
	{
		using Fixture fixture = new();
		CEObject handle = fixture.StageCreatedList();
		Assert.True(SymbolLists.TryCreate(out Owned<SymbolList>? owner).IsSuccess);
		Assert.True(SymbolLists.TryRegister(owner!, out SymbolListRegistrationLease? lease).IsSuccess);
		FakeHost.RunOnObject(fixture.State, handle, "o.getters.unregister = function() return nil end");

		SymbolListRegistrationReleaseOutcome unavailable = lease!.Release();
		FakeHost.RunOnObject(fixture.State, handle, "o.getters.unregister = nil");
		SymbolListRegistrationReleaseOutcome released = lease.Release();

		Assert.Equal(SymbolRegistrationReleaseKind.CleanupUnavailable, unavailable.UnregisterKind);
		Assert.Equal(LuaOperationStatusKind.GlobalUnavailable, unavailable.UnregisterStatus!.Value.Kind);
		Assert.False(unavailable.IsTerminal);
		Assert.Equal(SymbolRegistrationReleaseKind.Released, released.UnregisterKind);
		Assert.Equal(TargetReleaseStatus.Released, released.ListRelease.Status);
		Assert.Equal(0, fixture.State.Top);
	}

	[Fact]
	public void Symbol_list_lease_after_reattach_sends_no_host_call()
	{
		using Fixture fixture = new();
		CEObject handle = fixture.StageCreatedList();
		Assert.True(SymbolLists.TryCreate(out Owned<SymbolList>? owner).IsSuccess);
		Assert.True(SymbolLists.TryRegister(owner!, out SymbolListRegistrationLease? lease).IsSuccess);

		LuaRuntime.Detach();
		LuaRuntime.Attach(fixture.Binding);
		SymbolListRegistrationReleaseOutcome outcome = lease!.Release();

		Assert.Equal(SymbolRegistrationReleaseKind.StaleRuntime, outcome.UnregisterKind);
		Assert.Null(outcome.UnregisterStatus);
		Assert.Equal(TargetReleaseStatus.RefusedRuntimeChanged, outcome.ListRelease.Status);
		Assert.True(outcome.IsTerminal);
		fixture.Execute("assert(symbol_list_unregister_calls == 0)");
		Assert.False(FakeHost.IsDestroyed(fixture.State, handle));
		Assert.Equal(0, fixture.State.Top);
	}

	[Fact]
	public void Symbol_list_lease_while_detached_abandons_the_list_without_a_host_call()
	{
		using Fixture fixture = new();
		CEObject handle = fixture.StageCreatedList();
		Assert.True(SymbolLists.TryCreate(out Owned<SymbolList>? owner).IsSuccess);
		Assert.True(SymbolLists.TryRegister(owner!, out SymbolListRegistrationLease? lease).IsSuccess);

		LuaRuntime.Detach();
		SymbolListRegistrationReleaseOutcome outcome = lease!.Release();

		Assert.Equal(SymbolRegistrationReleaseKind.StaleRuntime, outcome.UnregisterKind);
		Assert.Equal(TargetReleaseStatus.NotInvoked, outcome.ListRelease.Status);
		fixture.Execute("assert(symbol_list_unregister_calls == 0)");
		Assert.False(FakeHost.IsDestroyed(fixture.State, handle));
	}

	[Fact]
	public void Symbol_list_register_failure_after_invocation_keeps_the_list_in_an_unconfirmed_lease()
	{
		using Fixture fixture = new();
		CEObject handle = fixture.StageCreatedList("o.register_raises = true");
		Assert.True(SymbolLists.TryCreate(out Owned<SymbolList>? owner).IsSuccess);

		LuaOperationStatus status = SymbolLists.TryRegister(owner!, out SymbolListRegistrationLease? lease);

		Assert.Equal(LuaOperationStatusKind.LuaFailure, status.Kind);
		Assert.True(owner!.IsDisposed);
		SymbolListRegistrationLease unconfirmed = Assert.IsType<SymbolListRegistrationLease>(lease);
		Assert.False(unconfirmed.RegistrationConfirmed);

		SymbolListRegistrationReleaseOutcome outcome = unconfirmed.Release();

		Assert.Equal(SymbolRegistrationReleaseKind.Released, outcome.UnregisterKind);
		Assert.Equal(TargetReleaseStatus.Released, outcome.ListRelease.Status);
		fixture.Execute("assert(symbol_list_unregister_calls == 1 and symbol_list_destroyed_while_registered == 0)");
		Assert.True(FakeHost.IsDestroyed(fixture.State, handle));
		Assert.Equal(0, fixture.State.Top);
	}

	[Fact]
	public void Symbol_list_register_member_missing_keeps_the_owner_with_the_caller()
	{
		using Fixture fixture = new();
		CEObject handle = fixture.StageCreatedList("o.getters.register = function() return nil end");
		Assert.True(SymbolLists.TryCreate(out Owned<SymbolList>? owner).IsSuccess);

		LuaOperationStatus status = SymbolLists.TryRegister(owner!, out SymbolListRegistrationLease? lease);

		Assert.Equal(LuaOperationStatusKind.GlobalUnavailable, status.Kind);
		Assert.Null(lease);
		Assert.False(owner!.IsDisposed);
		owner.Dispose();
		Assert.True(FakeHost.IsDestroyed(fixture.State, handle));
		Assert.Equal(0, fixture.State.Top);
	}

	[Fact]
	public void Symbol_list_lease_publication_failure_unregisters_once()
	{
		using Fixture fixture = new();
		CEObject handle = fixture.StageCreatedList();
		Assert.True(SymbolLists.TryCreate(out Owned<SymbolList>? owner).IsSuccess);
		InvalidOperationException cause = new("injected lease construction failure");

		SymbolListRegistrationHandoffException exception = Assert.Throws<SymbolListRegistrationHandoffException>(() =>
			SymbolLists.TryRegisterCore(owner!, out _, (_, _) => throw cause));

		Assert.Same(cause, exception.InnerException);
		Assert.Equal(SymbolRegistrationReleaseKind.Released, exception.CleanupOutcome.UnregisterKind);
		Assert.Equal(TargetReleaseStatus.Unspecified, exception.CleanupOutcome.ListRelease.Status);
		fixture.Execute("assert(symbol_list_register_calls == 1 and symbol_list_unregister_calls == 1)");
		Assert.False(owner!.IsDisposed);
		owner.Dispose();
		Assert.True(FakeHost.IsDestroyed(fixture.State, handle));
		fixture.Execute("assert(symbol_list_destroyed_while_registered == 0)");
		Assert.Equal(0, fixture.State.Top);
	}

	[Fact]
	public void Symbol_list_lease_publication_failure_with_a_raising_unregister_abandons_the_list()
	{
		using Fixture fixture = new();
		CEObject handle = fixture.StageCreatedList("o.unregister_raises = true");
		Assert.True(SymbolLists.TryCreate(out Owned<SymbolList>? owner).IsSuccess);

		SymbolListRegistrationHandoffException exception = Assert.Throws<SymbolListRegistrationHandoffException>(() =>
			SymbolLists.TryRegisterCore(owner!, out _,
				static (_, _) => throw new InvalidOperationException("injected lease construction failure")));

		Assert.Equal(SymbolRegistrationReleaseKind.CleanupIndeterminate, exception.CleanupOutcome.UnregisterKind);
		Assert.True(owner!.IsDisposed);
		Assert.False(FakeHost.IsDestroyed(fixture.State, handle));
		Assert.Equal(0, fixture.State.Top);
	}

	[Fact]
	public void Symbol_list_created_in_a_previous_runtime_cannot_be_registered()
	{
		using Fixture fixture = new();
		fixture.StageCreatedList();
		Assert.True(SymbolLists.TryCreate(out Owned<SymbolList>? owner).IsSuccess);

		FakeHost.ReplaceStateGeneration();

		Assert.Throws<InvalidOperationException>(() => SymbolLists.TryRegister(owner!, out _));
		Assert.False(owner!.IsDisposed);
		fixture.Execute("assert(symbol_list_register_calls == 0)");
		Assert.Equal(TargetReleaseStatus.RefusedRuntimeChanged, owner.ReleaseWithOutcome().Status);
	}

	[Fact]
	public void Main_symbol_list_is_borrowed_only()
	{
		using Fixture fixture = new();
		CEObject main = FakeHost.CreateSymbolList(fixture.State);
		FakeHost.SetGlobalObject(fixture.State, "main_list", main);
		fixture.Execute("getMainSymbolList = function(...) main_arguments = select('#', ...) return main_list end");

		LuaOperationStatus status = SymbolLists.TryGetMain(out SymbolList borrowed);

		Assert.True(status.IsSuccess);
		Assert.Equal(new SymbolList(main), borrowed);
		fixture.Execute("assert(main_arguments == 0)");
		Assert.Equal(0, fixture.State.Top);

		ParameterInfo output = typeof(SymbolLists).GetMethod(nameof(SymbolLists.TryGetMain))!.GetParameters()[0];
		Assert.True(Attribute.IsDefined(output, typeof(CEOwnedAttribute)));
		Assert.False(typeof(IDisposable).IsAssignableFrom(typeof(SymbolList)));
		List<string> offenders = [];
		foreach (Type type in typeof(SymbolList).Assembly.GetExportedTypes())
		{
			foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance |
			                                              BindingFlags.Static | BindingFlags.DeclaredOnly))
			{
				bool takesBorrowedList = (type == typeof(SymbolList) && !method.IsStatic) ||
				                         Array.Exists(method.GetParameters(),
					                         static parameter => parameter.ParameterType == typeof(SymbolList));
				bool lifecycleName = method.Name.Contains("register", StringComparison.OrdinalIgnoreCase) ||
				                     method.Name.Contains("destroy", StringComparison.OrdinalIgnoreCase);
				if (takesBorrowedList && lifecycleName)
				{
					offenders.Add(type.Name + "." + method.Name);
				}
			}
		}

		Assert.True(offenders.Count == 0,
			"A public method registers, unregisters or destroys a borrowed SymbolList: " +
			string.Join(", ", offenders));
	}

	[Fact]
	public void Symbol_list_calls_after_the_host_destroyed_the_list_fail_without_an_escaping_lua_error()
	{
		using Fixture fixture = new();
		CEObject handle = FakeHost.CreateSymbolList(fixture.State,
			"o.getters.Name = function(o) if o.destroyed then error('attempt to index a userdata value') end return o.props.Name end");
		SymbolList list = new(handle);
		FakeHost.RunOnObject(fixture.State, handle, "o.destroyed = true");

		Assert.Equal(LuaOperationStatusKind.LuaFailure, list.TryClear().Kind);
		Assert.Equal(LuaOperationStatusKind.LuaFailure,
			list.TryAddSymbol("m", "k", new Address(1), 1).Kind);
		Assert.Equal(LuaOperationStatusKind.LuaFailure, list.TryGetSymbolFromString("k", out _).Kind);
		Assert.Equal(LuaOperationStatusKind.LuaFailure, list.TryGetName(out _).Kind);
		Assert.Equal(0, fixture.State.Top);

		// The state is healthy afterwards.
		CEObject other = FakeHost.CreateSymbolList(fixture.State);
		Assert.True(new SymbolList(other).TryClear().IsSuccess);
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
			FakeHost.InstallSymbolListClass(State);
		}

		public LuaState State
		{
			get;
		}

		public LuaHostBinding Binding => _scope.Binding;

		public void Dispose()
		{
			_scope.Dispose();
			_nativeState.Dispose();
		}

		/// <summary>Creates a list that the next <c>createSymbolList()</c> call returns.</summary>
		public CEObject StageCreatedList(string initializer = "")
		{
			CEObject list = FakeHost.CreateSymbolList(State, initializer);
			FakeHost.SetGlobalObject(State, "staged_list", list);
			Execute("createSymbolList = function(...) create_arguments = select('#', ...) return staged_list end");
			return list;
		}

		public void Execute(string source)
		{
			EngineTest.Run(State, Encoding.UTF8.GetBytes(source));
		}
	}
}
