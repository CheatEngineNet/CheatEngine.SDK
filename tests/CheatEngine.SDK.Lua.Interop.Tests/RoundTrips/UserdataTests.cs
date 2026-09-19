using CheatEngine.SDK.Lua.Interop.Tests.Support;
using CheatEngine.SDK.Tests.Shared.NativeLua;
using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

namespace CheatEngine.SDK.Lua.Interop.Tests.RoundTrips;

[Trait("Category", "NativeLua")]
public sealed unsafe class UserdataTests
{
    [Fact]
    public void Newuserdata_returns_the_block_that_touserdata_reports()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;

        var block = lua_newuserdata(L, (nuint)sizeof(nint));
        *(nint*)block = 0x1234_5678;

        Assert.Equal(LUA_TUSERDATA, lua_type(L, -1));
        Assert.Equal(1, lua_isuserdata(L, -1));
        Assert.False(lua_islightuserdata(L, -1));
        Assert.True(lua_touserdata(L, -1) == block);
        Assert.Equal((nuint)sizeof(nint), lua_rawlen(L, -1));

        // The shape of a Cheat Engine object: a full userdata whose first pointer-sized field is the native object.
        Assert.Equal(0x1234_5678, *(nint*)lua_touserdata(L, -1));
    }

    [Fact]
    public void Named_metatable_identifies_a_userdata_without_raising()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;

        fixed (byte* typeName = "CheatEngine.SDK.Tests.Handle"u8)
        fixed (byte* otherName = "CheatEngine.SDK.Tests.Other"u8)
        {
            Assert.Equal(1, luaL_newmetatable(L, typeName));
            Assert.Equal(0, luaL_newmetatable(L, typeName));
            lua_pop(L, 2);

            var block = lua_newuserdata(L, 8);
            Assert.True(luaL_testudata(L, -1, typeName) is null);

            luaL_setmetatable(L, typeName);

            Assert.True(luaL_testudata(L, -1, typeName) == block);
            Assert.True(luaL_testudata(L, -1, otherName) is null);
            Assert.Equal(LUA_TTABLE, luaL_getmetatable(L, typeName));
            Assert.Equal(LUA_TNIL, luaL_getmetatable(L, otherName));
        }
    }

    [Fact]
    public void Uservalue_round_trips()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;

        _ = lua_newuserdata(L, 1);
        lua_pushliteral(L, "attached"u8);
        lua_setuservalue(L, 1);

        Assert.Equal(1, lua_gettop(L));
        Assert.Equal(LUA_TSTRING, lua_getuservalue(L, 1));
        Assert.Equal("attached", LuaTest.ReadString(L, -1));
    }
}
