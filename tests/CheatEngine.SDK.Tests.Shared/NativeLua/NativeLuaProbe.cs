using System.Runtime.InteropServices;
using System.Security;

using CheatEngine.SDK.Lua.Interop.Api;

namespace CheatEngine.SDK.Tests.Shared.NativeLua;

/// <summary>
///     Outcome of one attempt to locate, load and bind a Lua 5.3 library. <see cref="NativeLuaLibrary" /> runs it once per
///     process; it is a type of its own so that the lookup rules can be tested without touching that process-wide state.
/// </summary>
/// <param name="Handle">Handle of the loaded module, or zero when unavailable.</param>
/// <param name="LibraryPath">The absolute path that was checked, loaded and bound, or null when unavailable.</param>
/// <param name="Reason">Why the library is unavailable, written as a test skip reason; empty when it is available.</param>
internal sealed record NativeLuaProbe(nint Handle, string? LibraryPath, string Reason)
{
	/// <summary>
	///     Locates the DLL (<paramref name="configured" /> when it is not blank, else
	///     <see cref="NativeLuaLibrary.BundledPath" />), loads it and binds <see cref="LuaApi" />. Never throws: every
	///     failure becomes <see cref="Reason" />.
	/// </summary>
	/// <param name="configured">Value of the <see cref="NativeLuaLibrary.PathVariable" /> environment variable, or null.</param>
	public static NativeLuaProbe Run(string? configured)
	{
		bool fromEnvironment = !string.IsNullOrWhiteSpace(configured);
		string candidate = fromEnvironment ? configured!.Trim() : NativeLuaLibrary.BundledPath;
		string origin = fromEnvironment
			? "the " + NativeLuaLibrary.PathVariable + " environment variable"
			: "the Cheat Engine Lua copied next to the tests";
		string architecture = RuntimeInformation.ProcessArchitecture.ToString();

		// One absolute path for the existence check, the load and the report. A relative value would be resolved
		// against the current directory by File.Exists but by the loader's own search rules by TryLoad, so the file
		// that was checked and the file that gets bound could differ.
		if (!TryGetFullPath(candidate, out string path))
		{
			return Unavailable($"No Lua 5.3 library: '{candidate}' (from {origin}) is not a valid path.");
		}

		if (!File.Exists(path))
		{
			return Unavailable(
				$"No Lua 5.3 library: '{path}' (from {origin}) does not exist. Rebuild the test project, or set {NativeLuaLibrary.PathVariable} to a Lua 5.3 DLL built for {architecture}.");
		}

		if (!NativeLibrary.TryLoad(path, out IntPtr handle))
		{
			return Unavailable(
				$"'{path}' (from {origin}) exists but could not be loaded into this {architecture} process: wrong architecture or a missing dependency.");
		}

		if (!LuaApi.TryInitialize(handle, out string? failure))
		{
			return Unavailable($"'{path}' (from {origin}) was loaded but cannot be bound: {failure}");
		}

		return new NativeLuaProbe(handle, path, string.Empty);
	}

	private static bool TryGetFullPath(string candidate, out string fullPath)
	{
		try
		{
			fullPath = Path.GetFullPath(candidate);
			return true;
		}
		catch (Exception exception) when (exception is ArgumentException or NotSupportedException
			                                  or PathTooLongException or SecurityException)
		{
			fullPath = string.Empty;
			return false;
		}
	}

	private static NativeLuaProbe Unavailable(string reason)
	{
		return new NativeLuaProbe(0, null, reason);
	}
}
