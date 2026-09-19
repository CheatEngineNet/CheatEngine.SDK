using CheatEngine.SDK.Lua.Interop.Tests.Support;
using CheatEngine.SDK.Tests.Shared.NativeLua;
using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

namespace CheatEngine.SDK.Lua.Interop.Tests.RoundTrips;

[Trait("Category", "NativeLua")]
public sealed unsafe class ScalarTests
{
    [Fact]
    public void Nil_round_trips()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;

        lua_pushnil(L);

        Assert.Equal(LUA_TNIL, lua_type(L, -1));
        Assert.True(lua_isnil(L, -1));
        Assert.True(lua_isnoneornil(L, -1));
        Assert.False(lua_isnone(L, -1));
        Assert.Equal(0, lua_toboolean(L, -1));
        Assert.Equal(LUA_TNONE, lua_type(L, 2));
        Assert.True(lua_isnone(L, 2));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(-5, 1)]
    public void Boolean_round_trips_as_c_truth_value(int pushed, int expected)
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;

        lua_pushboolean(L, pushed);

        Assert.Equal(LUA_TBOOLEAN, lua_type(L, -1));
        Assert.True(lua_isboolean(L, -1));
        Assert.Equal(expected, lua_toboolean(L, -1));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(42L)]
    [InlineData(-1L)]
    [InlineData(0x7FFF_FFFF_FFFFL)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void Integer_round_trips_with_all_64_bits(long value)
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;

        lua_pushinteger(L, value);

        int isNumber;
        Assert.Equal(LUA_TNUMBER, lua_type(L, -1));
        Assert.Equal(1, lua_isinteger(L, -1));
        Assert.Equal(1, lua_isnumber(L, -1));
        Assert.Equal(value, lua_tointegerx(L, -1, &isNumber));
        Assert.Equal(1, isNumber);
        Assert.Equal(value, lua_tointeger(L, -1));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.1)]
    [InlineData(-1234.5678)]
    [InlineData(1e300)]
    [InlineData(double.PositiveInfinity)]
    public void Number_round_trips_bit_exact(double value)
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;

        lua_pushnumber(L, value);

        int isNumber;
        Assert.Equal(LUA_TNUMBER, lua_type(L, -1));
        Assert.Equal(0, lua_isinteger(L, -1));
        Assert.Equal(value, lua_tonumberx(L, -1, &isNumber));
        Assert.Equal(1, isNumber);
        Assert.Equal(value, lua_tonumber(L, -1));
    }

    [Fact]
    public void Float_with_integral_value_converts_to_integer_but_fraction_does_not()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;
        int isNumber;

        lua_pushnumber(L, 3.0);
        Assert.Equal(3, lua_tointegerx(L, -1, &isNumber));
        Assert.Equal(1, isNumber);

        lua_pushnumber(L, 3.5);
        Assert.Equal(0, lua_tointegerx(L, -1, &isNumber));
        Assert.Equal(0, isNumber);
    }

    [Fact]
    public void Conversion_flag_reports_non_numbers()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;
        var isNumber = 1;

        lua_pushboolean(L, 1);

        Assert.Equal(0, lua_tointegerx(L, -1, &isNumber));
        Assert.Equal(0, isNumber);
        isNumber = 1;
        Assert.Equal(0.0, lua_tonumberx(L, -1, &isNumber));
        Assert.Equal(0, isNumber);
        Assert.Equal(0, lua_isnumber(L, -1));
    }

    [Fact]
    public void Light_userdata_round_trips_the_pointer()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;
        var pointer = (void*)unchecked((nint)0x7FFE_1234_5678_9AB0);

        lua_pushlightuserdata(L, pointer);

        Assert.Equal(LUA_TLIGHTUSERDATA, lua_type(L, -1));
        Assert.True(lua_islightuserdata(L, -1));
        Assert.Equal(1, lua_isuserdata(L, -1));
        Assert.True(lua_touserdata(L, -1) == pointer);
        Assert.Equal((nuint)0, lua_rawlen(L, -1));
    }

    [Theory]
    [InlineData(LUA_TNIL, "nil")]
    [InlineData(LUA_TBOOLEAN, "boolean")]
    [InlineData(LUA_TLIGHTUSERDATA, "userdata")]
    [InlineData(LUA_TNUMBER, "number")]
    [InlineData(LUA_TSTRING, "string")]
    [InlineData(LUA_TTABLE, "table")]
    [InlineData(LUA_TFUNCTION, "function")]
    [InlineData(LUA_TUSERDATA, "userdata")]
    [InlineData(LUA_TTHREAD, "thread")]
    [InlineData(LUA_TNONE, "no value")]
    public void Typename_names_every_tag(int tag, string expected)
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);

        Assert.Equal(expected, LuaTest.ReadCString(lua_typename(state.L, tag)));
    }

    [Fact]
    public void LuaL_typename_names_the_value_at_an_index()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;

        lua_newtable(L);

        Assert.Equal("table", LuaTest.ReadCString(luaL_typename(L, -1)));
    }
}
