using CESDK.Lua.Interop.Tests.Support;
using CESDK.Lua.Interop.Types;
using CESDK.Tests.Shared.NativeLua;
using static CESDK.Lua.Interop.Api.LuaApi;

namespace CESDK.Lua.Interop.Tests.RoundTrips;

[Trait("Category", "NativeLua")]
public sealed unsafe class StackTests
{
    [Fact]
    public void Settop_grows_with_nils_and_shrinks()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;

        lua_settop(L, 3);
        Assert.Equal(3, lua_gettop(L));
        Assert.True(lua_isnil(L, 3));

        lua_settop(L, -2);
        Assert.Equal(2, lua_gettop(L));

        lua_settop(L, 0);
        Assert.Equal(0, lua_gettop(L));
    }

    [Fact]
    public void Pop_drops_the_top_elements()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;
        PushIntegers(L, 1, 2, 3);

        lua_pop(L, 2);

        Assert.Equal([1], ReadIntegers(L));
    }

    [Fact]
    public void Absindex_makes_negative_indices_stable_and_keeps_pseudo_indices()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;
        PushIntegers(L, 10, 20, 30);

        Assert.Equal(3, lua_absindex(L, -1));
        Assert.Equal(1, lua_absindex(L, -3));
        Assert.Equal(2, lua_absindex(L, 2));
        Assert.Equal(LUA_REGISTRYINDEX, lua_absindex(L, LUA_REGISTRYINDEX));
    }

    [Fact]
    public void Pushvalue_and_copy_duplicate_without_moving()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;
        PushIntegers(L, 1, 2, 3);

        lua_pushvalue(L, 1);
        lua_copy(L, 2, 3);

        Assert.Equal([1, 2, 2, 1], ReadIntegers(L));
    }

    [Fact]
    public void Rotate_moves_a_segment_both_ways()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;
        PushIntegers(L, 1, 2, 3, 4);

        lua_rotate(L, 2, 1);
        Assert.Equal([1, 4, 2, 3], ReadIntegers(L));

        lua_rotate(L, 2, -1);
        Assert.Equal([1, 2, 3, 4], ReadIntegers(L));
    }

    [Fact]
    public void Insert_remove_and_replace_behave_like_the_c_macros()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;
        PushIntegers(L, 1, 2, 3, 4);

        lua_insert(L, 1);
        Assert.Equal([4, 1, 2, 3], ReadIntegers(L));

        lua_remove(L, 2);
        Assert.Equal([4, 2, 3], ReadIntegers(L));

        lua_replace(L, 1);
        Assert.Equal([3, 2], ReadIntegers(L));
    }

    [Fact]
    public void Checkstack_grants_room_and_refuses_beyond_the_limit()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;

        Assert.NotEqual(0, lua_checkstack(L, 1000));
        for (var i = 0; i < 1000; i++) lua_pushinteger(L, i);

        Assert.Equal(1000, lua_gettop(L));
        Assert.Equal(999, lua_tointeger(L, -1));
        Assert.Equal(0, lua_checkstack(L, LUAI_MAXSTACK));
    }

    private static void PushIntegers(lua_State* L, params ReadOnlySpan<long> values)
    {
        foreach (var value in values) lua_pushinteger(L, value);
    }

    private static long[] ReadIntegers(lua_State* L)
    {
        var values = new long[lua_gettop(L)];
        for (var i = 0; i < values.Length; i++) values[i] = lua_tointeger(L, i + 1);

        return values;
    }
}
