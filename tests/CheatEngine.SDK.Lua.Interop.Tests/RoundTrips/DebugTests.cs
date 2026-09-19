using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CheatEngine.SDK.Lua.Interop.Tests.Support;
using CheatEngine.SDK.Lua.Interop.Types;
using CheatEngine.SDK.Tests.Shared.NativeLua;
using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

namespace CheatEngine.SDK.Lua.Interop.Tests.RoundTrips;

/// <summary>
///     The introspection half of the debug API: locals and upvalues. <c>lua_getlocal</c>/<c>lua_setlocal</c> and
///     <c>lua_getupvalue</c>/<c>lua_setupvalue</c> share one slot type per pair, so only behaviour can tell a forwarder
///     that is wired to its sibling; the indices below are chosen so that swapped arguments change the outcome too.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed unsafe class DebugTests
{
    [Fact]
    public void Getupvalue_and_setupvalue_name_read_and_replace_an_upvalue_of_a_lua_closure()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;

        // The nil keeps the closure off index 1: (funcindex 2, n 1) and (funcindex 1, n 2) are different questions.
        lua_pushnil(L);
        LuaTest.Run(L, "local counter = 5\nreturn function() counter = counter + 1 return counter end"u8, 1);

        var readName = lua_getupvalue(L, 2, 1);
        Assert.Equal("counter", LuaTest.ReadCString(readName));
        Assert.Equal(3, lua_gettop(L));
        Assert.Equal(5, lua_tointeger(L, -1));
        lua_pop(L, 1);

        Assert.True(lua_getupvalue(L, 2, 2) is null);
        Assert.Equal(2, lua_gettop(L));

        lua_pushinteger(L, 40);
        var writtenName = lua_setupvalue(L, 2, 1);
        Assert.Equal("counter", LuaTest.ReadCString(writtenName));
        Assert.Equal(2, lua_gettop(L));

        // Out of range: null, and the value stays on the stack.
        lua_pushinteger(L, 0);
        Assert.True(lua_setupvalue(L, 2, 2) is null);
        Assert.Equal(3, lua_gettop(L));
        lua_pop(L, 1);

        lua_pushvalue(L, 2);
        Assert.Equal(LUA_OK, lua_pcall(L, 0, 1, 0));
        Assert.Equal(41, lua_tointeger(L, -1));
    }

    [Fact]
    public void Upvalueid_and_upvaluejoin_make_two_closures_share_an_upvalue()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;
        LuaTest.Run(L,
            "local a, b, c = 1, 2, 3\nlocal function first() return a, b end\nlocal function second() return c end\nreturn first, second"u8,
            2);

        var ownedBySecond = lua_upvalueid(L, 2, 1);
        Assert.True(ownedBySecond is not null);
        Assert.True(lua_upvalueid(L, 1, 1) != ownedBySecond);
        Assert.True(lua_upvalueid(L, 1, 2) != ownedBySecond);

        // Upvalue 2 of the first closure (b) now refers to upvalue 1 of the second (c). Any permutation of
        // (1, 2, 2, 1) that is not the declared order leaves "first" returning 1, 2.
        lua_upvaluejoin(L, 1, 2, 2, 1);

        Assert.True(lua_upvalueid(L, 1, 2) == ownedBySecond);
        Assert.True(lua_upvalueid(L, 1, 1) != ownedBySecond);
        lua_pushvalue(L, 1);
        Assert.Equal(LUA_OK, lua_pcall(L, 0, 2, 0));
        Assert.Equal(1, lua_tointeger(L, -2));
        Assert.Equal(3, lua_tointeger(L, -1));
    }

    [Fact]
    public void Getlocal_and_setlocal_reach_the_locals_of_the_lua_caller_of_a_callback()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;

        fixed (byte* name = "touchLocals"u8)
        {
            lua_register(L, name, &TouchCallerLocals);
        }

        // "value", "readName" and "writtenName" only become locals after the call: during it the caller has three.
        LuaTest.Run(L,
            "local function caller(first, second)\n  local third = 30\n  local value, readName, writtenName = touchLocals()\n  return value, readName, writtenName, third\nend\nreturn caller(10, 20)"u8,
            LUA_MULTRET);

        Assert.Equal(4, lua_gettop(L));
        Assert.Equal(20, lua_tointeger(L, 1));
        Assert.Equal("second", LuaTest.ReadString(L, 2));
        Assert.Equal("third", LuaTest.ReadString(L, 3));
        Assert.Equal(99, lua_tointeger(L, 4));
    }

    [Fact]
    public void Getlocal_without_an_activation_record_names_the_parameters_of_the_function_on_top()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;
        LuaTest.Run(L, "return function(alpha, beta) local gamma = alpha end"u8, 1);

        Assert.Equal("alpha", LuaTest.ReadCString(lua_getlocal(L, null, 1)));
        Assert.Equal("beta", LuaTest.ReadCString(lua_getlocal(L, null, 2)));
        Assert.True(lua_getlocal(L, null, 3) is null);

        // This form only names: nothing is pushed, the function stays where it was.
        Assert.Equal(1, lua_gettop(L));
        Assert.True(lua_isfunction(L, -1));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int TouchCallerLocals(lua_State* L)
    {
        lua_Debug record = default;

        // Level 0 is this C function, level 1 the Lua function that called it.
        if (lua_getstack(L, 1, &record) == 0) return 0;

        // Pushes the value of local 2 ("second").
        var readName = lua_getlocal(L, &record, 2);
        if (readName is null) return 0;

        // Pops 99 into local 3 ("third"). A null name reaches the chunk as nil.
        lua_pushinteger(L, 99);
        var writtenName = lua_setlocal(L, &record, 3);

        _ = lua_pushstring(L, readName);
        _ = lua_pushstring(L, writtenName);
        return 3;
    }
}
