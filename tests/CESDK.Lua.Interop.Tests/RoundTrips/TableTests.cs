using CESDK.Lua.Interop.Tests.Support;
using CESDK.Tests.Shared.NativeLua;
using static CESDK.Lua.Interop.Api.LuaApi;

namespace CESDK.Lua.Interop.Tests.RoundTrips;

[Trait("Category", "NativeLua")]
public sealed unsafe class TableTests
{
    [Fact]
    public void Setfield_and_getfield_round_trip_and_report_the_type()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;

        lua_createtable(L, 0, 2);
        fixed (byte* key = "answer"u8)
        fixed (byte* absent = "absent"u8)
        {
            lua_pushinteger(L, 42);
            lua_setfield(L, 1, key);

            Assert.Equal(1, lua_gettop(L));
            Assert.Equal(LUA_TNUMBER, lua_getfield(L, 1, key));
            Assert.Equal(42, lua_tointeger(L, -1));
            Assert.Equal(LUA_TNIL, lua_getfield(L, 1, absent));
        }
    }

    [Fact]
    public void Settable_and_gettable_use_the_key_on_the_stack()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;

        lua_newtable(L);
        lua_pushnumber(L, 1.5);
        lua_pushliteral(L, "value"u8);
        lua_settable(L, 1);

        lua_pushnumber(L, 1.5);
        Assert.Equal(LUA_TSTRING, lua_gettable(L, 1));
        Assert.Equal("value", LuaTest.ReadString(L, -1));
        Assert.Equal(2, lua_gettop(L));
    }

    [Fact]
    public void Seti_geti_and_raw_variants_agree_on_a_plain_table()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;

        lua_createtable(L, 3, 0);
        lua_pushinteger(L, 100);
        lua_seti(L, 1, 1);
        lua_pushinteger(L, 200);
        lua_rawseti(L, 1, 2);
        lua_pushinteger(L, 3);
        lua_pushinteger(L, 300);
        lua_rawset(L, 1);

        Assert.Equal((nuint)3, lua_rawlen(L, 1));
        Assert.Equal(LUA_TNUMBER, lua_rawgeti(L, 1, 1));
        Assert.Equal(LUA_TNUMBER, lua_geti(L, 1, 2));
        lua_pushinteger(L, 3);
        Assert.Equal(LUA_TNUMBER, lua_rawget(L, 1));
        Assert.Equal(300, lua_tointeger(L, -1));
        Assert.Equal(200, lua_tointeger(L, -2));
        Assert.Equal(100, lua_tointeger(L, -3));
    }

    [Fact]
    public void Rawsetp_and_rawgetp_key_by_pointer_identity()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;
        var anchor = 0;

        lua_newtable(L);
        lua_pushliteral(L, "by pointer"u8);
        lua_rawsetp(L, 1, &anchor);

        Assert.Equal(LUA_TSTRING, lua_rawgetp(L, 1, &anchor));
        Assert.Equal(LUA_TNIL, lua_rawgetp(L, 1, (byte*)&anchor + 1));
    }

    [Fact]
    public void Setglobal_and_getglobal_round_trip()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;

        fixed (byte* name = "cesdkTestGlobal"u8)
        {
            lua_pushinteger(L, 7);
            lua_setglobal(L, name);

            Assert.Equal(0, lua_gettop(L));
            Assert.Equal(LUA_TNUMBER, lua_getglobal(L, name));
            Assert.Equal(7, lua_tointeger(L, -1));
        }
    }

    [Fact]
    public void Next_visits_every_pair_once_and_leaves_the_stack_balanced()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;
        LuaTest.Run(L, "return { 10, 20, 30, x = 40, y = 50 }"u8, 1);
        var table = lua_absindex(L, -1);
        var top = lua_gettop(L);

        long valueSum = 0;
        var integerKeys = 0;
        var stringKeys = 0;
        lua_pushnil(L);
        while (lua_next(L, table) != 0)
        {
            // Key at -2, value at -1. The key type is tested, never converted: tolstring would rewrite the slot.
            valueSum += lua_tointeger(L, -1);
            integerKeys += lua_isinteger(L, -2);
            stringKeys += lua_type(L, -2) == LUA_TSTRING ? 1 : 0;
            lua_pop(L, 1);
        }

        Assert.Equal(150, valueSum);
        Assert.Equal(3, integerKeys);
        Assert.Equal(2, stringKeys);
        Assert.Equal(top, lua_gettop(L));
    }

    [Fact]
    public void Metatable_round_trips_and_drives_getfield()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;

        lua_newtable(L);
        Assert.Equal(0, lua_getmetatable(L, 1));
        Assert.Equal(1, lua_gettop(L));

        LuaTest.Run(L, "return { __index = function(t, k) return k .. '!' end }"u8, 1);
        Assert.Equal(1, lua_setmetatable(L, 1));

        Assert.Equal(1, lua_getmetatable(L, 1));
        Assert.True(lua_istable(L, -1));
        lua_pop(L, 1);
        fixed (byte* key = "hello"u8)
        fixed (byte* index = "__index"u8)
        {
            Assert.Equal(LUA_TSTRING, lua_getfield(L, 1, key));
            Assert.Equal("hello!", LuaTest.ReadString(L, -1));
            Assert.Equal(LUA_TNIL, lua_rawgetp(L, 1, key));
            Assert.Equal(LUA_TFUNCTION, luaL_getmetafield(L, 1, index));
            Assert.True(lua_isfunction(L, -1));
        }
    }

    [Fact]
    public void Getsubtable_creates_once_then_finds()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;

        lua_newtable(L);
        fixed (byte* name = "cache"u8)
        {
            Assert.Equal(0, luaL_getsubtable(L, 1, name));
            var created = lua_topointer(L, -1);
            lua_pop(L, 1);

            Assert.Equal(1, luaL_getsubtable(L, 1, name));
            Assert.True(created is not null && lua_topointer(L, -1) == created);
        }
    }
}
