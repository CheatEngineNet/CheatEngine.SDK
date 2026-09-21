using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Annotations.Plugin;
using CheatEngine.SDK.Hosting.Diagnostics;
using CheatEngine.SDK.Hosting.Plugin;
using CheatEngine.SDK.Lua.Runtime;

namespace LivePlugin.Coexistence.PluginB;

/// <summary>
///     Second half of the opt-in coexistence fixture. Its Lua names are intentionally distinct from Plugin A's names.
/// </summary>
[CheatEnginePlugin("CheatEngine.SDK Coexistence Plugin B")]
public sealed class CoexistencePluginB : CheatEnginePlugin
{
    /// <inheritdoc />
    protected override void OnEnable()
    {
        CoexistenceDiagnostics.LogEnabled("B", typeof(CoexistencePluginB).Assembly, Context);

        var result = CoexistencePluginBFunctions.RegisterLuaFunctions(LuaRuntime.AcquireState());
        HostLog.Write(result.IsOk ? HostLogLevel.Information : HostLogLevel.Error,
            "CheatEngine.SDK coexistence B: RegisterLuaFunctions -> " + result + ".");
        if (!result.IsOk)
            throw new InvalidOperationException("Plugin B could not register its distinct coexistence Lua functions.");
    }

    /// <inheritdoc />
    protected override void OnDisable()
    {
        var result = CoexistencePluginBFunctions.UnregisterLuaFunctions(LuaRuntime.AcquireState());
        HostLog.Write(result.IsOk ? HostLogLevel.Information : HostLogLevel.Error,
            "CheatEngine.SDK coexistence B: UnregisterLuaFunctions -> " + result + ".");
    }
}
