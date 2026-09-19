using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using CheatEngine.SDK.Abi;
using CheatEngine.SDK.Abi.Managed;
using CheatEngine.SDK.Abi.Native;
using CheatEngine.SDK.Hosting.Context;
using CheatEngine.SDK.Hosting.Diagnostics;
using CheatEngine.SDK.Hosting.Plugin;
using CheatEngine.SDK.Lua.Interop.Api;
using CheatEngine.SDK.Lua.Interop.Types;
using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.SDK.Hosting.Bootstrap;

// The three stdcall callbacks whose addresses the bootstrap writes into the init record, and the enable/disable
// logic behind them. [UnmanagedCallersOnly] methods cannot be generic or live in a generic type: the factory they
// need is reached through the PluginDescriptor the bootstrap stored.
public static unsafe partial class PluginHost
{
    /// <summary>
    ///     Cheat Engine's version query. Writes <see cref="AbiConstants.SdkVersion" /> and the name pointer of the
    ///     bootstrap into the host-owned <see cref="PluginVersion" />.
    /// </summary>
    /// <param name="version">The host's record.</param>
    /// <param name="size">
    ///     The byte size the host reserved for it, under the same rule as the bootstrap's <c>size</c>: a positive value
    ///     smaller than the record refuses the call, zero or a negative value is treated as unknown and the record is written.
    /// </param>
    /// <returns><c>TRUE</c> when the record was written.</returns>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static Bool32 GetVersion(PluginVersion* version, int size)
    {
        try
        {
            Volatile.Write(ref s_lastVersionRecordSize, size);
            if (HostLog.IsEnabled(HostLogLevel.Trace))
                HostLog.Trace(string.Create(CultureInfo.InvariantCulture,
                    $"GetVersion(0x{(nint)version:X}, size {size})"));

            if (version is null)
            {
                HostLog.Error("GetVersion: the version record address is zero.");
                return Bool32.False;
            }

            if (size > 0 && size < sizeof(PluginVersion))
            {
                // Writing 16 bytes into a buffer the host says is smaller is memory corruption: refuse, and say so.
                // The public 7.5 host passes sizeof(TPluginVersion); a host that claims less is reporting a record
                // this SDK does not know. Zero or negative claims nothing and is treated as unknown, exactly as the
                // bootstrap treats its own size argument: one rule for the two unverified values.
                HostLog.Error(string.Create(
                    CultureInfo.InvariantCulture,
                    $"GetVersion: the host reserved {size} bytes for the version record, fewer than the {sizeof(PluginVersion)} this SDK writes."));
                return Bool32.False;
            }

            var name = s_name;
            if (name is null)
            {
                HostLog.Error("GetVersion: the bootstrap has not run, there is no plugin name to report.");
                return Bool32.False;
            }

            version->Version = AbiConstants.SdkVersion;
            version->PluginName = name;
            return Bool32.True;
        }
        catch (Exception exception)
        {
            HostLog.Error("GetVersion failed.", exception);
            return Bool32.False;
        }
    }

    /// <summary>
    ///     Cheat Engine's enable callback: copies the exports record, binds the Lua API, checks it, constructs the
    ///     plugin on the first enable, attaches the runtime binding and runs <see cref="CheatEnginePlugin.OnEnable" />.
    /// </summary>
    /// <param name="exports">The host's exports record (a stack local of the host: copied, never retained).</param>
    /// <param name="pluginId">The id the host assigned.</param>
    /// <returns>
    ///     <c>TRUE</c> when the plugin is enabled when the call returns; <c>FALSE</c> on any failure, and when the call
    ///     re-enters a running <c>OnEnable</c> or <c>OnDisable</c> (see <see cref="IsReentered" />).
    /// </returns>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static Bool32 EnablePlugin(ManagedExportedFunctions* exports, uint pluginId)
    {
        try
        {
            return Enable(exports, pluginId) ? Bool32.True : Bool32.False;
        }
        catch (Exception exception)
        {
            HostLog.Error("EnablePlugin failed.", exception);
            return Bool32.False;
        }
    }

