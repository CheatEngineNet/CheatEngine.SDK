using System.Diagnostics.CodeAnalysis;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Hosting.Bootstrap;
using CheatEngine.SDK.Lua.Callbacks;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.SDK.AotProbe;

internal static class Program
{
    public static void Main()
    {
        Console.WriteLine($"CheatEngine.SDK Native AOT publication probe: {ProbeRepresentativePaths()}, {typeof(PluginHost).Assembly.GetName().Name}");
    }

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(LuaCallback<ProbeState>))]
    private static string ProbeRepresentativePaths()
    {
        if (!Address.TryParse("140001000", out Address address) || address.ToUInt64() != 0x140001000)
            throw new InvalidOperationException("Address probe failed.");

        LuaStatus status = KeepGenericPath(LuaStatus.Ok);
        _ = typeof(LuaCallback<ProbeState>);
        return $"{typeof(Address).Assembly.GetName().Name}, {status}";
    }

    private static T KeepGenericPath<T>(T value) where T : struct => value;

    private sealed class ProbeState;
}
