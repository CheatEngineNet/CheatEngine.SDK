using System.Globalization;

using CheatEngine.SDK.Annotations.Plugin;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Hosting.Bootstrap;
using CheatEngine.SDK.Hosting.Context;
using CheatEngine.SDK.Hosting.Diagnostics;
using CheatEngine.SDK.Hosting.Plugin;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace LivePlugin;

/// <summary>
///     The CheatEngine.SDK Live Plugin: a minimal Cheat Engine plugin to load into a real Cheat Engine. It exercises the
///     plugin lifecycle and the diagnostics seam of <c>CheatEngine.SDK.Hosting</c>, a generated Lua global binding
///     (<see cref="MemoryBindings" />) and a generated <c>[LuaFunction]</c> registration
///     (<see cref="LiveFunctions" />). <see cref="OnEnable" /> logs what Cheat Engine reported to the host.
///     README.md lists the steps that load the plugin into Cheat Engine.
/// </summary>
[CheatEnginePlugin("CheatEngine.SDK Live Plugin")]
public sealed class CheatEngineSdkLivePlugin : CheatEnginePlugin
{
	// PluginHost exposes no call counter for CEPluginInitialize. This counts how many times this plugin's OnEnable
	// has run across enable, disable and re-enable cycles, and is logged next to the two record sizes that
	// PluginHost does expose.
	private static int s_enableCount;

	/// <inheritdoc />
	protected override void OnEnable()
	{
		int enableCount = Interlocked.Increment(ref s_enableCount);
		LogEnableDiagnostics(enableCount);

		LuaState state = LuaRuntime.AcquireState();
		LuaStatus registered = LiveFunctions.RegisterLuaFunctions(state);
		HostLog.Write(
			registered.IsOk ? HostLogLevel.Information : HostLogLevel.Error,
			string.Create(CultureInfo.InvariantCulture,
				$"CheatEngine.SDK Live Plugin: RegisterLuaFunctions -> {registered}."));

		ReadMemoryAdjacentPrimitive();
	}

	/// <inheritdoc />
	protected override void OnDisable()
	{
		LuaState state = LuaRuntime.AcquireState();
		LuaStatus unregistered = LiveFunctions.UnregisterLuaFunctions(state);
		HostLog.Write(
			unregistered.IsOk ? HostLogLevel.Information : HostLogLevel.Error,
			string.Create(CultureInfo.InvariantCulture,
				$"CheatEngine.SDK Live Plugin: UnregisterLuaFunctions -> {unregistered}."));
	}

	// Logs, as one entry a person reads in DebugView, what Cheat Engine reported to the host: the two record sizes
	// PluginHost observed, plus the exports record size and the slot presence that PluginContext reports.
	private static void LogEnableDiagnostics(int enableCount)
	{
		PluginContext context = Context;
		string message = string.Create(
			CultureInfo.InvariantCulture,
			$"""
			 CheatEngine.SDK Live Plugin: OnEnable #{enableCount} (this plugin instance's own enable count; PluginHost does not expose a count of CEPluginInitialize calls).
			 PluginContext: PluginId={context.PluginId}, Epoch={context.Epoch}, MainThreadId={context.MainThreadId}, ReportedExportsSize={context.ReportedExportsSize} (expect 48 on x64), HasProcessMessages={context.HasProcessMessages}, HasCheckSynchronize={context.HasCheckSynchronize}.
			 PluginHost: LastInitRecordArgument={PluginHost.LastInitRecordArgument} (the raw CEPluginInitialize second integer; opaque until a CE 7.7 live probe establishes its meaning), LastVersionRecordSize={PluginHost.LastVersionRecordSize} (the GetVersion host-reported record size; expect >= 16), IsInitialized={PluginHost.IsInitialized}.
			 """);
		HostLog.Write(HostLogLevel.Information, message);
	}

	private static void ReadMemoryAdjacentPrimitive()
	{
		// A placeholder address: point it at a readable location in an attached target to log a real value. Address
		// supplies the number-or-hex-text convention and culture-invariant formatting. The read uses this plugin's
		// generated integer binding; the SDK also exposes the same operation through its ready-made scalar wrapper.
		Address probe = Address.FromUInt64(0x00400000UL);
		bool ok = MemoryBindings.TryReadInt32((nuint) probe.Value, out int value);
		HostLog.Write(
			HostLogLevel.Information,
			string.Create(CultureInfo.InvariantCulture,
				$"CheatEngine.SDK Live Plugin: readInteger({probe}) -> ok={ok}, value={value}."));
	}
}
