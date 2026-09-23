using CheatEngine.SDK.Engine.Assembly;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Assembly;

/// <summary>
///     The bounded, sorted, exact copy of Cheat Engine's Auto Assembler disable information, and the rule that a
///     diagnostic copy that failed never fails the activation.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed class AutoAssemblerDisableInfoSnapshotTests
{
	[Fact]
	public void Snapshot_projects_allocations_symbols_exception_ranges_and_registered_symbols()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AutoAssemblerTestHost.Install(L);
		EngineTest.Run(L, """
		                  aa_disable_info = {
		                    allocs = {
		                      newmem = { address = 0x140000000, size = 4096, prefered = 0x13FFF0000 },
		                      code = { address = 0x140001000, size = 16 },
		                    },
		                    registeredsymbols = { "player_base", "ammo" },
		                    exceptionlist = { 0x140000010, 0x140000020 },
		                    symbols = { newmem = 0x140000000, return_here = 0x140001005 },
		                    compileinfo = { ignored = true },
		                  }
		                  """u8);

		AutoAssemblerApplyOutcome outcome =
			AutoAssemblerPatcher.TryApplyWithOutcome("success", out AutoAssemblerPatch? patch);

		AutoAssemblerDisableInfoSnapshot
			snapshot = Assert.IsType<AutoAssemblerDisableInfoSnapshot>(outcome.DisableInfo);
		Assert.Equal(AutoAssemblerDisableInfoSnapshotStatus.Complete, snapshot.Status);
		Assert.Collection(snapshot.Allocations,
			static code =>
			{
				Assert.Equal("code", code.Name);
				Assert.Equal(new Address(0x140001000), code.Address);
				Assert.Equal(16, code.Size);
				Assert.Null(code.PreferredAddress);
			},
			static newmem =>
			{
				Assert.Equal("newmem", newmem.Name);
				Assert.Equal(new Address(0x140000000), newmem.Address);
				Assert.Equal(4096, newmem.Size);
				Assert.Equal(new Address(0x13FFF0000), newmem.PreferredAddress);
			});
		Assert.Equal(["player_base", "ammo"], snapshot.RegisteredSymbols);
		Assert.Equal([new Address(0x140000010), new Address(0x140000020)], snapshot.ExceptionRanges);
		Assert.Equal(
			[
				new AutoAssemblerSymbolInfo("newmem", new Address(0x140000000)),
				new AutoAssemblerSymbolInfo("return_here", new Address(0x140001005))
			],
			snapshot.Symbols);
		Assert.False(snapshot.HasCCodeSymbolList);
		Assert.IsNotType<AutoAssemblerAllocationInfo[]>(snapshot.Allocations);
		patch!.Dispose();
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Snapshot_sorts_entries_by_ordinal_name()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AutoAssemblerTestHost.Install(L);
		EngineTest.Run(L, """
		                  aa_disable_info = {
		                    allocs = {
		                      b = { address = 2, size = 1 }, B = { address = 1, size = 1 },
		                      a = { address = 3, size = 1 }, _z = { address = 4, size = 1 },
		                    },
		                    symbols = { b = 2, B = 1, a = 3, _z = 4 },
		                  }
		                  """u8);

		AutoAssemblerApplyOutcome outcome =
			AutoAssemblerPatcher.TryApplyWithOutcome("success", out AutoAssemblerPatch? patch);

		AutoAssemblerDisableInfoSnapshot snapshot = outcome.DisableInfo!;
		Assert.Equal(["B", "_z", "a", "b"], snapshot.Allocations.Select(static allocation => allocation.Name),
			StringComparer.Ordinal);
		Assert.Equal(["B", "_z", "a", "b"], snapshot.Symbols.Select(static symbol => symbol.Name),
			StringComparer.Ordinal);
		patch!.Dispose();
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Snapshot_keeps_addresses_above_2_pow_63_exactly()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AutoAssemblerTestHost.Install(L);
		EngineTest.Run(L, """
		                  aa_disable_info = {
		                    allocs = { high = { address = 0x8000000000000001, size = 8, prefered = 0xFFFFFFFFFFFFFFFF } },
		                    exceptionlist = { 0x8000000000000000 },
		                    symbols = { top = 0xFFFFFFFFFFFFFFFE },
		                  }
		                  """u8);

		AutoAssemblerApplyOutcome outcome =
			AutoAssemblerPatcher.TryApplyWithOutcome("success", out AutoAssemblerPatch? patch);

		AutoAssemblerDisableInfoSnapshot snapshot = outcome.DisableInfo!;
		Assert.Equal(AutoAssemblerDisableInfoSnapshotStatus.Complete, snapshot.Status);
		AutoAssemblerAllocationInfo high = Assert.Single(snapshot.Allocations);
		Assert.Equal(new Address(0x8000_0000_0000_0001UL), high.Address);
		Assert.Equal(new Address(0xFFFF_FFFF_FFFF_FFFFUL), high.PreferredAddress);
		Assert.Equal(new Address(0x8000_0000_0000_0000UL), Assert.Single(snapshot.ExceptionRanges));
		Assert.Equal(new Address(0xFFFF_FFFF_FFFF_FFFEUL), Assert.Single(snapshot.Symbols).Address);
		patch!.Dispose();
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Snapshot_truncates_at_the_entry_and_name_limits()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AutoAssemblerTestHost.Install(L);
		EngineTest.Run(L, """
		                  aa_disable_info = {
		                    allocs = { a = { address = 1, size = 1 }, b = { address = 2, size = 1 }, c = { address = 3, size = 1 } },
		                    registeredsymbols = { "first", "second", "third" },
		                    symbols = { short = 1, far_too_long_a_name = 2 },
		                  }
		                  """u8);
		AutoAssemblerOptions options = new()
		{
			MaxDisableInfoEntries = 2,
			MaxDisableInfoNameBytes = 8
		};

		AutoAssemblerApplyOutcome outcome =
			AutoAssemblerPatcher.TryApplyWithOutcome("success", options, out AutoAssemblerPatch? patch);

		AutoAssemblerDisableInfoSnapshot snapshot = outcome.DisableInfo!;
		Assert.Equal(AutoAssemblerApplyOutcomeKind.Applied, outcome.Kind);
		Assert.Equal(AutoAssemblerDisableInfoSnapshotStatus.Truncated, snapshot.Status);
		Assert.Equal(2, snapshot.Allocations.Count);
		Assert.Equal(["first", "second"], snapshot.RegisteredSymbols);
		Assert.Equal("short", Assert.Single(snapshot.Symbols).Name);
		patch!.Dispose();
		Assert.Equal(1, AutoAssemblerTestHost.ReadCounter(L, "auto_assembler_disable_count"));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Malformed_disable_info_publishes_the_patch_with_a_malformed_snapshot()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		AutoAssemblerTestHost.Install(L);
		EngineTest.Run(L, """
		                  aa_disable_info = {
		                    allocs = {
		                      good = { address = 0x1000, size = 16 },
		                      notatable = 42,
		                      badsize = { address = 0x2000, size = "big" },
		                      [7] = { address = 0x3000, size = 1 },
		                    },
		                    registeredsymbols = { "ok", 5 },
		                    exceptionlist = "not a table",
		                    symbols = { fine = 0x10, broken = "zz" },
		                    ccodesymbols = "not a symbol list",
		                  }
		                  """u8);

		AutoAssemblerApplyOutcome outcome =
			AutoAssemblerPatcher.TryApplyWithOutcome("success", out AutoAssemblerPatch? patch);

		Assert.Equal(AutoAssemblerApplyOutcomeKind.Applied, outcome.Kind);
		Assert.Equal(EngineEffectState.Applied, outcome.Effect);
		Assert.True(outcome.HasPatch);
		AutoAssemblerDisableInfoSnapshot snapshot = outcome.DisableInfo!;
		Assert.Equal(AutoAssemblerDisableInfoSnapshotStatus.Malformed, snapshot.Status);
		Assert.Equal("good", Assert.Single(snapshot.Allocations).Name);
		Assert.Equal(["ok"], snapshot.RegisteredSymbols);
		Assert.Empty(snapshot.ExceptionRanges);
		Assert.Equal("fine", Assert.Single(snapshot.Symbols).Name);
		Assert.False(snapshot.HasCCodeSymbolList);
		Assert.Equal(0, L.Top);

		// The rooted table, not the snapshot, stays the disable authority.
		Assert.Equal(TargetReleaseStatus.Released, patch!.ReleaseWithTargetOutcome().Status);
		Assert.True(AutoAssemblerTestHost.ReadBoolean(L, "auto_assembler_disable_received_info"));
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Ccode_symbol_list_is_reported_but_never_owned_or_destroyed()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		FakeHost.InstallSymbolListClass(L);
		AutoAssemblerTestHost.Install(L);
		CEObject ccode = FakeHost.CreateSymbolList(L, "o.registered = true");
		FakeHost.SetGlobalObject(L, "aa_ccode_symbols", ccode);
		EngineTest.Run(L, "aa_disable_info = { ccodesymbols = aa_ccode_symbols }"u8);
		long destroyed = FakeHost.DestroyedCount(L);

		AutoAssemblerApplyOutcome outcome =
			AutoAssemblerPatcher.TryApplyWithOutcome("success", out AutoAssemblerPatch? patch);
		patch!.Dispose();

		Assert.True(outcome.DisableInfo!.HasCCodeSymbolList);
		Assert.Equal(AutoAssemblerDisableInfoSnapshotStatus.Complete, outcome.DisableInfo.Status);
		Assert.False(FakeHost.IsDestroyed(L, ccode));
		Assert.Equal(destroyed, FakeHost.DestroyedCount(L));
		EngineTest.Run(L, "assert(symbol_list_register_calls == 0 and symbol_list_unregister_calls == 0)"u8);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void Snapshot_restores_the_stack_and_leaves_the_table_unchanged()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		using LuaFrame frame = new(L);
		EngineTest.Run(L, "return { allocs = { x = { address = 1, size = 2 } }, symbols = { x = 1 } }"u8, 1);
		int top = L.Top;

		AutoAssemblerDisableInfoSnapshot snapshot =
			AutoAssemblerDisableInfoSnapshot.Read(L, -1, AutoAssemblerOptions.Default);

		Assert.Equal(top, L.Top);
		Assert.Equal(AutoAssemblerDisableInfoSnapshotStatus.Complete, snapshot.Status);
		Assert.Single(snapshot.Allocations);
		Assert.Single(snapshot.Symbols);
	}
}
