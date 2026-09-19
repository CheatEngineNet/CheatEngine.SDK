using CheatEngine.SDK.Lua.Interop.Tests.Support;
using CheatEngine.SDK.Tests.Shared.NativeLua;
using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

namespace CheatEngine.SDK.Lua.Interop.Tests.RoundTrips;

[Trait("Category", "NativeLua")]
public sealed unsafe class ReferenceTests
{
    [Fact]
    public void Ref_pops_the_value_and_rawgeti_brings_it_back()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;

        lua_newtable(L);
        var identity = lua_topointer(L, -1);
        var reference = luaL_ref(L, LUA_REGISTRYINDEX);

        Assert.Equal(0, lua_gettop(L));
        Assert.True(reference > LUA_RIDX_LAST);
        Assert.Equal(LUA_TTABLE, lua_rawgeti(L, LUA_REGISTRYINDEX, reference));
        Assert.True(lua_topointer(L, -1) == identity);

        luaL_unref(L, LUA_REGISTRYINDEX, reference);
        Assert.Equal(1, lua_gettop(L));
    }

    [Fact]
    public void Ref_of_nil_is_refnil_and_stores_nothing()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;

        lua_pushnil(L);
        var reference = luaL_ref(L, LUA_REGISTRYINDEX);

        Assert.Equal(LUA_REFNIL, reference);
        Assert.Equal(0, lua_gettop(L));
    }

    [Fact]
    public void Unref_ignores_the_marker_values_and_frees_the_slot_for_reuse()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;

        luaL_unref(L, LUA_REGISTRYINDEX, LUA_NOREF);
        luaL_unref(L, LUA_REGISTRYINDEX, LUA_REFNIL);

        lua_pushinteger(L, 1);
        var first = luaL_ref(L, LUA_REGISTRYINDEX);
        luaL_unref(L, LUA_REGISTRYINDEX, first);
        lua_pushinteger(L, 2);
        var second = luaL_ref(L, LUA_REGISTRYINDEX);

        Assert.Equal(first, second);
        Assert.Equal(LUA_TNUMBER, lua_rawgeti(L, LUA_REGISTRYINDEX, second));
        Assert.Equal(2, lua_tointeger(L, -1));
    }

    [Fact]
    public void Reference_is_shared_by_the_threads_of_one_state()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;
        var thread = lua_newthread(L);

        lua_pushliteral(L, "shared"u8);
        var reference = luaL_ref(L, LUA_REGISTRYINDEX);

        Assert.Equal(LUA_TSTRING, lua_rawgeti(thread, LUA_REGISTRYINDEX, reference));
        Assert.Equal("shared", LuaTest.ReadString(thread, -1));
    }
}
