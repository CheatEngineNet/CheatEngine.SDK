using System.Runtime.InteropServices;

using CheatEngine.SDK.Hosting.Bootstrap;
using CheatEngine.SDK.Hosting.Tests.Support;
using CheatEngine.SDK.Lua.Interop.Api;
using CheatEngine.SDK.Lua.Interop.Loading;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Hosting.Tests.Bootstrap;

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
		string name = FindUnmappedSystemDll();
		IntPtr owner = NativeLibrary.Load(name);
		try
		{
			Assert.True(LuaModule.TryGetLoaded(name, out IntPtr handle));
			Assert.Equal(owner, handle);

			Assert.False(LuaModuleLocator.BindLocated(handle, true, out string? failure));

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
		string name = FindUnmappedSystemDll();
		IntPtr owner = NativeLibrary.Load(name);
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
		Assert.True(LuaModule.TryGetLoaded(NativeLuaLibrary.LibraryPath!, out IntPtr handle));
		Assert.Equal(NativeLuaLibrary.Handle, handle);

		Assert.True(LuaModuleLocator.BindLocated(handle, true, out string? failure));

		Assert.Null(failure);
		Assert.Equal(NativeLuaLibrary.Handle, LuaApi.ModuleHandle);
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	public void Repeated_enable_releases_exactly_the_reference_each_lookup_added()
	{
		HostingTest.RequireNativeLua();
		Assert.SkipUnless(OperatingSystem.IsWindows(), "The loaded-module lookup is implemented for Windows only.");
		LuaModuleLocator.ResetFreedReferenceCountForTests();

		// The first bind here may already observe the table bound to this same handle from an earlier test (static,
		// process-wide state); ignore its effect on the counter. Every bind after that one is guaranteed to see the
		// table already bound to this handle, so each must release exactly the one loader reference its own lookup
		// added -- never more (a double free), never fewer (an accumulated reference; A05-01).
		Assert.True(LuaModule.TryGetLoaded(NativeLuaLibrary.LibraryPath!, out IntPtr firstHandle));
		Assert.True(LuaModuleLocator.BindLocated(firstHandle, true, out _));
		long baseline = LuaModuleLocator.FreedReferenceCountForTests;

		for (int i = 0; i < 2; i++)
		{
			Assert.True(LuaModule.TryGetLoaded(NativeLuaLibrary.LibraryPath!, out IntPtr handle));
			Assert.Equal(NativeLuaLibrary.Handle, handle);
			Assert.True(LuaModuleLocator.BindLocated(handle, true, out _));
		}

		Assert.Equal(baseline + 2, LuaModuleLocator.FreedReferenceCountForTests);
		Assert.Equal(NativeLuaLibrary.Handle, LuaApi.ModuleHandle);
	}

	private static string FindUnmappedSystemDll()
	{
		string? found = null;
		if (OperatingSystem.IsWindows())
		{
			foreach (string candidate in SystemDlls)
			{
				if (IsMapped(candidate) || !NativeLibrary.TryLoad(candidate, out IntPtr probe))
				{
					continue;
				}

				NativeLibrary.Free(probe);
				if (IsMapped(candidate))
				{
					continue;
				}

				found = candidate;
				break;
			}
		}

		Assert.SkipUnless(found is not null, "No system DLL that this process leaves unmapped was found.");
		return found!;
	}

	// A successful lookup adds a reference: it is handed straight back, so the probe never changes the count.
	private static bool IsMapped(string name)
	{
		if (!LuaModule.TryGetLoaded(name, out IntPtr handle))
		{
			return false;
		}

		NativeLibrary.Free(handle);
		return true;
	}

	// Teardown for a failed assertion: drains whatever the test left behind, one net reference per turn.
	private static void Unload(string name)
	{
		while (LuaModule.TryGetLoaded(name, out IntPtr handle))
		{
			NativeLibrary.Free(handle);
			NativeLibrary.Free(handle);
		}
	}
}
