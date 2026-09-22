using System.Globalization;

using CheatEngine.SDK.Hosting.Diagnostics;
using CheatEngine.SDK.Hosting.Plugin;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.State;

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
		LuaState state = CheatEngine.SDK.Lua.Runtime.LuaRuntime.AcquireState();
		LuaStatus registration = ProbeConsole.RegisterLuaFunctions(state);
		HostLog.Write(registration.IsOk ? HostLogLevel.Information : HostLogLevel.Error,
			string.Create(CultureInfo.InvariantCulture,
				$"CE 7.7 live probe: console command registration -> {registration}."));

		LiveProbeState.ValidateAfterEnable();
		HostLog.Write(HostLogLevel.Information, LiveProbeState.GetStatus());
	}

	/// <inheritdoc />
	protected override void OnDisable()
	{
		LuaState state = CheatEngine.SDK.Lua.Runtime.LuaRuntime.AcquireState();
		LuaStatus registration = ProbeConsole.UnregisterLuaFunctions(state);
		HostLog.Write(registration.IsOk ? HostLogLevel.Information : HostLogLevel.Error,
			string.Create(CultureInfo.InvariantCulture,
				$"CE 7.7 live probe: console command unregistration -> {registration}."));

		// Deliberately do not dispose the callback-shutdown probe here. LuaRuntime.Detach, which runs immediately after
		// OnDisable, is the system under test: it must neutralize the callback before freeing its GCHandle.
		LiveProbeState.RecordDisable();
	}
}
