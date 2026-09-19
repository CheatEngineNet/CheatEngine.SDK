using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CheatEngine.SDK.Lua.Interop.Tests.Support;
using CheatEngine.SDK.Lua.Interop.Types;
using CheatEngine.SDK.Tests.Shared.NativeLua;
using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

namespace CheatEngine.SDK.Lua.Interop.Tests.RoundTrips;

/// <summary>
///     Lua calling managed code. Every callback is a static <c>[UnmanagedCallersOnly]</c> cdecl method whose address is
///     taken with <c>&amp;</c>; none of them can throw, and none calls an API that can raise.
/// </summary>
[Trait("Category", "NativeLua")]
public sealed unsafe class CallbackTests
{
    private static int s_hookCalls;
    private static int s_hookEvent = -1;

    [Fact]
    public void Managed_cfunction_registered_with_pushcclosure_is_called_from_a_lua_chunk()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;

        fixed (byte* name = "managedAdd"u8)
        {
            lua_pushcclosure(L, &Add, 0);
            lua_setglobal(L, name);
        }

        LuaTest.Run(L, "return managedAdd(40, 2), managedAdd(0.5, 0.25), managedAdd()"u8, 3);

        Assert.Equal(42, lua_tonumber(L, 1));
        Assert.Equal(0.75, lua_tonumber(L, 2));
        Assert.True(lua_isnil(L, 3));
    }

    [Fact]
    public void Pushed_cfunction_is_recognised_and_gives_its_address_back()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;
        delegate* unmanaged[Cdecl]<lua_State*, int> function = &Add;

        lua_pushcfunction(L, function);

        Assert.True(lua_isfunction(L, -1));
        Assert.Equal(1, lua_iscfunction(L, -1));
        Assert.Equal((nint)function, (nint)lua_tocfunction(L, -1));

        LuaTest.Run(L, "return function() end"u8, 1);
        Assert.Equal(0, lua_iscfunction(L, -1));
        Assert.True(lua_tocfunction(L, -1) is null);
    }

    [Fact]
    public void Closure_upvalues_are_reached_through_upvalueindex_and_stay_private_to_the_closure()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;

        fixed (byte* name = "nextTicket"u8)
        {
            lua_pushinteger(L, 100);
            lua_pushcclosure(L, &Counter, 1);
            lua_setglobal(L, name);
        }

        LuaTest.Run(L, "return nextTicket(), nextTicket(), nextTicket()"u8, 3);

        Assert.Equal(101, lua_tointeger(L, 1));
        Assert.Equal(102, lua_tointeger(L, 2));
        Assert.Equal(103, lua_tointeger(L, 3));
    }

    [Fact]
    public void Register_and_setfuncs_install_managed_functions()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;

        fixed (byte* global = "add"u8)
        fixed (byte* library = "lib"u8)
        fixed (byte* first = "add"u8)
        fixed (byte* second = "ticket"u8)
        {
            var functions = stackalloc luaL_Reg[3];
            functions[0] = new luaL_Reg { name = first, func = &Add };
            functions[1] = new luaL_Reg { name = second, func = &Counter };
            functions[2] = default;

            lua_register(L, global, &Add);
            lua_newtable(L);
            lua_pushinteger(L, 0);
            luaL_setfuncs(L, functions, 1);
            lua_setglobal(L, library);
        }

        LuaTest.Run(L, "return add(1, 2), lib.add(3, 4), lib.ticket(), lib.ticket()"u8, 4);

        Assert.Equal(3, lua_tonumber(L, 1));
        Assert.Equal(7, lua_tonumber(L, 2));
        Assert.Equal(1, lua_tointeger(L, 3));
        Assert.Equal(2, lua_tointeger(L, 4));
    }

    [Fact]
    public void Callback_receives_the_state_that_runs_it()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new();
        var L = state.L;

        fixed (byte* name = "isMainThread"u8)
        {
            lua_register(L, name, &IsMainThread);
        }

        LuaTest.Run(L, "return isMainThread(), coroutine.wrap(isMainThread)()"u8, 2);

        // Inside a coroutine the callback gets the coroutine's lua_State, not the one the chunk was started on.
        Assert.Equal(1, lua_toboolean(L, 1));
        Assert.Equal(0, lua_toboolean(L, 2));
    }

    [Fact]
    public void Getstack_and_getinfo_describe_the_lua_caller_of_a_callback()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;

        fixed (byte* name = "whereAmI"u8)
        {
            lua_register(L, name, &DescribeCaller);
        }

        // Not "return whereAmI()": a tail call would be a different question about which frame is level 1.
        LuaTest.Run(L,
            "local function caller(a, b)\n  local l, s, w, d, p = whereAmI()\n  return l, s, w, d, p\nend\nreturn caller()"u8,
            LUA_MULTRET);

        Assert.Equal(5, lua_gettop(L));
        Assert.Equal(2, lua_tointeger(L, 1));
        Assert.Equal("test", LuaTest.ReadString(L, 2));
        Assert.Equal("Lua", LuaTest.ReadString(L, 3));
        Assert.Equal(1, lua_tointeger(L, 4));
        Assert.Equal(2, lua_tointeger(L, 5));
    }

    [Fact]
    public void Getinfo_with_the_function_on_the_stack_fills_the_source_fields()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;
        LuaTest.Run(L, "\n\nreturn function(a, b, c)\n  return a\nend"u8, 1);

        lua_Debug record = default;
        fixed (byte* what = ">Su"u8)
        {
            Assert.NotEqual(0, lua_getinfo(L, what, &record));
        }

        Assert.Equal(0, lua_gettop(L));
        Assert.Equal(3, record.linedefined);
        Assert.Equal(5, record.lastlinedefined);
        Assert.Equal(3, record.nparams);
        Assert.Equal(0, record.isvararg);
        Assert.Equal("=test", LuaTest.ReadCString(record.source));
        Assert.Equal("test", LuaTest.ReadCString(record.short_src));
    }

    [Fact]
    public void Native_debug_record_fits_the_managed_struct()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;

        fixed (byte* name = "recordFits"u8)
        {
            lua_register(L, name, &DebugRecordStaysInBounds);
        }

        LuaTest.Run(L, "return recordFits()"u8, 3);

        // A library built with LUA_IDSIZE above 64 would place i_ci (and the tail of short_src) beyond 128 bytes; one
        // built with 56 or less would write i_ci below offset 120. Values from 57 to 64 share the managed layout, so
        // this proves that the record fits, not that the native value is exactly 60.
        Assert.Equal(1, lua_toboolean(L, 1));
        Assert.Equal(1, lua_toboolean(L, 2));
        Assert.Equal("[C]", LuaTest.ReadString(L, 3));
    }

    [Fact]
    public void Count_hook_runs_a_managed_function_inside_the_interpreter()
    {
        LuaTest.RequireNativeLua();
        using NativeLuaState state = new(false);
        var L = state.L;
        delegate* unmanaged[Cdecl]<lua_State*, lua_Debug*, void> hook = &CountHook;

        lua_sethook(L, hook, LUA_MASKCOUNT, 10);
        Assert.Equal((nint)hook, (nint)lua_gethook(L));
        Assert.Equal(LUA_MASKCOUNT, lua_gethookmask(L));
        Assert.Equal(10, lua_gethookcount(L));

        LuaTest.Run(L, "local n = 0 for i = 1, 1000 do n = n + i end return n"u8, 1);
        lua_sethook(L, null, 0, 0);

        Assert.Equal(500500, lua_tointeger(L, -1));
        Assert.True(Volatile.Read(ref s_hookCalls) > 10);
        Assert.Equal(LUA_HOOKCOUNT, Volatile.Read(ref s_hookEvent));
        Assert.True(lua_gethook(L) is null);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Add(lua_State* L)
    {
        // Argument validation by inspection: a managed callback must not use luaL_check*, which raises.
        int firstIsNumber;
        int secondIsNumber;
        var first = lua_tonumberx(L, 1, &firstIsNumber);
        var second = lua_tonumberx(L, 2, &secondIsNumber);
        if (firstIsNumber == 0 || secondIsNumber == 0)
        {
            lua_pushnil(L);
            return 1;
        }

        lua_pushnumber(L, first + second);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Counter(lua_State* L)
    {
        var next = lua_tointegerx(L, lua_upvalueindex(1), null) + 1;
        lua_pushinteger(L, next);
        lua_copy(L, -1, lua_upvalueindex(1));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int IsMainThread(lua_State* L)
    {
        var isMain = lua_pushthread(L);
        lua_settop(L, -2);
        lua_pushboolean(L, isMain);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int DescribeCaller(lua_State* L)
    {
        lua_Debug record = default;
        fixed (byte* what = "Slu"u8)
        {
            // Level 0 is this C function, level 1 the Lua function that called it.
            if (lua_getstack(L, 1, &record) == 0 || lua_getinfo(L, what, &record) == 0) return 0;
        }

        lua_pushinteger(L, record.currentline);
        _ = lua_pushstring(L, record.short_src);
        _ = lua_pushstring(L, record.what);
        lua_pushinteger(L, record.linedefined);
        lua_pushinteger(L, record.nparams);
        return 5;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int DebugRecordStaysInBounds(lua_State* L)
    {
        const int Guarded = 256;
        const byte Canary = 0xCC;
        var buffer = stackalloc byte[Guarded];
        new Span<byte>(buffer, Guarded).Fill(Canary);
        var record = (lua_Debug*)buffer;

        int found;
        fixed (byte* what = "nSltu"u8)
        {
            // Level 0 is this function: getstack writes the private i_ci field, getinfo everything else.
            found = lua_getstack(L, 0, record) != 0 && lua_getinfo(L, what, record) != 0 ? 1 : 0;
        }

        var tailUntouched =
            new ReadOnlySpan<byte>(buffer + sizeof(lua_Debug), Guarded - sizeof(lua_Debug)).IndexOfAnyExcept(Canary) <
            0;
        var callInfoWritten =
            record->i_ci is not null && (nuint)record->i_ci != unchecked((nuint)0xCCCC_CCCC_CCCC_CCCC);

        lua_pushboolean(L, found != 0 && tailUntouched ? 1 : 0);
        lua_pushboolean(L, callInfoWritten ? 1 : 0);
        _ = lua_pushstring(L, record->short_src);
        return 3;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CountHook(lua_State* L, lua_Debug* ar)
    {
        Volatile.Write(ref s_hookEvent, ar->@event);
        Interlocked.Increment(ref s_hookCalls);
    }
}