    /// <summary>
    ///     Cheat Engine's disable callback: runs <see cref="CheatEnginePlugin.OnDisable" />, detaches the runtime
    ///     binding (which neutralizes every live Lua callback) and withdraws the context.
    /// </summary>
    /// <returns>
    ///     <c>TRUE</c> after the plugin is disabled, including when <c>OnDisable</c> throws after its failure is logged;
    ///     <c>FALSE</c> when the call re-enters a running <c>OnEnable</c> or <c>OnDisable</c> (nothing is changed then;
    ///     see <see cref="IsReentered" />).
    /// </returns>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static Bool32 DisablePlugin()
    {
        try
        {
            return Disable() ? Bool32.True : Bool32.False;
        }
        catch (Exception exception)
        {
            HostLog.Error("DisablePlugin failed.", exception);
            return Bool32.False;
        }
    }

    // The gate is held only during a transition, and a transition runs plugin code (OnEnable, OnDisable) on the
    // thread that holds it. A lifecycle callback arriving on that same thread can therefore only come from inside
    // that plugin code: Cheat Engine's plugin dialog handled while OnEnable pumps the message loop through
    // MainThread.ProcessMessages, for instance. System.Threading.Lock is reentrant, so without this check the nested
    // call would run a second transition under the first and answer TRUE for a state the outer call then overturns
    // (a disable nested in OnEnable would leave the host believing the plugin is enabled while it is not). The nested
    // call is refused with FALSE, nothing is touched, and the outer transition decides the state.
    private static bool IsReentered(string callback)
    {
        if (!SGate.IsHeldByCurrentThread) return false;

        HostLog.Error(callback +
                      ": re-entered from plugin code while OnEnable or OnDisable is running on this thread (the message loop was pumped inside it); the call is refused and the outer transition decides the state.");
        return true;
    }

    private static bool Enable(ManagedExportedFunctions* exports, uint pluginId)
    {
        if (HostLog.IsEnabled(HostLogLevel.Trace))
            HostLog.Trace(string.Create(
                CultureInfo.InvariantCulture,
                $"EnablePlugin(0x{(nint)exports:X}, plugin id {pluginId}); reported record size {(exports is null ? -1 : exports->SizeOfExportedFunctions)}"));

        if (IsReentered("EnablePlugin")) return false;

        if (!TryCopyExports(exports, out var copy)) return false;

        lock (SGate)
        {
            var descriptor = s_descriptor;
            if (descriptor is null)
            {
                HostLog.Error(
                    "EnablePlugin: the bootstrap has not run (InitializeManaged was never called successfully).");
                return false;
            }

            if (s_context is not null)
            {
                // Cheat Engine never enables twice without a disable in between; if it did, the plugin is enabled.
                HostLog.Warning("EnablePlugin: the plugin is already enabled; the call is ignored.");
                return true;
            }

            // 1 + 2. The Lua API table and its self-check, before any plugin code.
            if (!TryBindLua(in copy)) return false;

            // 3. The plugin object, once, before the runtime binding exists.
            if (!TryGetOrCreatePlugin(descriptor, out var plugin)) return false;

            // 4 + 5. The ambient binding, then the plugin's own enable.
            return AttachAndEnable(plugin, in copy, pluginId);
        }
    }

    // Attaches the runtime binding (this thread is the main thread of this enable; the epoch advances), publishes
    // the context so that OnEnable can use it, and runs OnEnable inside the catch-all: a throw undoes everything.
    private static bool AttachAndEnable(CheatEnginePlugin plugin, in ManagedExportedFunctions exports, uint pluginId)
    {
        var mainThreadId = Environment.CurrentManagedThreadId;
        LuaHostBinding binding = new(exports.GetLuaState, exports.LuaPushClassInstance, mainThreadId);
        LuaRuntime.Attach(in binding);
        PluginContext context = new(in exports, pluginId, LuaRuntime.Epoch, mainThreadId, in binding);
        Volatile.Write(ref s_context, context);

        try
        {
            plugin.OnEnable();
        }
        catch (Exception exception)
        {
            HostLog.Error("EnablePlugin: OnEnable threw; the plugin stays disabled.", exception);
            Volatile.Write(ref s_context, null);
            LuaRuntime.Detach();
            return false;
        }

        if (HostLog.IsEnabled(HostLogLevel.Information))
            HostLog.Information(string.Create(CultureInfo.InvariantCulture,
                $"Plugin {pluginId} enabled (epoch {context.Epoch})."));

        return true;
    }

