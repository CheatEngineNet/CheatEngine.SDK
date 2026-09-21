using CheatEngine.SDK.Annotations.Lua;

namespace LiveProbe;

/// <summary>
///     Lua-console commands for the CE 7.7 live-probe plugin. Every command returns a self-contained status record;
///     commands that initiate a probe first require the exact host and disposable-target authorization gate.
/// </summary>
internal static partial class ProbeConsole
{
    /// <summary>Returns the gate decision and all observations accumulated in this process.</summary>
    [LuaFunction("ce77_live_probe_status")]
    public static string Status()
    {
        return LiveProbeState.GetStatus();
    }

    /// <summary>Captures a JSON identity record for the authorized CE host, Lua, bridge, plugin, and disposable target.</summary>
    [LuaFunction("ce77_live_probe_host_profile")]
    public static string HostProfile()
    {
        return LiveProbeState.CaptureHostProfile();
    }

    /// <summary>Starts the non-mutating worker-to-GUI synchronize probe and returns immediately.</summary>
    [LuaFunction("ce77_live_probe_begin_synchronize")]
    public static string BeginSynchronize()
    {
        return LiveProbeState.BeginSynchronizeProbe();
    }

    /// <summary>Returns the most recent synchronize probe result without waiting or pumping the GUI thread.</summary>
    [LuaFunction("ce77_live_probe_synchronize_status")]
    public static string SynchronizeStatus()
    {
        return LiveProbeState.GetSynchronizeStatus();
    }

    /// <summary>Starts the worker Lua-state and shared-registry observation and returns immediately.</summary>
    [LuaFunction("ce77_live_probe_begin_lua_threads")]
    public static string BeginLuaThreads()
    {
        return LiveProbeState.BeginLuaThreadProbe();
    }

    /// <summary>Returns the worker Lua-state and shared-registry observation.</summary>
    [LuaFunction("ce77_live_probe_lua_threads_status")]
    public static string LuaThreadsStatus()
    {
        return LiveProbeState.GetLuaThreadStatus();
    }

    /// <summary>Captures a Lua-state/reference snapshot before an operator manually calls CE's resetLuaState().</summary>
    [LuaFunction("ce77_live_probe_snapshot_before_reset")]
    public static string SnapshotBeforeReset()
    {
        return LiveProbeState.SnapshotBeforeReset();
    }

    /// <summary>Captures a second snapshot after an operator manually called CE's resetLuaState().</summary>
    [LuaFunction("ce77_live_probe_snapshot_after_reset")]
    public static string SnapshotAfterReset()
    {
        return LiveProbeState.SnapshotAfterReset();
    }

    /// <summary>Observes the type and identity text of CE's getMainForm() userdata without retaining it.</summary>
    [LuaFunction("ce77_live_probe_userdata")]
    public static string ObserveUserdata()
    {
        return LiveProbeState.ObserveHostUserdata();
    }

    /// <summary>
    ///     Registers a harmless managed callback, intentionally leaves it registered, then asks the operator to disable
    ///     the plugin and call it through pcall. This tests LuaRuntime.Detach's callback neutralization.
    /// </summary>
    [LuaFunction("ce77_live_probe_prepare_callback_shutdown")]
    public static string PrepareCallbackShutdown()
    {
        return LiveProbeState.PrepareCallbackShutdownProbe();
    }
}
