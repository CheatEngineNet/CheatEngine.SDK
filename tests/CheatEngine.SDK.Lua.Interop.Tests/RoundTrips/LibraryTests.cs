using CheatEngine.SDK.Lua.Interop.Tests.Support;
using CheatEngine.SDK.Lua.Interop.Types;
using CheatEngine.SDK.Tests.Shared.NativeLua;

using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

namespace CheatEngine.SDK.Lua.Interop.Tests.RoundTrips;

[Trait("Category", "NativeLua")]
public sealed unsafe class LibraryTests
{
	[Fact]
	public void Bare_state_has_no_libraries_and_openlibs_adds_them()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		lua_State* L = state.L;

		fixed (byte* name = "string"u8)
		{
			Assert.Equal(LUA_TNIL, lua_getglobal(L, name));
			luaL_openlibs(L);
			Assert.Equal(LUA_TTABLE, lua_getglobal(L, name));
		}
	}

	[Fact]
	public void Requiref_opens_one_library_into_a_sandboxed_state()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		lua_State* L = state.L;

		fixed (byte* baseName = "_G"u8)
		fixed (byte* stringName = LUA_STRLIBNAME)
		fixed (byte* osName = LUA_OSLIBNAME)
		{
			luaL_requiref(L, baseName, luaopen_base, 1);
			luaL_requiref(L, stringName, luaopen_string, 1);
			lua_pop(L, 2);

			LuaTest.Run(L, "return string.rep('ab', 3), type(print), os"u8, 3);

			Assert.Equal("ababab", LuaTest.ReadString(L, 1));
			Assert.Equal("function", LuaTest.ReadString(L, 2));
			Assert.True(lua_isnil(L, 3));
			Assert.Equal(LUA_TNIL, lua_getglobal(L, osName));
		}
	}

	[Fact]
	public void Every_opener_is_a_distinct_function_of_the_module()
	{
		LuaTest.RequireNativeLua();

		nint[] openers =
		[
			(nint) luaopen_base, (nint) luaopen_coroutine, (nint) luaopen_table, (nint) luaopen_io, (nint) luaopen_os,
			(nint) luaopen_string, (nint) luaopen_utf8, (nint) luaopen_math, (nint) luaopen_debug,
			(nint) luaopen_package
		];

		Assert.DoesNotContain(0, openers);
		Assert.Equal(openers.Length, openers.Distinct().Count());
	}

	[Fact]
	public void Traceback_describes_the_stack_with_the_message_first()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		lua_State* L = state.L;

		fixed (byte* message = "while testing"u8)
		{
			luaL_traceback(L, L, message, 0);
		}

		string? traceback = LuaTest.ReadString(L, -1);
		Assert.NotNull(traceback);
		Assert.StartsWith("while testing\nstack traceback:", traceback, StringComparison.Ordinal);
	}

	[Fact]
	public void Callmeta_runs_a_metamethod_by_name()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		lua_State* L = state.L;
		LuaTest.Run(L, "return setmetatable({}, { __describe = function(self) return 'described' end })"u8, 1);

		fixed (byte* present = "__describe"u8)
		fixed (byte* absent = "__missing"u8)
		{
			Assert.Equal(0, luaL_callmeta(L, 1, absent));
			Assert.Equal(1, lua_gettop(L));
			Assert.Equal(1, luaL_callmeta(L, 1, present));
			Assert.Equal("described", LuaTest.ReadString(L, -1));
		}
	}
}
