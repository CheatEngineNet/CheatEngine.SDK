using CheatEngine.SDK.Tests.Infrastructure;
using System.Runtime.InteropServices;

namespace CheatEngine.SDK.Tests.Packaging;

/// <summary>
///     The reason the package exists at all: a plugin project that only adds
///     <c>PackageReference Include="CheatEngine.SDK"</c> gets the host-mandated <c>CESDK.CESDK.CEPluginInitialize</c>
///     entry point for free, and a project that opts out with <c>CheatEngineSdkGenerateEntryPoint=false</c> does not
///     get it - proof that the <c>CompilerVisibleProperty</c> declared in the packaged
///     <c>build/CheatEngine.SDK.props</c> really reaches the generator, not just its own internal default.
/// </summary>
[Collection(PackagedUmbrellaSuite.Name)]
public sealed class EntryPointTests(PackagedUmbrellaFixture fixture)
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint BridgeVersion();

    [Fact]
    public void Default_consumer_gets_the_generated_entry_point_type()
    {
        Assert.True(fixture.DefaultEntryPointTypeExists,
            "CESDK.CESDK was not found in the default consumer's built assembly.");
    }

    [Fact]
    public void Default_consumer_entry_point_declares_CEPluginInitialize()
    {
        Assert.True(fixture.DefaultEntryPointMethodExists,
            "CESDK.CESDK.CEPluginInitialize(object, object) was not found in the default consumer's built assembly.");
    }

    [Fact]
    public void CheatEngineSdkGenerateEntryPoint_false_switches_the_generator_off()
    {
        Assert.False(fixture.EntryPointOffTypeExists,
            "CESDK.CESDK was generated although the consumer set CheatEngineSdkGenerateEntryPoint=false.");
    }

    [Fact]
    public void Default_consumer_gets_a_loadable_native_bridge()
    {
        Assert.True(File.Exists(fixture.DefaultNativeBridgePath),
            $"The bridge was not copied to '{fixture.DefaultNativeBridgePath}'.");

        var module = NativeLibrary.Load(fixture.DefaultNativeBridgePath);
        try
        {
            Assert.True(NativeLibrary.TryGetExport(module, "cheatengine_sdk_lua_protected", out _));
            var versionAddress = NativeLibrary.GetExport(module, "cheatengine_sdk_lua_bridge_abi_version");
            var version = Marshal.GetDelegateForFunctionPointer<BridgeVersion>(versionAddress);
            Assert.Equal(1u, version());
            var fingerprintAddress = NativeLibrary.GetExport(module, "cheatengine_sdk_lua_bridge_source_fingerprint");
            Assert.False(string.IsNullOrWhiteSpace(Marshal.PtrToStringAnsi(fingerprintAddress)));
        }
        finally
        {
            NativeLibrary.Free(module);
        }
    }

    [Fact]
    public void Published_consumer_keeps_the_native_bridge()
    {
        Assert.True(File.Exists(fixture.DefaultPublishedNativeBridgePath),
            $"The bridge was not published to '{fixture.DefaultPublishedNativeBridgePath}'.");
    }
}
