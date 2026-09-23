using CheatEngine.SDK.Annotations.Lua;

namespace LivePlugin.Coexistence.PluginB;

internal static class CoexistencePluginBFunctions
{
	private static long s_pingCount;

	[LuaFunction("cheatengine_sdk_coexistence_b_ping")]
	public static long Ping()
	{
		return Interlocked.Increment(ref s_pingCount);
	}

	[LuaFunction("cheatengine_sdk_coexistence_b_identity")]
	public static string Identity()
	{
		return CoexistenceDiagnostics.GetIdentity("B", typeof(CoexistencePluginB).Assembly);
	}
}
