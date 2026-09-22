using CheatEngine.SDK.Lua.Interop.Tests.Support;
using CheatEngine.SDK.Lua.Interop.Types;
using CheatEngine.SDK.Tests.Shared.NativeLua;

using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

namespace CheatEngine.SDK.Lua.Interop.Tests.RoundTrips;

[Trait("Category", "NativeLua")]
public sealed unsafe class CoroutineTests
{
	[Fact]
	public void Resume_runs_to_the_yield_then_to_the_end()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		lua_State* L = state.L;
		lua_State* coroutine = lua_newthread(L);
		Assert.Equal(LUA_OK, LuaTest.Load(coroutine, "local received = coroutine.yield(10)\nreturn received + 1"u8));

		int first = lua_resume(coroutine, L, 0);

		Assert.Equal(LUA_YIELD, first);
		Assert.Equal(LUA_YIELD, lua_status(coroutine));
		Assert.Equal(10, lua_tointeger(coroutine, -1));

		lua_pop(coroutine, 1);
		lua_pushinteger(coroutine, 41);
		int second = lua_resume(coroutine, L, 1);

		Assert.Equal(LUA_OK, second);
		Assert.Equal(LUA_OK, lua_status(coroutine));
		Assert.Equal(42, lua_tointeger(coroutine, -1));
	}

	[Fact]
	public void Resume_returns_the_error_status_instead_of_raising()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new();
		lua_State* L = state.L;
		lua_State* coroutine = lua_newthread(L);
		Assert.Equal(LUA_OK, LuaTest.Load(coroutine, "error('inside coroutine')"u8));

		int status = lua_resume(coroutine, L, 0);

		Assert.Equal(LUA_ERRRUN, status);
		Assert.Equal("test:1: inside coroutine", LuaTest.ReadString(coroutine, -1));
		Assert.Equal(LUA_ERRRUN, lua_status(coroutine));
	}

	[Fact]
	public void Main_thread_is_not_yieldable()
	{
		LuaTest.RequireNativeLua();
		using NativeLuaState state = new(false);

		Assert.Equal(0, lua_isyieldable(state.L));
	}
}
