using System.Text;
using CheatEngine.SDK.Lua.Interop.Tests.Support;
using CheatEngine.SDK.Tests.Shared.NativeLua;
using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

namespace CheatEngine.SDK.Lua.Interop.Tests.RoundTrips;

[Trait("Category", "NativeLua")]
public sealed unsafe class StringTests
{
    // Latin-1, an arrow and two CJK characters, then a NUL in the middle, then a 4-byte code point:
    // 1-, 2-, 3- and 4-byte UTF-8 sequences.
    private const string Tricky = "h\u00E9llo \u2192 \u4E16\u754C\0tail \U0001F600";

    [Fact]
    public void Pushlstring_round_trips_embedded_nul_and_non_ascii_utf8()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;
        var utf8 = Encoding.UTF8.GetBytes(Tricky);

        fixed (byte* bytes = utf8)
        {
            var interned = lua_pushlstring(L, bytes, (nuint)utf8.Length);
            Assert.True(interned is not null && interned != bytes);
        }

        nuint length;
        var read = lua_tolstring(L, -1, &length);

        Assert.Equal(LUA_TSTRING, lua_type(L, -1));
        Assert.Equal((nuint)utf8.Length, length);
        Assert.Equal((nuint)utf8.Length, lua_rawlen(L, -1));
        Assert.True(new ReadOnlySpan<byte>(read, (int)length).SequenceEqual(utf8));
        Assert.Equal(0, read[length]);
        Assert.Equal(Tricky, LuaTest.ReadString(L, -1));
    }

    [Fact]
    public void Lua_code_sees_the_same_bytes()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new();
        var L = state.L;
        var utf8 = Encoding.UTF8.GetBytes(Tricky);

        LuaTest.Run(L, "return function(s) return #s, s:byte(2), s:byte(3), utf8.len(s) end"u8, 1);
        fixed (byte* bytes = utf8)
        {
            _ = lua_pushlstring(L, bytes, (nuint)utf8.Length);
        }

        Assert.Equal(LUA_OK, lua_pcall(L, 1, 4, 0));
        Assert.Equal(utf8.Length, lua_tointeger(L, -4));
        Assert.Equal(0xC3, lua_tointeger(L, -3));
        Assert.Equal(0xA9, lua_tointeger(L, -2));
        Assert.Equal(Tricky.EnumerateRunes().Count(), lua_tointeger(L, -1));
    }

    [Fact]
    public void Pushstring_stops_at_the_first_nul_and_maps_null_to_nil()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;

        fixed (byte* text = "abc\0def"u8)
        {
            _ = lua_pushstring(L, text);
        }

        Assert.True(lua_pushstring(L, null) is null);

        Assert.True(lua_isnil(L, -1));
        Assert.Equal("abc", LuaTest.ReadString(L, -2));
        Assert.Equal("abc", LuaTest.ReadCString(lua_tostring(L, -2)));
    }

    [Fact]
    public void Pushliteral_takes_a_utf8_literal_including_the_empty_one()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;

        lua_pushliteral(L, "soExactValue"u8);
        lua_pushliteral(L, ""u8);
        lua_pushliteral(L, default);

        Assert.Equal("soExactValue", LuaTest.ReadString(L, -3));
        Assert.Equal(string.Empty, LuaTest.ReadString(L, -2));
        Assert.Equal(string.Empty, LuaTest.ReadString(L, -1));
        Assert.Equal(LUA_TSTRING, lua_type(L, -1));
    }

    [Fact]
    public void Tolstring_converts_a_number_in_place_and_returns_null_for_other_types()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;

        lua_pushinteger(L, 1234);
        lua_newtable(L);

        Assert.Equal(1, lua_isstring(L, 1));
        Assert.Equal("1234", LuaTest.ReadString(L, 1));
        Assert.Equal(LUA_TSTRING, lua_type(L, 1));
        Assert.True(lua_tolstring(L, 2, null) is null);
        Assert.Equal(0, lua_isstring(L, 2));
    }

    [Fact]
    public void Numeric_string_converts_without_changing_the_slot()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;
        int isNumber;

        lua_pushliteral(L, "12.5"u8);

        Assert.Equal(1, lua_isnumber(L, -1));
        Assert.Equal(12.5, lua_tonumberx(L, -1, &isNumber));
        Assert.Equal(1, isNumber);
        Assert.Equal(0, lua_isinteger(L, -1));
        Assert.Equal(LUA_TSTRING, lua_type(L, -1));
    }

    [Fact]
    public void LuaL_tolstring_pushes_a_printable_copy_and_leaves_the_value_alone()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;

        lua_pushboolean(L, 1);
        nuint length;
        var text = luaL_tolstring(L, 1, &length);

        Assert.Equal(2, lua_gettop(L));
        Assert.Equal((nuint)4, length);
        Assert.Equal("true", LuaTest.ReadCString(text));
        Assert.Equal(LUA_TBOOLEAN, lua_type(L, 1));
    }
}
