using CheatEngine.SDK.Annotations.Lua;

namespace LivePlugin.Coexistence.PluginA;

internal static class CoexistencePluginAFunctions
{
	private static long s_pingCount;

	[LuaFunction("cheatengine_sdk_coexistence_a_ping")]
	public static long Ping()
	{
		return Interlocked.Increment(ref s_pingCount);
	}

	[LuaFunction("cheatengine_sdk_coexistence_a_identity")]
	public static string Identity()
	{
		return CoexistenceDiagnostics.GetIdentity("A", typeof(CoexistencePluginA).Assembly);
	}
}
