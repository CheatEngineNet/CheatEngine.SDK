using System.Globalization;

using CheatEngine.SDK.Annotations.Lua;

namespace LivePlugin;

/// <summary>
///     The <c>[LuaFunction]</c>s this plugin registers: two Lua-callable diagnostics, reachable from Cheat Engine's
///     Lua console once the plugin is enabled, for a person at that console to call by hand.
///     <see cref="CheatEngineSdkLivePlugin.OnEnable" /> registers them through the generator's <c>RegisterLuaFunctions</c>
///     ;
///     <see cref="CheatEngineSdkLivePlugin.OnDisable" /> unregisters them through <c>UnregisterLuaFunctions</c>.
/// </summary>
internal static class LiveFunctions
{
	private static long s_pingCount;

	/// <summary>Lua: <c>cheatengine_sdk_live_ping()</c>. Increments and returns a process-lifetime counter.</summary>
	/// <returns>The counter, after incrementing.</returns>
	[LuaFunction("cheatengine_sdk_live_ping")]
	public static long Ping()
	{
		return Interlocked.Increment(ref s_pingCount);
	}

	/// <summary>Lua: <c>cheatengine_sdk_live_status()</c>. A one-line status string for a human at the Lua console.</summary>
	/// <returns>The status text.</returns>
	[LuaFunction("cheatengine_sdk_live_status")]
	public static string Status()
	{
		return string.Create(
			CultureInfo.InvariantCulture,
			$"CheatEngine.SDK Live Plugin enabled; cheatengine_sdk_live_ping has been called {Volatile.Read(ref s_pingCount)} time(s).");
	}
}
