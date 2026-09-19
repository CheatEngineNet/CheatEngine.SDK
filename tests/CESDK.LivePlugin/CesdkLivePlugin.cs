using System.Globalization;
using CESDK.Annotations.Plugin;
using CESDK.Engine.Values;
using CESDK.Hosting.Bootstrap;
using CESDK.Hosting.Diagnostics;
using CESDK.Hosting.Plugin;
using CESDK.Lua.Runtime;

namespace LivePlugin;

/// <summary>
///     The CESDK Live Plugin: a minimal Cheat Engine plugin to load into a real Cheat Engine. It exercises the
///     plugin lifecycle and the diagnostics seam of <c>CESDK.Hosting</c>, a generated Lua global binding
///     (<see cref="MemoryBindings" />) and a generated <c>[LuaFunction]</c> registration
///     (<see cref="LiveFunctions" />). <see cref="OnEnable" /> logs what Cheat Engine reported to the host.
///     README.md lists the steps that load the plugin into Cheat Engine.
/// </summary>
[CheatEnginePlugin("CESDK Live Plugin")]
public sealed class CesdkLivePlugin : CheatEnginePlugin
{
    // PluginHost exposes no call counter for CEPluginInitialize. This counts how many times this plugin's OnEnable
    // has run across enable, disable and re-enable cycles, and is logged next to the two record sizes that
    // PluginHost does expose.
    private static int s_enableCount;

    /// <inheritdoc />
    protected override void OnEnable()
    {
        var enableCount = Interlocked.Increment(ref s_enableCount);
        LogEnableDiagnostics(enableCount);

        var state = LuaRuntime.AcquireState();
        var registered = LiveFunctions.RegisterLuaFunctions(state);
        HostLog.Write(
            registered.IsOk ? HostLogLevel.Information : HostLogLevel.Error,
            string.Create(CultureInfo.InvariantCulture, $"CESDK Live Plugin: RegisterLuaFunctions -> {registered}."));

        ReadMemoryAdjacentPrimitive();
    }

    /// <inheritdoc />
    protected override void OnDisable()
    {
        var state = LuaRuntime.AcquireState();
        var unregistered = LiveFunctions.UnregisterLuaFunctions(state);
        HostLog.Write(
            unregistered.IsOk ? HostLogLevel.Information : HostLogLevel.Error,
            string.Create(CultureInfo.InvariantCulture,
                $"CESDK Live Plugin: UnregisterLuaFunctions -> {unregistered}."));
    }

    // Logs, as one entry a person reads in DebugView, what Cheat Engine reported to the host: the two record sizes
    // PluginHost observed, plus the exports record size and the slot presence that PluginContext reports.
    private static void LogEnableDiagnostics(int enableCount)
    {
        var context = Context;
        var message = string.Create(
            CultureInfo.InvariantCulture,
            $"""
             CESDK Live Plugin: OnEnable #{enableCount} (this plugin instance's own enable count; PluginHost does not expose a count of CEPluginInitialize calls).
             PluginContext: PluginId={context.PluginId}, Epoch={context.Epoch}, MainThreadId={context.MainThreadId}, ReportedExportsSize={context.ReportedExportsSize} (expect 48 on x64), HasProcessMessages={context.HasProcessMessages}, HasCheckSynchronize={context.HasCheckSynchronize}.
             PluginHost: LastInitRecordSize={PluginHost.LastInitRecordSize} (the CEPluginInitialize size argument; expect 36), LastVersionRecordSize={PluginHost.LastVersionRecordSize} (the GetVersion size argument; expect >= 16), IsInitialized={PluginHost.IsInitialized}.
             """);
        HostLog.Write(HostLogLevel.Information, message);
    }

    private static void ReadMemoryAdjacentPrimitive()
    {
        // A placeholder address: point it at a readable location in an attached target (for example a module base
        // from getAddress()) to log a real value. Address supplies the number-or-hex-text convention and the
        // culture-invariant formatting. The read goes through this plugin's own readInteger binding (MemoryBindings);
        // CESDK.Engine.Generated.MemoryScalars.TryReadInt32 offers the same call as a ready-made wrapper.
        var probe = Address.FromUInt64(0x00400000UL);
        var ok = MemoryBindings.TryReadInt32((nuint)probe.Value, out var value);
        HostLog.Write(
            HostLogLevel.Information,
            string.Create(CultureInfo.InvariantCulture,
                $"CESDK Live Plugin: readInteger({probe}) -> ok={ok}, value={value}."));
    }
}
