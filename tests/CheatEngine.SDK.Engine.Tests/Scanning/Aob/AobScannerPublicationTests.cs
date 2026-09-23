using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Scanning.Aob;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Scanning.Aob;

/// <summary>
///     The AOB result list has a single destroy authority from the moment CE returns it (audit ch.08, F13 SDK side,
///     A13-34): a managed failure while publishing its owner destroys the raw list once and rethrows the original
///     failure; a successful publication leaves the owner as the only authority.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class AobScannerPublicationTests
{
	[Fact]
	[Trait("Qualification", "Q27")]
	public void TryScanOutcome_publication_failure_destroys_the_raw_list_once_and_rethrows()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		CEObject handle = CreateTracedList(L, "Probe", false);
		AobStringListTestHost.InstallAobScan(L, handle);
		InvalidOperationException injected = new("injected publication failure");
		Owned<StringList>? results = null;

		InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() =>
			AobScanner.TryScanOutcomeCore("48 8B", AobScanOptions.Default, _ => throw injected, out results));

		Assert.Same(injected, thrown);
		Assert.Null(results);
		Assert.True(FakeHost.IsDestroyed(L, handle));
		Assert.Equal("list.destroy", ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q27")]
	public void TryScanDetailed_publication_failure_destroys_the_raw_list_once_and_rethrows()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		CEObject handle = CreateTracedList(L, "Probe", false);
		AobStringListTestHost.InstallAobScan(L, handle);
		InvalidOperationException injected = new("injected owner allocation failure");
		Owned<StringList>? results = null;

		InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() =>
			AobScanner.TryScanDetailedCore("48 8B", AobScanOptions.Default, _ => throw injected, out results));

		Assert.Same(injected, thrown);
		Assert.Null(results);
		Assert.True(FakeHost.IsDestroyed(L, handle));
		Assert.Equal("list.destroy", ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q27")]
	public void
		TryScanOutcome_publication_failure_with_a_refusing_destroy_rethrows_the_original_failure_without_retrying()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		CEObject handle = CreateTracedList(L, "Stubborn", true);
		AobStringListTestHost.InstallAobScan(L, handle);
		InvalidOperationException injected = new("injected publication failure");
		Owned<StringList>? results = null;

		InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() =>
			AobScanner.TryScanOutcomeCore("48 8B", AobScanOptions.Default, _ => throw injected, out results));

		Assert.Same(injected, thrown);
		Assert.Null(results);
		Assert.Equal("list.destroy", ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q27")]
	public void TryScanOutcome_publication_failure_with_a_lua_exception_is_rethrown_not_reported_as_a_lua_failure()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		CEObject handle = CreateTracedList(L, "Probe", false);
		AobStringListTestHost.InstallAobScan(L, handle);
		LuaException injected = new("injected publication failure");
		Owned<StringList>? results = null;

		LuaException thrown = Assert.Throws<LuaException>(() =>
			AobScanner.TryScanOutcomeCore("48 8B", AobScanOptions.Default, _ => throw injected, out results));

		Assert.Same(injected, thrown);
		Assert.Null(results);
		Assert.Equal("list.destroy", ReadTrace(L));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	[Trait("Qualification", "Q27")]
	public void TryScanOutcome_successful_publication_leaves_the_only_destroy_authority_with_the_owner()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		CEObject handle = AobStringListTestHost.CreateList(L);
		AobStringListTestHost.InstallAobScan(L, handle);
		long destroyedBefore = FakeHost.DestroyedCount(L);

		AobScanOutcome outcome = AobScanner.TryScanOutcome("48 8B", out Owned<StringList>? results);

		Assert.Equal(AobScanOutcome.Matches(2), outcome);
		Owned<StringList> owned = Assert.IsType<Owned<StringList>>(results);
		Assert.Equal(destroyedBefore, FakeHost.DestroyedCount(L));
		Assert.False(FakeHost.IsDestroyed(L, handle));
		Assert.Equal(0, L.Top);

		owned.Dispose();
		Assert.Equal(destroyedBefore + 1, FakeHost.DestroyedCount(L));
		owned.Dispose();

		Assert.Equal(destroyedBefore + 1, FakeHost.DestroyedCount(L));
		Assert.True(FakeHost.IsDestroyed(L, handle));
		Assert.Equal(0, L.Top);
	}

	// A two-entry list whose destroy is traced; it can refuse destruction like the model's Stubborn class does.
	private static CEObject CreateTracedList(LuaState state, string className, bool destroyRaises)
	{
		EngineTest.Run(state, "trace = {}"u8);
		string refusal = destroyRaises ? "; error('refuses to be destroyed')" : string.Empty;
		return FakeHost.CreateObject(state, className,
			"o.props.Count = 2\n" +
			"o.items = { '00401000', '7FF6A1B2C3D4' }\n" +
			"o.getters.destroy = function(o) return function() o.destroyed = true; table.insert(trace, 'list.destroy')" +
			refusal + " end end");
	}

	private static string ReadTrace(LuaState state)
	{
		using LuaFrame frame = new(state);
		EngineTest.Run(state, "return table.concat(trace, ',')"u8, 1);
		return EngineTest.ReadString(state, -1);
	}
}
