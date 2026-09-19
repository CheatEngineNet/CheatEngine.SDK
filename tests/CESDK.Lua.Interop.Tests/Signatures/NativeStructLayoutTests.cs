using CESDK.Lua.Interop.Api;
using CESDK.Lua.Interop.Types;

namespace CESDK.Lua.Interop.Tests.Signatures;

/// <summary>DLL-free: layouts of the two structs that cross the boundary, for a 64-bit process.</summary>
public sealed unsafe class NativeStructLayoutTests
{
    [Fact]
    public void Lua_debug_matches_the_64_bit_c_layout()
    {
        Assert.SkipUnless(Environment.Is64BitProcess, "The expected offsets are those of a 64-bit build.");

        lua_Debug record;
        var start = (byte*)&record;

        Assert.Equal(128, sizeof(lua_Debug));
        Assert.Equal(0, (int)((byte*)&record.@event - start));
        Assert.Equal(8, (int)((byte*)&record.name - start));
        Assert.Equal(16, (int)((byte*)&record.namewhat - start));
        Assert.Equal(24, (int)((byte*)&record.what - start));
        Assert.Equal(32, (int)((byte*)&record.source - start));
        Assert.Equal(40, (int)((byte*)&record.currentline - start));
        Assert.Equal(44, (int)((byte*)&record.linedefined - start));
        Assert.Equal(48, (int)((byte*)&record.lastlinedefined - start));
        Assert.Equal(52, (int)(&record.nups - start));
        Assert.Equal(53, (int)(&record.nparams - start));
        Assert.Equal(54, (int)((byte*)&record.isvararg - start));
        Assert.Equal(55, (int)((byte*)&record.istailcall - start));
        Assert.Equal(56, (int)(record.short_src - start));
        Assert.Equal(120, (int)((byte*)&record.i_ci - start));
    }

    [Fact]
    public void Lua_debug_short_src_has_lua_idsize_bytes()
    {
        Assert.Equal(60, LuaApi.LUA_IDSIZE);
    }

    [Fact]
    public void LuaL_reg_is_two_pointers()
    {
        luaL_Reg entry;
        var start = (byte*)&entry;

        Assert.Equal(2 * sizeof(nint), sizeof(luaL_Reg));
        Assert.Equal(0, (int)((byte*)&entry.name - start));
        Assert.Equal(sizeof(nint), (int)((byte*)&entry.func - start));
    }
}
