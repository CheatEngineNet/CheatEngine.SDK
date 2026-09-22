using System.Globalization;

using CheatEngine.SDK.Hosting.Diagnostics;
using CheatEngine.SDK.Hosting.Plugin;

namespace LiveProbe;

/// <summary>
///     An operator-driven, opt-in set of probes for a hash-pinned CE 7.7 x64 installation.
/// </summary>
/// <remarks>
///     Nothing here is production SDK behaviour. The plugin records evidence through DebugView/OutputDebugString and
///     exposes small Lua-console commands only after its authorization checks pass. It never selects, opens, writes to,
///     suspends, injects into, or otherwise mutates a target process.
/// </remarks>
internal sealed class Ce77LiveProbePlugin : CheatEnginePlugin
{
	/// <inheritdoc />
	protected override void OnEnable()
	{
		var state = CheatEngine.SDK.Lua.Runtime.LuaRuntime.AcquireState();
		var registration = ProbeConsole.RegisterLuaFunctions(state);
		HostLog.Write(registration.IsOk ? HostLogLevel.Information : HostLogLevel.Error,
			string.Create(CultureInfo.InvariantCulture,
				$"CE 7.7 live probe: console command registration -> {registration}."));

		LiveProbeState.ValidateAfterEnable();
		HostLog.Write(HostLogLevel.Information, LiveProbeState.GetStatus());
	}

	/// <inheritdoc />
	protected override void OnDisable()
	{
		var state = CheatEngine.SDK.Lua.Runtime.LuaRuntime.AcquireState();
		var registration = ProbeConsole.UnregisterLuaFunctions(state);
		HostLog.Write(registration.IsOk ? HostLogLevel.Information : HostLogLevel.Error,
			string.Create(CultureInfo.InvariantCulture,
				$"CE 7.7 live probe: console command unregistration -> {registration}."));

		// Deliberately do not dispose the callback-shutdown probe here. LuaRuntime.Detach, which runs immediately after
		// OnDisable, is the system under test: it must neutralize the callback before freeing its GCHandle.
		LiveProbeState.RecordDisable();
	}
}
