using System.Runtime.InteropServices;
using System.Text;
using CheatEngine.SDK.Lua.Interop.Types;
using CheatEngine.SDK.Tests.Shared.NativeLua;
using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;

namespace CheatEngine.SDK.Lua.Interop.Tests.Support;

/// <summary>What the NativeLua tests share: the skip guard, chunk loading and string reading.</summary>
internal static unsafe class LuaTest
{
    /// <summary>Skips the calling test, with the fixture's reason, when no Lua 5.3 library is available.</summary>
    public static void RequireNativeLua()
    {
        Assert.SkipUnless(NativeLuaLibrary.IsAvailable, NativeLuaLibrary.UnavailableReason);
    }

    /// <summary>Compiles a text chunk named "=test"; returns the load status, with the function or the message pushed.</summary>
    public static int Load(lua_State* L, ReadOnlySpan<byte> source)
    {
        fixed (byte* text = source)
        fixed (byte* name = "=test"u8)
        fixed (byte* mode = "t"u8)
        {
            return luaL_loadbufferx(L, text, (nuint)source.Length, name, mode);
        }
    }

    /// <summary>Compiles and runs a chunk under <c>lua_pcallk</c>, failing the test with the Lua message on any error.</summary>
    public static void Run(lua_State* L, ReadOnlySpan<byte> source, int nresults = 0)
    {
        if (Load(L, source) != LUA_OK) Assert.Fail("The chunk did not compile: " + ReadString(L, -1));

        if (lua_pcallk(L, 0, nresults, 0, 0, null) != LUA_OK) Assert.Fail("The chunk raised: " + ReadString(L, -1));
    }

    /// <summary>
    ///     Decodes the string at <paramref name="idx" /> as UTF-8, NULs included; null when the value is not a string or
    ///     a number.
    /// </summary>
    public static string? ReadString(lua_State* L, int idx)
    {
        nuint length;
        var bytes = lua_tolstring(L, idx, &length);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes, checked((int)length));
    }

    /// <summary>Decodes a NUL-terminated C string owned by the Lua library.</summary>
    public static string? ReadCString(byte* text)
    {
        return text is null ? null : Encoding.UTF8.GetString(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(text));
    }
}
