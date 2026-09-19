using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CheatEngine.SDK.Lua.Interop.Tests.Support;
using CheatEngine.SDK.Lua.Interop.Types;
using CheatEngine.SDK.Tests.Shared.NativeLua;
using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

namespace CheatEngine.SDK.Lua.Interop.Tests.RoundTrips;

[Trait("Category", "NativeLua")]
public sealed unsafe class StateTests
{
    [Fact]
    public void NewState_and_close_round_trip()
    {
        LuaTest.RequireNativeLua();

        var L = luaL_newstate();
        Assert.True(L is not null);
        try
        {
            Assert.Equal(0, lua_gettop(L));
            Assert.Equal(LUA_OK, lua_status(L));
        }
        finally
        {
            lua_close(L);
        }
    }

    [Fact]
    public void Version_is_5_3_for_a_state_and_for_the_library()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);

        Assert.Equal(LUA_VERSION_NUM, *lua_version(state.L));
        Assert.Equal(LUA_VERSION_NUM, *lua_version(null));
    }

    [Fact]
    public void Registry_index_of_the_library_matches_the_managed_constant()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;

        // With a different LUAI_MAXSTACK in the native build, -1001000 would not be the registry (or not even valid).
        Assert.Equal(LUA_TTABLE, lua_type(L, LUA_REGISTRYINDEX));
        Assert.Equal(LUA_TTHREAD, lua_rawgeti(L, LUA_REGISTRYINDEX, LUA_RIDX_MAINTHREAD));
        Assert.True(lua_tothread(L, -1) == L);
        Assert.Equal(LUA_TTABLE, lua_rawgeti(L, LUA_REGISTRYINDEX, LUA_RIDX_GLOBALS));
        Assert.Equal(LUA_TTABLE, lua_pushglobaltable(L));
        Assert.Equal(1, lua_rawequal(L, -1, -2));
    }

    [Fact]
    public void NewThread_shares_globals_and_moves_values_with_xmove()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;

        var thread = lua_newthread(L);

        Assert.True(thread is not null && thread != L);
        Assert.True(lua_isthread(L, -1));
        Assert.True(lua_tothread(L, -1) == thread);
        Assert.Equal(1, lua_pushthread(L));
        Assert.Equal(0, lua_pushthread(thread));
        lua_pop(L, 1);
        lua_pop(thread, 1);

        lua_pushinteger(L, 7);
        lua_pushinteger(L, 8);
        lua_xmove(L, thread, 2);

        Assert.Equal(1, lua_gettop(L));
        Assert.Equal(2, lua_gettop(thread));
        Assert.Equal(8, lua_tointeger(thread, -1));
    }

    [Fact]
    public void NewState_uses_the_managed_allocator_it_is_given()
    {
        LuaTest.RequireNativeLua();
        long allocations = 0;

        var L = lua_newstate(&CountingAllocator, &allocations);
        Assert.True(L is not null);
        try
        {
            void* userData;
            Assert.True(lua_getallocf(L, &userData) is not null);
            Assert.True(userData == &allocations);

            var before = allocations;
            lua_createtable(L, 16, 16);
            Assert.True(allocations > before);
        }
        finally
        {
            lua_close(L);
        }
    }

    [Fact]
    public void Setallocf_replaces_the_allocator_that_getallocf_reports()
    {
        LuaTest.RequireNativeLua();
        long first = 0;
        long second = 0;

        var L = lua_newstate(&CountingAllocator, &first);
        Assert.True(L is not null);
        try
        {
            // Same function, other opaque pointer: the new allocator can free what the old one handed out.
            var allocator = lua_getallocf(L, null);
            lua_setallocf(L, allocator, &second);

            void* userData;
            Assert.Equal((nint)allocator, (nint)lua_getallocf(L, &userData));
            Assert.True(userData == &second);

            var firstBefore = first;
            var secondBefore = second;
            lua_createtable(L, 16, 16);
            Assert.Equal(firstBefore, first);
            Assert.True(second > secondBefore);
        }
        finally
        {
            lua_close(L);
        }
    }

    [Fact]
    public void Atpanic_returns_the_previous_function()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        delegate* unmanaged[Cdecl]<lua_State*, int> mine = &Panic;

        var installedByAuxlib = lua_atpanic(state.L, mine);
        var previous = lua_atpanic(state.L, installedByAuxlib);

        Assert.True(installedByAuxlib is not null);
        Assert.Equal((nint)mine, (nint)previous);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void* CountingAllocator(void* ud, void* ptr, nuint osize, nuint nsize)
    {
        if (nsize == 0)
        {
            NativeMemory.Free(ptr);
            return null;
        }

        try
        {
            var block = NativeMemory.Realloc(ptr, nsize);
            (*(long*)ud)++;
            return block;
        }
        catch (OutOfMemoryException)
        {
            // The allocator contract: report failure as null, never unwind into Lua.
            return null;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Panic(lua_State* L)
    {
        return 0;
    }
}
