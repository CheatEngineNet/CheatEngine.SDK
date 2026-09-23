using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Tables;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Engine.Tests.Tables;

/// <summary>Fixture-backed contracts for table-file bindings without any managed file-system policy.</summary>
[Trait("Category", "NativeLua")]
public sealed class CheatTableFilesTests
{
	[Fact]
	public void TryLoad_and_try_save_forward_opaque_path_and_merge_arguments()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		EngineTest.Run(L, """
		                  loaded = nil
		                  saved = nil
		                  loadTable = function(path, merge) loaded = { path = path, merge = merge } end
		                  saveTable = function(path) saved = path end
		                  """u8);

		int top = L.Top;
		LuaOperationStatus load = CheatTableFiles.TryLoad("../unrestricted.ct", true);

		Assert.Equal(LuaOperationStatusKind.Success, load.Kind);
		Assert.Equal(top, L.Top);
		EngineTest.Run(L, "assert(loaded.path == '../unrestricted.ct' and loaded.merge)"u8);
		Assert.Equal(top, L.Top);

		LuaOperationStatus save = CheatTableFiles.TrySave("C:/profiles/current.ct");

		Assert.Equal(LuaOperationStatusKind.Success, save.Kind);
		Assert.Equal(top, L.Top);
		EngineTest.Run(L, "assert(saved == 'C:/profiles/current.ct')"u8);
		Assert.Equal(top, L.Top);
	}

	[Fact]
	public void Table_file_calls_distinguish_missing_globals_and_lua_failures()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		int top = L.Top;

		Assert.Equal(LuaOperationStatusKind.GlobalUnavailable, CheatTableFiles.TryLoad("missing.ct", false).Kind);
		Assert.Equal(LuaOperationStatusKind.GlobalUnavailable, CheatTableFiles.TrySave("missing.ct").Kind);
		Assert.Equal(top, L.Top);

		EngineTest.Run(L, """
		                  loadTable = function() error('load rejected') end
		                  saveTable = function() error('save rejected') end
		                  """u8);

		Assert.Equal(LuaOperationStatusKind.LuaFailure, CheatTableFiles.TryLoad("broken.ct", false).Kind);
		Assert.Equal(top, L.Top);
		Assert.Equal(LuaOperationStatusKind.LuaFailure, CheatTableFiles.TrySave("broken.ct").Kind);
		Assert.Equal(top, L.Top);
	}

	[Fact]
	public void TryLoad_passes_exactly_the_path_and_merge_arguments()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		EngineTest.Run(L, """
		                  load_arguments = -1
		                  loadTable = function(...)
		                    load_arguments = select("#", ...)
		                    load_path, load_merge = ...
		                  end
		                  """u8);

		LuaOperationStatus merged = CheatTableFiles.TryLoad("with scripts.ct", true);
		EngineTest.Run(L, "assert(load_arguments == 2 and load_path == 'with scripts.ct' and load_merge == true)"u8);
		LuaOperationStatus replaced = CheatTableFiles.TryLoad("with scripts.ct", false);
		EngineTest.Run(L, "assert(load_arguments == 2 and load_merge == false)"u8);

		Assert.True(merged.IsSuccess);
		Assert.True(replaced.IsSuccess);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void TryLoad_marks_address_list_mutations_as_refused_while_it_runs()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		List<bool> observed = [];
		MemoryRecordMutationOutcome refused = default;
		using (FakeHost.InstallReentrantHook(L, "table_script", () =>
			   {
				   observed.Add(CheatTableFiles.IsLoadInProgressOnCurrentThread);
				   refused = AddressListMutations.Delete(new MemoryRecordId(1));
			   }))
		{
			EngineTest.Run(L, "loadTable = function(path, merge) table_script() end"u8);

			Assert.False(CheatTableFiles.IsLoadInProgressOnCurrentThread);
			Assert.True(CheatTableFiles.TryLoad("scripted.ct", false).IsSuccess);
		}

		Assert.Null(FakeHost.TakeReentrantHookFailure());
		Assert.Equal([true], observed);
		Assert.Equal(MemoryRecordMutationProblem.TableLoadInProgress, refused.Problem);
		Assert.Equal(MemoryRecordMutationEffect.NotAttempted, refused.Effect);
		Assert.False(CheatTableFiles.IsLoadInProgressOnCurrentThread);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void TryLoad_clears_the_load_scope_after_a_lua_failure()
	{
		EngineTest.RequireNativeLua();
		using NativeLuaState state = new();
		using HostScope scope = new(state);
		LuaState L = scope.State;
		EngineTest.Run(L, "loadTable = function() error('corrupt table') end"u8);

		LuaOperationStatus status = CheatTableFiles.TryLoad("corrupt.ct", false);

		Assert.Equal(LuaOperationStatusKind.LuaFailure, status.Kind);
		Assert.False(CheatTableFiles.IsLoadInProgressOnCurrentThread);
		Assert.Equal(MemoryRecordMutationProblem.GlobalUnavailable,
			AddressListMutations.Delete(new MemoryRecordId(1)).Problem);
		Assert.Equal(0, L.Top);
	}

	[Fact]
	public void TryLoad_clears_the_load_scope_when_the_runtime_is_detached()
	{
		LuaRuntime.Detach();

		Assert.Throws<InvalidOperationException>(() => CheatTableFiles.TryLoad("any.ct", false));

		Assert.False(CheatTableFiles.IsLoadInProgressOnCurrentThread);
	}
}