    // Honours the size field: a record shorter than the one this SDK knows cannot be copied safely; a longer one
    // carries fields of a later host revision, which are ignored. The copy is what survives the call.
    private static bool TryCopyExports(ManagedExportedFunctions* exports, out ManagedExportedFunctions copy)
    {
        copy = default;
        if (exports is null)
        {
            HostLog.Error("EnablePlugin: the exports record address is zero.");
            return false;
        }

        var reportedSize = exports->SizeOfExportedFunctions;
        if (reportedSize < sizeof(ManagedExportedFunctions))
        {
            HostLog.Error(string.Create(
                CultureInfo.InvariantCulture,
                $"EnablePlugin: the host reports a {reportedSize}-byte exports record, smaller than the {sizeof(ManagedExportedFunctions)}-byte record this SDK expects."));
            return false;
        }

        copy = *exports;
        if (copy.GetLuaState is null)
        {
            HostLog.Error("EnablePlugin: the exports record has no GetLuaState function.");
            return false;
        }

        return true;
    }

    // The Lua API table first (a forwarder called through an unbound table jumps to address zero), then a cheap
    // check that the host's state and the bound library agree: the registry pseudo-index must hold a table.
    private static bool TryBindLua(in ManagedExportedFunctions exports)
    {
        if (!LuaModuleLocator.TryBind(out var bindFailure))
        {
            HostLog.Error("EnablePlugin: " + bindFailure);
            return false;
        }

        var L = (lua_State*)exports.GetLuaState();
        if (L is null)
        {
            HostLog.Error("EnablePlugin: GetLuaState returned no state for the enabling thread.");
            return false;
        }

        if (LuaApi.lua_type(L, LuaApi.LUA_REGISTRYINDEX) != LuaApi.LUA_TTABLE)
        {
            HostLog.Error(
                "EnablePlugin: the Lua registry is not a table; the bound Lua library does not match the host's state.");
            return false;
        }

        return true;
    }

    // Constructed once per process, and before the runtime binding is attached on purpose: SDK use from a
    // constructor is a documented error, and here it fails loudly instead of working by luck on the first enable.
    private static bool TryGetOrCreatePlugin(PluginDescriptor descriptor,
        [NotNullWhen(true)] out CheatEnginePlugin? plugin)
    {
        plugin = s_plugin;
        if (plugin is not null) return true;

        try
        {
            plugin = descriptor.CreatePlugin();
        }
        catch (Exception exception)
        {
            HostLog.Error("EnablePlugin: the plugin constructor threw.", exception);
            return false;
        }

        if (plugin is null)
        {
            HostLog.Error("EnablePlugin: the plugin factory returned null.");
            return false;
        }

        s_plugin = plugin;
        return true;
    }

    private static bool Disable()
    {
        HostLog.Trace("DisablePlugin()");
        if (IsReentered("DisablePlugin")) return false;

        lock (SGate)
        {
            var context = s_context;
            if (context is null)
            {
                // Nothing is enabled, so nothing needs disabling; the host's bookkeeping ("disabled") is correct.
                HostLog.Warning("DisablePlugin: the plugin is not enabled; the call is ignored.");
                return true;
            }

            var plugin = s_plugin;
            if (plugin is not null)
                try
                {
                    plugin.OnDisable();
                }
                catch (Exception exception)
                {
                    HostLog.Error("DisablePlugin: OnDisable threw; the plugin is disabled anyway.", exception);
                }

            // Detach while the provider is still valid: this is where forgotten callbacks are neutralized.
            LuaRuntime.Detach();
            Volatile.Write(ref s_context, null);
            if (HostLog.IsEnabled(HostLogLevel.Information))
                HostLog.Information(string.Create(CultureInfo.InvariantCulture,
                    $"Plugin {context.PluginId} disabled."));

            return true;
        }
    }
}
