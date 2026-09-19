using CESDK.Abi;
using CESDK.Hosting.Bootstrap;
using CESDK.Hosting.Context;
using CESDK.Hosting.Plugin;
using CESDK.Hosting.Threading;
using CESDK.Lua.Runtime;
using CESDK.Lua.State;

namespace CESDK.Hosting.Tests.Support;

/// <summary>
///     The plugin under test. Records what it observes at each lifecycle point, throws where a test told it to, and
///     makes a nested lifecycle call from inside <see cref="OnEnable" /> or <see cref="OnDisable" /> when a test installs
///     one (static switches, because the host constructs the instance itself).
/// </summary>
internal sealed class RecordingPlugin : CheatEnginePlugin
{
    public RecordingPlugin()
    {
        ConstructorCalls++;
        RuntimeAttachedInConstructor = LuaRuntime.IsAttached;
        HostEnabledInConstructor = PluginHost.IsEnabled;
        LastConstructed = this;
        if (ThrowInConstructor) throw new InvalidOperationException("constructor failure requested by the test");
    }

    public static bool ThrowInConstructor { get; set; }

    public static bool ThrowInOnEnable { get; set; }

    public static bool ThrowInOnDisable { get; set; }

    /// <summary>
    ///     A lifecycle call to make from inside <see cref="OnEnable" />, as a host re-entering through the message pump
    ///     would; null for none. Runs once, then clears itself.
    /// </summary>
    public static Func<Bool32>? NestedCallInOnEnable { get; set; }

    /// <summary>A lifecycle call to make from inside <see cref="OnDisable" />; null for none. Runs once, then clears itself.</summary>
    public static Func<Bool32>? NestedCallInOnDisable { get; set; }

    public static int ConstructorCalls { get; private set; }

    public static bool RuntimeAttachedInConstructor { get; private set; }

    public static bool HostEnabledInConstructor { get; private set; }

    public static RecordingPlugin? LastConstructed { get; private set; }

    public int EnableCalls { get; private set; }

    public int DisableCalls { get; private set; }

    public int EnableThreadId { get; private set; }

    public bool RuntimeAttachedInOnEnable { get; private set; }

    public bool HostEnabledInOnEnable { get; private set; }

    public bool MainThreadInOnEnable { get; private set; }

    public PluginContext? ContextInOnEnable { get; private set; }

    public long LuaResultInOnEnable { get; private set; }

    public bool RuntimeAttachedInOnDisable { get; private set; }

    public bool HostEnabledInOnDisable { get; private set; }

    /// <summary>What the nested call installed in <see cref="NestedCallInOnEnable" /> returned; null when none ran.</summary>
    public Bool32? NestedResultInOnEnable { get; private set; }

    /// <summary>What the nested call installed in <see cref="NestedCallInOnDisable" /> returned; null when none ran.</summary>
    public Bool32? NestedResultInOnDisable { get; private set; }

    /// <summary><see cref="PluginHost.IsEnabled" /> right after the nested call returned, inside the outer callback.</summary>
    public bool HostEnabledAfterNestedCall { get; private set; }

    /// <summary><see cref="LuaRuntime.IsAttached" /> right after the nested call returned, inside the outer callback.</summary>
    public bool RuntimeAttachedAfterNestedCall { get; private set; }

    public static void Reset()
    {
        ThrowInConstructor = false;
        ThrowInOnEnable = false;
        ThrowInOnDisable = false;
        NestedCallInOnEnable = null;
        NestedCallInOnDisable = null;
        ConstructorCalls = 0;
        RuntimeAttachedInConstructor = false;
        HostEnabledInConstructor = false;
        LastConstructed = null;
    }

    protected internal override void OnEnable()
    {
        EnableCalls++;
        EnableThreadId = Environment.CurrentManagedThreadId;
        RuntimeAttachedInOnEnable = LuaRuntime.IsAttached;
        HostEnabledInOnEnable = PluginHost.IsEnabled;
        MainThreadInOnEnable = MainThread.IsMainThread;
        ContextInOnEnable = Context;

        // The Lua layer must be usable here: run a chunk on the state the host hands out.
        var L = LuaRuntime.AcquireState();
        using (LuaFrame frame = new(L))
        {
            if (L.TryExecute("return 40 + 2"u8, 1).IsOk && L.TryReadInteger(-1, out var value))
                LuaResultInOnEnable = value;
        }

        var nested = NestedCallInOnEnable;
        if (nested is not null)
        {
            NestedCallInOnEnable = null;
            NestedResultInOnEnable = nested();
            HostEnabledAfterNestedCall = PluginHost.IsEnabled;
            RuntimeAttachedAfterNestedCall = LuaRuntime.IsAttached;
        }

        if (ThrowInOnEnable) throw new InvalidOperationException("OnEnable failure requested by the test");
    }

    protected internal override void OnDisable()
    {
        DisableCalls++;
        RuntimeAttachedInOnDisable = LuaRuntime.IsAttached;
        HostEnabledInOnDisable = PluginHost.IsEnabled;

        var nested = NestedCallInOnDisable;
        if (nested is not null)
        {
            NestedCallInOnDisable = null;
            NestedResultInOnDisable = nested();
            HostEnabledAfterNestedCall = PluginHost.IsEnabled;
            RuntimeAttachedAfterNestedCall = LuaRuntime.IsAttached;
        }

        if (ThrowInOnDisable) throw new InvalidOperationException("OnDisable failure requested by the test");
    }
}
