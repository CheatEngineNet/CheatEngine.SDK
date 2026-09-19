using System.Reflection;
using System.Runtime.InteropServices;
using CheatEngine.SDK.Lua.Interop.Api;

namespace CheatEngine.SDK.Lua.Interop.Tests.Initialization;

/// <summary>
///     DLL-free: how binding fails. The main program module is the stand-in for "a module that is not Lua": it is always
///     loaded and exports none of the table's names. None of these calls can disturb a table that the NativeLua tests
///     have bound in the same process, because a failed bind changes nothing.
/// </summary>
public sealed class LuaApiInitializationTests
{
    private static nint NotLua => NativeLibrary.GetMainProgramHandle();

    private static int SlotCount
        => typeof(LuaApi.Table).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length;

    [Fact]
    public void Initialize_zero_handle_throws_argument_exception()
    {
        var exception = Assert.Throws<ArgumentException>(static () => LuaApi.Initialize(0));

        Assert.Equal("moduleHandle", exception.ParamName);
    }

    [Fact]
    public void TryInitialize_zero_handle_returns_false_with_reason()
    {
        Assert.False(LuaApi.TryInitialize(0, out var failure));
        Assert.Contains("zero", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void GetMissingExports_zero_handle_throws_argument_exception()
    {
        Assert.Throws<ArgumentException>(static () => LuaApi.GetMissingExports(0));
    }

    [Fact]
    public void GetMissingExports_module_without_lua_lists_every_slot_once()
    {
        var missing = LuaApi.GetMissingExports(NotLua);

        Assert.Equal(SlotCount, missing.Count);
        Assert.Equal(missing.Count, missing.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("lua_pcallk", missing, StringComparer.Ordinal);
        Assert.Contains("luaL_ref", missing, StringComparer.Ordinal);
        Assert.Contains("luaopen_base", missing, StringComparer.Ordinal);
    }

    [Fact]
    public void Initialize_module_without_lua_throws_naming_the_missing_exports()
    {
        var exception = Assert.Throws<EntryPointNotFoundException>(() => LuaApi.Initialize(NotLua));

        Assert.Contains("lua_gettop", exception.Message, StringComparison.Ordinal);
        Assert.Contains("lua_pcallk", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryInitialize_module_without_lua_returns_false_and_stays_unbound_to_it()
    {
        Assert.False(LuaApi.TryInitialize(NotLua, out var failure));

        Assert.Contains("lua_gettop", failure, StringComparison.Ordinal);
        Assert.NotEqual(NotLua, LuaApi.ModuleHandle);
    }
}
