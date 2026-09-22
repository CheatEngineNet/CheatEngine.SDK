using CheatEngine.SDK.Lua.Interop.Api;

namespace CheatEngine.SDK.Tests.Shared.NativeLua;

/// <summary>
///     Process-wide access to a real Lua 5.3 library for <c>Category=NativeLua</c> tests and for the benchmarks.
///     The first touch locates the DLL, loads it and binds <see cref="LuaApi" />; everything after that is a field read.
/// </summary>
/// <remarks>
///     Lookup: the <see cref="PathVariable" /> environment variable when it is set (no fallback then: an override must
///     not silently test another DLL), else <see cref="BundledPath" />, Cheat Engine's own Lua kept in
///     <c>native/cheat-engine</c>. An installed Cheat Engine is never consulted, so every machine and CI run binds the
///     same build. The module is never unloaded.
///     This file stays free of xUnit types: a test skips with
///     <c>Assert.SkipUnless(NativeLuaLibrary.IsAvailable, NativeLuaLibrary.UnavailableReason)</c>.
/// </remarks>
internal static class NativeLuaLibrary
{
	/// <summary>Environment variable that points at a Lua 5.3 DLL of the test process architecture.</summary>
	public const string PathVariable = "CHEATENGINE_SDK_LUA53_PATH";

	// A static readonly initializer runs once, under the runtime's type-initialization lock.
	private static readonly NativeLuaProbe SProbe =
		NativeLuaProbe.Run(Environment.GetEnvironmentVariable(PathVariable));

	/// <summary>
	///     Where the build copies Cheat Engine's 64-bit Lua 5.3 DLL, beside the test executable. Computed on each read, so
	///     the static field initializer above never depends on the order in which the members are declared.
	/// </summary>
	public static string BundledPath => Path.Combine(AppContext.BaseDirectory, "native", "lua53-64.dll");

	/// <summary>Whether a Lua 5.3 library is loaded and <see cref="LuaApi" /> is bound to it.</summary>
	public static bool IsAvailable => SProbe.Handle != 0;

	/// <summary>Handle of the loaded module, or zero when unavailable.</summary>
	public static nint Handle => SProbe.Handle;

	/// <summary>Full path of the loaded DLL, or null when unavailable.</summary>
	public static string? LibraryPath => SProbe.LibraryPath;

	/// <summary>Why the library is unavailable, written as a test skip reason; empty when it is available.</summary>
	public static string UnavailableReason => SProbe.Reason;

	/// <summary>For callers that cannot skip (benchmarks, fixtures of other helpers).</summary>
	/// <exception cref="InvalidOperationException">
	///     The library is unavailable; the message is <see cref="UnavailableReason" />
	///     .
	/// </exception>
	public static void ThrowIfUnavailable()
	{
		if (!IsAvailable)
		{
			throw new InvalidOperationException(UnavailableReason);
		}
	}
}
