using static CESDK.Lua.Interop.Api.LuaApi;

namespace CESDK.Lua.Interop.Tests.Constants;

/// <summary>DLL-free: the numeric contract of lua.h / lauxlib.h / luaconf.h for a default 64-bit Lua 5.3 build.</summary>
public sealed class LuaConstantsTests
{
    [Fact]
    public void RegistryIndex_default_build_is_minus_1001000()
    {
        // Merge gate: the pseudo-index is baked into the native library at compile time.
        Assert.Equal(-1001000, LUA_REGISTRYINDEX);
        Assert.Equal(1000000, LUAI_MAXSTACK);
        Assert.Equal(LUA_REGISTRYINDEX, LUAI_FIRSTPSEUDOIDX);
    }

    [Fact]
    public void Call_and_reference_markers_match_headers()
    {
        Assert.Equal(-1, LUA_MULTRET);
        Assert.Equal(-2, LUA_NOREF);
        Assert.Equal(-1, LUA_REFNIL);
        Assert.Equal(20, LUA_MINSTACK);
        Assert.Equal(503, LUA_VERSION_NUM);
        Assert.Equal(136, LUAL_NUMSIZES);
    }

    [Fact]
    public void Registry_slots_match_headers()
    {
        Assert.Equal(1, LUA_RIDX_MAINTHREAD);
        Assert.Equal(2, LUA_RIDX_GLOBALS);
        Assert.Equal(LUA_RIDX_GLOBALS, LUA_RIDX_LAST);
    }

    [Fact]
    public void Status_codes_match_headers()
    {
        Assert.Equal(0, LUA_OK);
        Assert.Equal(1, LUA_YIELD);
        Assert.Equal(2, LUA_ERRRUN);
        Assert.Equal(3, LUA_ERRSYNTAX);
        Assert.Equal(4, LUA_ERRMEM);
        Assert.Equal(5, LUA_ERRGCMM);
        Assert.Equal(6, LUA_ERRERR);
        Assert.Equal(7, LUA_ERRFILE);
    }

    [Fact]
    public void Type_tags_match_headers()
    {
        Assert.Equal(-1, LUA_TNONE);
        Assert.Equal(0, LUA_TNIL);
        Assert.Equal(1, LUA_TBOOLEAN);
        Assert.Equal(2, LUA_TLIGHTUSERDATA);
        Assert.Equal(3, LUA_TNUMBER);
        Assert.Equal(4, LUA_TSTRING);
        Assert.Equal(5, LUA_TTABLE);
        Assert.Equal(6, LUA_TFUNCTION);
        Assert.Equal(7, LUA_TUSERDATA);
        Assert.Equal(8, LUA_TTHREAD);
        Assert.Equal(9, LUA_NUMTAGS);
    }

    [Fact]
    public void Arithmetic_operators_match_headers()
    {
        int[] expected = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13];
        int[] actual =
        [
            LUA_OPADD, LUA_OPSUB, LUA_OPMUL, LUA_OPMOD, LUA_OPPOW, LUA_OPDIV, LUA_OPIDIV,
            LUA_OPBAND, LUA_OPBOR, LUA_OPBXOR, LUA_OPSHL, LUA_OPSHR, LUA_OPUNM, LUA_OPBNOT
        ];

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Comparison_operators_match_headers()
    {
        Assert.Equal(0, LUA_OPEQ);
        Assert.Equal(1, LUA_OPLT);
        Assert.Equal(2, LUA_OPLE);
    }

    [Fact]
    public void Gc_commands_match_headers()
    {
        int[] expected = [0, 1, 2, 3, 4, 5, 6, 7, 9];
        int[] actual =
        [
            LUA_GCSTOP, LUA_GCRESTART, LUA_GCCOLLECT, LUA_GCCOUNT, LUA_GCCOUNTB,
            LUA_GCSTEP, LUA_GCSETPAUSE, LUA_GCSETSTEPMUL, LUA_GCISRUNNING
        ];

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Hook_events_and_masks_match_headers()
    {
        Assert.Equal(0, LUA_HOOKCALL);
        Assert.Equal(1, LUA_HOOKRET);
        Assert.Equal(2, LUA_HOOKLINE);
        Assert.Equal(3, LUA_HOOKCOUNT);
        Assert.Equal(4, LUA_HOOKTAILCALL);
        Assert.Equal(1, LUA_MASKCALL);
        Assert.Equal(2, LUA_MASKRET);
        Assert.Equal(4, LUA_MASKLINE);
        Assert.Equal(8, LUA_MASKCOUNT);
    }

    [Fact]
    public void Integer_limits_are_64_bit()
    {
        Assert.Equal(long.MaxValue, LUA_MAXINTEGER);
        Assert.Equal(long.MinValue, LUA_MININTEGER);
    }

    [Fact]
    public void Byte_string_constants_match_headers()
    {
        Assert.True(LUA_SIGNATURE.SequenceEqual((ReadOnlySpan<byte>)[0x1B, (byte)'L', (byte)'u', (byte)'a']));
        Assert.True(LUA_COLIBNAME.SequenceEqual("coroutine"u8));
        Assert.True(LUA_TABLIBNAME.SequenceEqual("table"u8));
        Assert.True(LUA_IOLIBNAME.SequenceEqual("io"u8));
        Assert.True(LUA_OSLIBNAME.SequenceEqual("os"u8));
        Assert.True(LUA_STRLIBNAME.SequenceEqual("string"u8));
        Assert.True(LUA_UTF8LIBNAME.SequenceEqual("utf8"u8));
        Assert.True(LUA_MATHLIBNAME.SequenceEqual("math"u8));
        Assert.True(LUA_DBLIBNAME.SequenceEqual("debug"u8));
        Assert.True(LUA_LOADLIBNAME.SequenceEqual("package"u8));
    }
}
