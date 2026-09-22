using CheatEngine.SDK.Annotations.Plugin;
using CheatEngine.SDK.Hosting.Diagnostics;
using CheatEngine.SDK.Hosting.Plugin;
using CheatEngine.SDK.Lua.Runtime;

namespace LivePlugin.Coexistence.PluginA;

/// <summary>
///     First half of the opt-in coexistence fixture. Its Lua names are intentionally distinct from Plugin B's names.
/// </summary>
[CheatEnginePlugin("CheatEngine.SDK Coexistence Plugin A")]
public sealed class CoexistencePluginA : CheatEnginePlugin
{
	/// <inheritdoc />
	protected override void OnEnable()
	{
		CoexistenceDiagnostics.LogEnabled("A", typeof(CoexistencePluginA).Assembly, Context);

		var result = CoexistencePluginAFunctions.RegisterLuaFunctions(LuaRuntime.AcquireState());
		HostLog.Write(result.IsOk ? HostLogLevel.Information : HostLogLevel.Error,
			"CheatEngine.SDK coexistence A: RegisterLuaFunctions -> " + result + ".");
		if (!result.IsOk)
		{
			throw new InvalidOperationException("Plugin A could not register its distinct coexistence Lua functions.");
		}
	}

	/// <inheritdoc />
	protected override void OnDisable()
	{
		var result = CoexistencePluginAFunctions.UnregisterLuaFunctions(LuaRuntime.AcquireState());
		HostLog.Write(result.IsOk ? HostLogLevel.Information : HostLogLevel.Error,
			"CheatEngine.SDK coexistence A: UnregisterLuaFunctions -> " + result + ".");
	}
}
