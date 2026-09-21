using System.Reflection;
using System.Runtime.InteropServices;

using CheatEngine.SDK.Lua.Interop.Protected;

namespace CheatEngine.SDK.Lua.Interop.Tests.Protected;

/// <summary>Contract tests for deterministic loading of the packaged native Lua bridge.</summary>
public sealed class LuaBridgeLoadingPolicyTests
{
    [Fact]
    public void Bridge_is_loaded_only_from_the_interop_assembly_directory()
    {
        var assembly = typeof(LuaProtectedApi).Assembly;
        var policy = assembly.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>();

        Assert.NotNull(policy);
        Assert.Equal(DllImportSearchPath.AssemblyDirectory, policy!.Paths);

        var bridgePath = Path.Combine(Path.GetDirectoryName(assembly.Location)!, "cheatengine-sdk-lua-bridge.dll");
        Assert.True(File.Exists(bridgePath), $"The native Lua bridge was not copied to '{bridgePath}'.");

        var module = NativeLibrary.Load(
            "cheatengine-sdk-lua-bridge",
            assembly,
            DllImportSearchPath.AssemblyDirectory);
        try
        {
            Assert.True(NativeLibrary.TryGetExport(module, "cheatengine_sdk_lua_protected", out _));
        }
        finally
        {
            NativeLibrary.Free(module);
        }
    }
}
