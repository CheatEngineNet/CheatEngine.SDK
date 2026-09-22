using CheatEngine.SDK.Engine.Tables;
using CheatEngine.SDK.Engine.Tests.Support;
using CheatEngine.SDK.Lua.Calls;
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
}
