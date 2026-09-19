using CheatEngine.SDK.Lua.Interop.Types;
using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

namespace CheatEngine.SDK.Lua.Interop.Tests.Macros;

/// <summary>DLL-free: the macro equivalents that are plain arithmetic.</summary>
public sealed unsafe class PureMacroTests
{
    [Theory]
    [InlineData(1, -1001001)]
    [InlineData(2, -1001002)]
    [InlineData(255, -1001255)]
    public void Upvalueindex_counts_down_from_the_registry_index(int upvalue, int expected)
    {
        Assert.Equal(expected, lua_upvalueindex(upvalue));
    }

    [Fact]
    public void Getextraspace_is_one_pointer_in_front_of_the_state()
    {
        var state = (lua_State*)0x10000;

        Assert.Equal(sizeof(nint), LUA_EXTRASPACE);
        Assert.Equal((nint)0x10000 - sizeof(nint), (nint)lua_getextraspace(state));
    }
}
