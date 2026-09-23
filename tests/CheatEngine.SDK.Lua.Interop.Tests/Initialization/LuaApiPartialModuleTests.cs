using System.Runtime.InteropServices;

using CheatEngine.SDK.Lua.Interop.Api;
using CheatEngine.SDK.Lua.Interop.Tests.Support;
using CheatEngine.SDK.Lua.Interop.Types;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Lua.Interop.Tests.Initialization;

/// <summary>
///     Q11 with a module that is Lua 5.3 except for one export: a copy of the fixture whose <c>lua_rotate</c> name was
///     overwritten in its export table (<see cref="PartialLuaModule" />). Binding refuses it all or nothing and names
///     exactly the missing export; a table already bound stays bound to the fixture and keeps working.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed unsafe class LuaApiPartialModuleTests
{
	[Fact]
	[Trait("Qualification", "Q11")]
	public void Module_missing_one_lua_export_is_refused_naming_exactly_that_export()
	{
		LuaTest.RequireNativeLua();
		using PartialLuaModule module = PartialLuaModule.Load(NativeLuaLibrary.LibraryPath!);

		IReadOnlyList<string> missing = LuaApi.GetMissingExports(module.Handle);
		EntryPointNotFoundException exception =
			Assert.Throws<EntryPointNotFoundException>(() => LuaApi.Initialize(module.Handle));

		Assert.Equal([PartialLuaModule.RemovedExport], missing);
		Assert.Contains($"1 of {LuaApi.GetMissingExports(NativeLibrary.GetMainProgramHandle()).Count} required exports",
			exception.Message, StringComparison.Ordinal);
		Assert.EndsWith($"({PartialLuaModule.RemovedExport}).", exception.Message, StringComparison.Ordinal);
		Assert.False(NativeLibrary.TryGetExport(module.Handle, PartialLuaModule.RemovedExport, out _));
		Assert.True(NativeLibrary.TryGetExport(module.Handle, "lua_settop", out _));
		Assert.True(NativeLibrary.TryGetExport(module.Handle, "lua_setallocf", out _));
		Assert.NotEqual(NativeLuaLibrary.Handle, module.Handle);
	}

	[Fact]
	[Trait("Qualification", "Q11")]
	public void TryInitialize_with_a_partial_module_leaves_the_table_unchanged()
	{
		LuaTest.RequireNativeLua();
		nint boundBefore = LuaApi.ModuleHandle;
		using PartialLuaModule module = PartialLuaModule.Load(NativeLuaLibrary.LibraryPath!);

		bool bound = LuaApi.TryInitialize(module.Handle, out string? failure);

		Assert.False(bound);
		Assert.Contains($"({PartialLuaModule.RemovedExport})", failure, StringComparison.Ordinal);
		Assert.Equal(boundBefore, LuaApi.ModuleHandle);
		Assert.Equal(NativeLuaLibrary.Handle, LuaApi.ModuleHandle);

		// The slot of the export the partial module lacks still reaches the fixture's lua_rotate.
		using NativeLuaState state = new(false);
		lua_State* L = state.L;
		LuaApi.lua_pushinteger(L, 1);
		LuaApi.lua_pushinteger(L, 2);
		LuaApi.lua_rotate(L, 1, 1);
		Assert.Equal(2, LuaApi.lua_tointegerx(L, 1, null));
		Assert.Equal(1, LuaApi.lua_tointegerx(L, 2, null));
	}
}
