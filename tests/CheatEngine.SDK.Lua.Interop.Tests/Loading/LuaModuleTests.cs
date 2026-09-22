using System.Runtime.InteropServices;

using CheatEngine.SDK.Lua.Interop.Loading;
using CheatEngine.SDK.Lua.Interop.Tests.Support;
using CheatEngine.SDK.Tests.Shared.NativeLua;

namespace CheatEngine.SDK.Lua.Interop.Tests.Loading;

public sealed class LuaModuleTests
{
	private const string NeverLoaded = "cheatengine-sdk-module-that-is-not-loaded-5f1c.dll";

	[Fact]
	public void Cheat_engine_module_name_is_the_64_bit_lua53_dll()
	{
		Assert.Equal("lua53-64.dll", LuaModule.CheatEngine64ModuleName);
	}

	[Fact]
	public void TryGetLoaded_null_name_throws()
	{
		Assert.Throws<ArgumentNullException>(static () => LuaModule.TryGetLoaded(null!, out _));
	}

	[Fact]
	public void TryGetLoaded_empty_name_throws()
	{
		Assert.Throws<ArgumentException>(static () => LuaModule.TryGetLoaded(string.Empty, out _));
	}

	[Fact]
	public void TryGetLoaded_unknown_module_returns_false_and_loads_nothing()
	{
		Assert.False(LuaModule.TryGetLoaded(NeverLoaded, out IntPtr handle));
		Assert.Equal(0, handle);

		// Still not there afterwards: the lookup must not have gone through the DLL search path.
		Assert.False(LuaModule.TryGetLoaded(NeverLoaded, out _));
	}

	[Fact]
	public void TryGetLoaded_module_every_windows_process_has_returns_the_loader_handle()
	{
		Assert.SkipUnless(OperatingSystem.IsWindows(), "The loaded-module lookup is implemented for Windows only.");

		Assert.True(LuaModule.TryGetLoaded("kernel32.dll", out IntPtr handle));
		Assert.Equal(NativeLibrary.Load("kernel32.dll"), handle);
	}

	[Fact]
	public void TryGetLoaded_default_name_agrees_with_the_explicit_name()
	{
		bool byDefault = LuaModule.TryGetLoaded(out IntPtr defaultHandle);
		bool byName = LuaModule.TryGetLoaded(LuaModule.CheatEngine64ModuleName, out IntPtr namedHandle);

		Assert.Equal(byName, byDefault);
		Assert.Equal(namedHandle, defaultHandle);
	}

	[Fact]
	[Trait("Category", "NativeLua")]
	public void TryGetLoaded_finds_the_lua_module_the_fixture_loaded_without_loading_another()
	{
		LuaTest.RequireNativeLua();
		Assert.SkipUnless(OperatingSystem.IsWindows(), "The loaded-module lookup is implemented for Windows only.");

		string fileName = Path.GetFileName(NativeLuaLibrary.LibraryPath!);

		Assert.True(LuaModule.TryGetLoaded(fileName, out IntPtr byName));
		Assert.True(LuaModule.TryGetLoaded(NativeLuaLibrary.LibraryPath!, out IntPtr byPath));
		Assert.Equal(NativeLuaLibrary.Handle, byName);
		Assert.Equal(NativeLuaLibrary.Handle, byPath);
	}
}
