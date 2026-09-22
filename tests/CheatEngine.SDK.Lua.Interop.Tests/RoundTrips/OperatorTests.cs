using CheatEngine.SDK.Lua.Interop.Tests.Support;
using CheatEngine.SDK.Lua.Interop.Types;
using CheatEngine.SDK.Tests.Shared.NativeLua;

using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

namespace CheatEngine.SDK.Lua.Interop.Tests.RoundTrips;

[Trait("Category", "NativeLua")]
public sealed unsafe class OperatorTests
{
	[Fact]
	public void Arith_applies_binary_and_unary_operators()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		lua_State* L = state.L;

		lua_pushinteger(L, 40);
		lua_pushinteger(L, 2);
		lua_arith(L, LUA_OPADD);
		Assert.Equal(1, lua_gettop(L));
		Assert.Equal(42, lua_tointeger(L, -1));

		lua_arith(L, LUA_OPUNM);
		Assert.Equal(1, lua_gettop(L));
		Assert.Equal(-42, lua_tointeger(L, -1));

		lua_pushinteger(L, 5);
		lua_arith(L, LUA_OPIDIV);
		Assert.Equal(-9, lua_tointeger(L, -1));
	}

	[Fact]
	public void Compare_and_rawequal_follow_lua_semantics()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		lua_State* L = state.L;

		lua_pushinteger(L, 1);
		lua_pushnumber(L, 1.0);
		lua_pushinteger(L, 2);

		Assert.Equal(1, lua_compare(L, 1, 2, LUA_OPEQ));
		Assert.Equal(1, lua_rawequal(L, 1, 2));
		Assert.Equal(1, lua_compare(L, 1, 3, LUA_OPLT));
		Assert.Equal(1, lua_compare(L, 1, 2, LUA_OPLE));
		Assert.Equal(0, lua_compare(L, 3, 1, LUA_OPLE));
		Assert.Equal(0, lua_rawequal(L, 1, 3));
		Assert.Equal(0, lua_rawequal(L, 1, 9));
	}

	[Fact]
	public void Concat_joins_strings_and_numbers()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		lua_State* L = state.L;

		lua_pushliteral(L, "vt"u8);
		lua_pushliteral(L, "Dword="u8);
		lua_pushinteger(L, 2);
		lua_concat(L, 3);

		Assert.Equal(1, lua_gettop(L));
		Assert.Equal("vtDword=2", LuaTest.ReadString(L, -1));
	}

	[Fact]
	public void Len_and_luaL_len_report_the_sequence_length()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		lua_State* L = state.L;
		LuaTest.Run(L, "return { 'a', 'b', 'c' }"u8, 1);

		lua_len(L, 1);

		Assert.Equal(3, lua_tointeger(L, -1));
		Assert.Equal(3, luaL_len(L, 1));
		Assert.Equal((nuint) 3, lua_rawlen(L, 1));
	}

	[Fact]
	public void Stringtonumber_pushes_on_success_only()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		lua_State* L = state.L;

		fixed (byte* numeral = "0x10"u8)
		fixed (byte* garbage = "10 apples"u8)
		{
			Assert.Equal((nuint) 5, lua_stringtonumber(L, numeral));
			Assert.Equal(16, lua_tointeger(L, -1));
			Assert.Equal((nuint) 0, lua_stringtonumber(L, garbage));
			Assert.Equal(1, lua_gettop(L));
		}
	}

	[Fact]
	public void Gc_reports_memory_and_collects()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);
		lua_State* L = state.L;

		Assert.Equal(1, lua_gc(L, LUA_GCISRUNNING, 0));
		Assert.True(lua_gc(L, LUA_GCCOUNT, 0) > 0);
		Assert.InRange(lua_gc(L, LUA_GCCOUNTB, 0), 0, 1023);
		Assert.Equal(0, lua_gc(L, LUA_GCCOLLECT, 0));
		Assert.Equal(0, lua_gc(L, LUA_GCSTOP, 0));
		Assert.Equal(0, lua_gc(L, LUA_GCISRUNNING, 0));
	}
}
