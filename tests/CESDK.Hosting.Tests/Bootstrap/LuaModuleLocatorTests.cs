using System.Runtime.InteropServices;
using CESDK.Hosting.Bootstrap;
using CESDK.Hosting.Tests.Support;
using CESDK.Lua.Interop.Api;
using CESDK.Lua.Interop.Loading;
using CESDK.Tests.Shared.NativeLua;

namespace CESDK.Hosting.Tests.Bootstrap;

/// <summary>
///     The loader reference that the module lookup adds. A system DLL that is not Lua stands in for the module: its bind
///     fails on the missing exports without touching the table, and only the test maps it, so it unloads with the last
///     reference, which is how the count is observed.
/// </summary>
public sealed class LuaModuleLocatorTests
{
    // Small system DLLs that a test process does not map on its own.
    private static readonly string[] SystemDlls = ["msftedit.dll", "winhttp.dll", "wintrust.dll", "cabinet.dll"];

    [Fact]
    public void A_refused_bind_releases_the_reference_the_lookup_added()
    {
        var name = FindUnmappedSystemDll();
        var owner = NativeLibrary.Load(name);
        try
        {
            Assert.True(LuaModule.TryGetLoaded(name, out var handle));
            Assert.Equal(owner, handle);

            Assert.False(LuaModuleLocator.BindLocated(handle, true, out var failure));

            Assert.Contains("could not be bound", failure, StringComparison.Ordinal);
            Assert.True(IsMapped(name)); // the owner's reference is untouched
            NativeLibrary.Free(owner);
            Assert.False(IsMapped(name)); // and the lookup's reference is gone
        }
        finally
        {
            Unload(name);
        }
    }

    [Fact]
    public void A_handle_the_caller_owns_is_never_released()
    {
        var name = FindUnmappedSystemDll();
        var owner = NativeLibrary.Load(name);
        try
        {
            Assert.False(LuaModuleLocator.BindLocated(owner, false, out _));

            Assert.True(IsMapped(name));
            NativeLibrary.Free(owner);
            Assert.False(IsMapped(name));
        }
        finally
        {
            Unload(name);
        }
    }

    [Fact]
    [Trait("Category", "NativeLua")]
    public void Binding_again_the_module_the_table_is_bound_to_succeeds_and_keeps_it_bound()
    {
        HostingTest.RequireNativeLua();
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The loaded-module lookup is implemented for Windows only.");

        // The fixture bound the table to this module when it loaded the DLL.
        Assert.True(LuaModule.TryGetLoaded(NativeLuaLibrary.LibraryPath!, out var handle));
        Assert.Equal(NativeLuaLibrary.Handle, handle);

        Assert.True(LuaModuleLocator.BindLocated(handle, true, out var failure));

        Assert.Null(failure);
        Assert.Equal(NativeLuaLibrary.Handle, LuaApi.ModuleHandle);
    }

    private static string FindUnmappedSystemDll()
    {
        string? found = null;
        if (OperatingSystem.IsWindows())
            foreach (var candidate in SystemDlls)
            {
                if (IsMapped(candidate) || !NativeLibrary.TryLoad(candidate, out var probe)) continue;

                NativeLibrary.Free(probe);
                if (IsMapped(candidate)) continue;

                found = candidate;
                break;
            }

        Assert.SkipUnless(found is not null, "No system DLL that this process leaves unmapped was found.");
        return found!;
    }

    // A successful lookup adds a reference: it is handed straight back, so the probe never changes the count.
    private static bool IsMapped(string name)
    {
        if (!LuaModule.TryGetLoaded(name, out var handle)) return false;

        NativeLibrary.Free(handle);
        return true;
    }

    // Teardown for a failed assertion: drains whatever the test left behind, one net reference per turn.
    private static void Unload(string name)
    {
        while (LuaModule.TryGetLoaded(name, out var handle))
        {
            NativeLibrary.Free(handle);
            NativeLibrary.Free(handle);
        }
    }
}
