using System.Reflection;
using System.Runtime.InteropServices;
using CESDK.Lua.Interop.Api;
using CESDK.Lua.Interop.Tests.Support;
using CESDK.Tests.Shared.NativeLua;

namespace CESDK.Lua.Interop.Tests.Initialization;

/// <summary>NativeLua: the state of the table once the fixture has bound it to a real Lua 5.3 module.</summary>
[Trait("Category", "NativeLua")]
public sealed class LuaApiBoundTableTests
{
    [Fact]
    public void Fixture_binds_the_table_to_its_module()
    {
        LuaTest.RequireNativeLua();

        Assert.True(LuaApi.IsInitialized);
        Assert.Equal(NativeLuaLibrary.Handle, LuaApi.ModuleHandle);
        Assert.Empty(LuaApi.GetMissingExports(NativeLuaLibrary.Handle));
    }

    [Fact]
    public void Initialize_same_module_again_is_a_no_op()
    {
        LuaTest.RequireNativeLua();

        LuaApi.Initialize(NativeLuaLibrary.Handle);
        Assert.True(LuaApi.TryInitialize(NativeLuaLibrary.Handle, out var failure));

        Assert.Null(failure);
        Assert.Equal(NativeLuaLibrary.Handle, LuaApi.ModuleHandle);
    }

    [Fact]
    public void Every_slot_holds_an_address_inside_the_process()
    {
        LuaTest.RequireNativeLua();

        var table = typeof(LuaApi).GetField("s_table", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var slots = typeof(LuaApi.Table).GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                   BindingFlags.NonPublic);

        Assert.NotEmpty(slots);
        foreach (var slot in slots)
        {
            // Reflection hands a function-pointer field back as a boxed IntPtr.
            var address = Assert.IsType<nint>(slot.GetValue(table));
            Assert.True(address != 0, slot.Name);
            Assert.True(NativeLibrary.TryGetExport(NativeLuaLibrary.Handle, slot.Name, out var export), slot.Name);
            Assert.Equal(export, address);
        }
    }

    [Fact]
    public void Second_copy_of_the_library_is_refused_and_leaves_the_table_alone()
    {
        LuaTest.RequireNativeLua();
        var copy = LoadSecondCopy();

        var bound = LuaApi.TryInitialize(copy, out var failure);
        var exception = Assert.Throws<InvalidOperationException>(() => LuaApi.Initialize(copy));

        Assert.False(bound);
        Assert.Contains("already bound", failure, StringComparison.Ordinal);
        Assert.Contains("already bound", exception.Message, StringComparison.Ordinal);
        Assert.Empty(LuaApi.GetMissingExports(copy));
        Assert.Equal(NativeLuaLibrary.Handle, LuaApi.ModuleHandle);
    }

    /// <summary>Maps the same DLL a second time under another file name, which the loader treats as a different module.</summary>
    private static nint LoadSecondCopy()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CESDK.Lua.Interop.Tests");
        var path = Path.Combine(directory, "lua53-second-copy.dll");
        Directory.CreateDirectory(directory);
        try
        {
            File.Copy(NativeLuaLibrary.LibraryPath!, path, true);
        }
        catch (IOException) when (File.Exists(path))
        {
            // Another test process has the copy mapped: it is the same file, use it as it is.
        }

        Assert.SkipUnless(NativeLibrary.TryLoad(path, out var copy),
            "The second copy of the Lua library could not be loaded from " + path);
        Assert.NotEqual(NativeLuaLibrary.Handle, copy);
        return copy;
    }
}
