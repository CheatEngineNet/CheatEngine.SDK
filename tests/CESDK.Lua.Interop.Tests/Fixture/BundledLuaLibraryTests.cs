using System.Security.Cryptography;
using CESDK.Lua.Interop.Tests.Support;
using CESDK.Tests.Shared.NativeLua;

namespace CESDK.Lua.Interop.Tests.Fixture;

/// <summary>
///     The Lua DLL that the fixture binds by default is the recorded Cheat Engine build, byte for byte, so the tests run
///     against the Lua that a plugin meets in production. Replacing <c>native/cheat-engine/lua53-64.dll</c> means
///     updating the two constants below and <c>native/cheat-engine/README.md</c>.
/// </summary>
public sealed class BundledLuaLibraryTests
{
    // Cheat Engine 7.7, x64.
    private const long ExpectedLength = 539496;
    private const string ExpectedSha256 = "C95DCDFA0F60F97B43D970D77FD1BB907AF4DE04B500A3C89A99600B20B35BD2";

    [Fact]
    public void Bundled_copy_is_the_recorded_Cheat_Engine_build()
    {
        var path = NativeLuaLibrary.BundledPath;

        Assert.True(File.Exists(path), $"The build did not copy '{path}' next to the test executable.");
        Assert.Equal(ExpectedLength, new FileInfo(path).Length);
        Assert.Equal(ExpectedSha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void Fixture_binds_the_bundled_copy_unless_the_variable_overrides_it()
    {
        LuaTest.RequireNativeLua();
        Assert.SkipWhen(!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(NativeLuaLibrary.PathVariable)),
            $"{NativeLuaLibrary.PathVariable} overrides the bundled copy.");

        Assert.Equal(Path.GetFullPath(NativeLuaLibrary.BundledPath), NativeLuaLibrary.LibraryPath);
    }
}
