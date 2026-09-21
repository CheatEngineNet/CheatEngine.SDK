using CheatEngine.SDK.Annotations.Lua;

namespace LivePlugin.Coexistence.PluginA;

internal static partial class CoexistencePluginAFunctions
{
    private static long s_pingCount;

    [LuaFunction("cheatengine_sdk_coexistence_a_ping")]
    public static long Ping() => Interlocked.Increment(ref s_pingCount);

    [LuaFunction("cheatengine_sdk_coexistence_a_identity")]
    public static string Identity() => CoexistenceDiagnostics.GetIdentity("A", typeof(CoexistencePluginA).Assembly);
}
