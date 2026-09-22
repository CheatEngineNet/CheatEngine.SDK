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
using CheatEngine.SDK.Hosting.Threading;
using CheatEngine.SDK.Lua.Interop.Api;
using CheatEngine.SDK.Lua.Interop.Types;
using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.SDK.Hosting.Bootstrap;

// The three stdcall callbacks whose addresses the bootstrap writes into the init record, and the enable/disable
// logic behind them. [UnmanagedCallersOnly] methods cannot be generic or live in a generic type: the factory they
// need is reached through the PluginDescriptor the bootstrap stored.
public static unsafe partial class PluginHost // NOSONAR: bootstrap callbacks must expose Cheat Engine's unmanaged ABI.
{
	/// <summary>
	///     Cheat Engine's version query. Writes <see cref="AbiConstants.SdkVersion" /> and the name pointer of the
	///     bootstrap into the host-owned <see cref="PluginVersion" />.
	/// </summary>
	/// <param name="version">The host's record.</param>
	/// <param name="size">
	///     The byte size the host reserved for this version record. A positive value smaller than the known record is
	///     refused; zero or a negative value is treated as unknown. This contract is independent of the bootstrap's
	///     opaque second integer.
	/// </param>
	/// <returns><c>TRUE</c> when the record was written.</returns>
	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
	private static Bool32 GetVersion(PluginVersion* version, int size)
	{
		try
		{
			Volatile.Write(ref s_lastVersionRecordSize, size);
			if (HostLog.IsEnabled(HostLogLevel.Trace))
			{
				HostLog.Trace(string.Create(CultureInfo.InvariantCulture,
					$"GetVersion(0x{(nint) version:X}, size {size})"));
			}

			if (version is null)
			{
				HostLog.Error("GetVersion: the version record address is zero.");
				return Bool32.False;
			}

			if (size > 0 && size < sizeof(PluginVersion))
			{
				// Writing 16 bytes into a buffer the host says is smaller is memory corruption: refuse, and say so.
				// The public 7.5 host passes sizeof(TPluginVersion); a host that claims less is reporting a record
				// this SDK does not know. Zero or negative claims nothing and are treated as unknown for this distinct
				// version-record callback; this says nothing about the opaque bootstrap argument.
				HostLog.Error(string.Create(
					CultureInfo.InvariantCulture,
					$"GetVersion: the host reserved {size} bytes for the version record, fewer than the {sizeof(PluginVersion)} this SDK writes."));
				return Bool32.False;
			}

			byte* name = s_name;
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
	///     is nested in or concurrent with another lifecycle transition.
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
	///     <c>FALSE</c> when another lifecycle transition is active, when the host invokes disable from a thread other
	///     than the captured plugin main thread, when it is nested in admitted Lua or dispatched work, or when Lua
	///     cleanup cannot detach the runtime. Refusals leave the lifecycle unchanged; incomplete cleanup remains in
	///     <see cref="PluginHostLifecyclePhase.Disabling" /> for diagnosis rather than reporting a false completion.
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

	// SGate only serializes the short state-selection step. The phase remains Enabling/Disabling for the full callback,
	// so a nested callback after SGate was released is still refused. A callback arriving during state selection also
	// gets an immediate FALSE rather than blocking behind a native lifecycle path.
	private static bool TryEnterLifecycleCallback(string callback)
	{
		if (SGate.IsHeldByCurrentThread)
		{
			HostLog.Error(callback +
			              ": re-entered from plugin code while OnEnable or OnDisable is running on this thread; the call is refused and the outer transition decides the state.");
			return false;
		}

		if (SGate.TryEnter())
		{
			return true;
		}

		HostLog.Error(callback +
		              ": another lifecycle transition is already running; concurrent callbacks fail immediately and do not wait for plugin code.");
		return false;
	}

	private static bool Enable(ManagedExportedFunctions* exports, uint pluginId)
	{
		TraceEnableCall(exports, pluginId);
		LifecycleStart start = TryStartEnable(out PluginDescriptor? descriptor);
		if (start is LifecycleStart.Refused)
		{
			return false;
		}

		if (start is LifecycleStart.AlreadyStable)
		{
			return true;
		}

		try
		{
			return RunEnable(exports, pluginId, descriptor!);
		}
		finally
		{
			// Any pre-attach or OnEnable failure returns to Registered. Successful enable moved to Enabled first.
			if (Phase is PluginHostLifecyclePhase.Enabling)
			{
				SetPhase(PluginHostLifecyclePhase.Registered);
			}
		}
	}

	private static void TraceEnableCall(ManagedExportedFunctions* exports, uint pluginId)
	{
		if (!HostLog.IsEnabled(HostLogLevel.Trace))
		{
			return;
		}

		HostLog.Trace(string.Create(
			CultureInfo.InvariantCulture,
			$"EnablePlugin(0x{(nint) exports:X}, plugin id {pluginId}); reported record size {(exports is null ? -1 : exports->SizeOfExportedFunctions)}"));
	}

	private static LifecycleStart TryStartEnable(out PluginDescriptor? descriptor)
	{
		descriptor = null;
		if (!TryEnterLifecycleCallback("EnablePlugin"))
		{
			return LifecycleStart.Refused;
		}

		try
		{
			descriptor = s_descriptor;
			if (descriptor is null)
			{
				HostLog.Error(
					"EnablePlugin: the bootstrap has not run (InitializeManaged was never called successfully).");
				return LifecycleStart.Refused;
			}

			if (Phase is PluginHostLifecyclePhase.Enabled)
			{
				HostLog.Warning("EnablePlugin: the plugin is already enabled; the call is ignored.");
				return LifecycleStart.AlreadyStable;
			}

			if (Phase is not PluginHostLifecyclePhase.Registered)
			{
				HostLog.Error("EnablePlugin: the plugin lifecycle is in " + Phase +
				              "; enable is valid only from Registered.");
				return LifecycleStart.Refused;
			}

			SetPhase(PluginHostLifecyclePhase.Enabling);
			return LifecycleStart.Started;
		}
		finally
		{
			// The phase, rather than SGate, guards the long-running transition after this point.
			SGate.Exit();
		}
	}

	private static bool RunEnable(ManagedExportedFunctions* exports, uint pluginId, PluginDescriptor descriptor)
	{
		Volatile.Write(ref s_incompleteEnableCleanup, 0);
		Volatile.Write(ref s_incompleteEnableCleanupActive, 0);
		if (!TryCopyExports(exports, out ManagedExportedFunctions copy))
		{
			return false;
		}

		if (!TryBindLua(in copy))
		{
			return false;
		}

		if (!TryGetOrCreatePlugin(descriptor, out CheatEnginePlugin? plugin))
		{
			return false;
		}

		return AttachAndEnable(plugin, in copy, pluginId);
	}

	// Attaches the runtime binding (this thread is the main thread of this enable; the epoch advances), publishes
	// the context so that OnEnable can use it, and runs OnEnable. It opens dispatch admission only after OnEnable
	// succeeds. IsEnabled intentionally stays false until then: the published context is lifecycle-only in Enabling.
	private static bool AttachAndEnable(CheatEnginePlugin plugin, in ManagedExportedFunctions exports, uint pluginId)
	{
		bool runtimeAttached = false;
		CancellationTokenSource? shutdown = null;
		try
		{
			PluginContext context =
				AttachAndPublishEnableContext(in exports, pluginId, out runtimeAttached, out shutdown);
			plugin.OnEnable();
			CompleteEnable(context, shutdown);
			return true;
		}
		catch (Exception exception)
		{
			HostLog.Error("EnablePlugin: OnEnable threw; the plugin stays disabled.", exception);
			return false;
		}
		finally
		{
			if (Phase is not PluginHostLifecyclePhase.Enabled)
			{
				CleanupFailedEnable(runtimeAttached, shutdown);
			}
		}
	}

	private static PluginContext AttachAndPublishEnableContext(
		in ManagedExportedFunctions exports,
		uint pluginId,
		out bool runtimeAttached,
		[NotNull] out CancellationTokenSource? shutdown)
	{
		int mainThreadId = Environment.CurrentManagedThreadId;
		LuaHostBinding binding = new(exports.GetLuaState, exports.LuaPushClassInstance, mainThreadId);
		runtimeAttached = false;
		shutdown = null;

		LuaRuntime.Attach(in binding);
		runtimeAttached = true;
		shutdown = CreateShutdownSource();
		PluginContext context = new(in exports, pluginId, LuaRuntime.Epoch, mainThreadId, in binding, shutdown.Token);
		Volatile.Write(ref s_context, context);
		return context;
	}

	private static void CompleteEnable(PluginContext context, CancellationTokenSource shutdown)
	{
		// OnEnable observes Enabling and cannot admit worker dispatch. Only its successful completion opens the gate.
		OpenMainThreadWorkAdmission(shutdown);
		SetPhase(PluginHostLifecyclePhase.Enabled);

		if (HostLog.IsEnabled(HostLogLevel.Information))
		{
			HostLog.Information(string.Create(CultureInfo.InvariantCulture,
				$"Plugin {context.PluginId} enabled (epoch {context.Epoch})."));
		}
	}

	private static void CleanupFailedEnable(bool runtimeAttached, CancellationTokenSource? shutdown)
	{
		bool cleanupSucceeded = false;
		try
		{
			if (shutdown is not null)
			{
				CloseMainThreadWorkAdmissionAndSignalShutdown(Volatile.Read(ref s_context));
			}

			if (runtimeAttached)
			{
				LuaRuntime.CloseOperationAdmissionAndDrain();
				LuaRuntime.Detach();
			}

			cleanupSucceeded = true;
		}
		catch (Exception exception)
		{
			HostLog.Error("EnablePlugin: Lua cleanup after a failed enable threw; shutdown remains incomplete.",
				exception);
			if (runtimeAttached)
			{
				Volatile.Write(ref s_incompleteEnableCleanup, 1);
				SetPhase(PluginHostLifecyclePhase.Disabling);
			}
		}

		if (!cleanupSucceeded)
		{
			return;
		}

		Volatile.Write(ref s_context, null);
		Volatile.Write(ref s_incompleteEnableCleanup, 0);
		Volatile.Write(ref s_incompleteEnableCleanupActive, 0);
		if (shutdown is not null)
		{
			EndMainThreadWorkAdmission();
		}
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

		int reportedSize = exports->SizeOfExportedFunctions;
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
		if (!LuaModuleLocator.TryBind(out string? bindFailure))
		{
			HostLog.Error("EnablePlugin: " + bindFailure);
			return false;
		}

		lua_State* L = (lua_State*) exports.GetLuaState();
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
		if (plugin is not null)
		{
			return true;
		}

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
		LifecycleStart start = TryStartDisable(out PluginContext? context, out CheatEnginePlugin? plugin);
		if (start is LifecycleStart.Refused)
		{
			return false;
		}

		if (start is LifecycleStart.AlreadyStable)
		{
			return true;
		}

		bool cleanupSucceeded = false;
		try
		{
			RunDisable(context!, plugin);
		}
		finally
		{
			// A failed detach leaves the attached runtime and context available for diagnosis rather than publishing a
			// successful Registered state. The native callback must report that incomplete shutdown to the host.
			cleanupSucceeded = CleanupDisable();
		}

		// Keep the retry claimed through detach failure and its synchronous error log. Only after the whole attempt
		// has unwound may another host request claim the pending cleanup again.
		Volatile.Write(ref s_incompleteEnableCleanupActive, 0);

		if (cleanupSucceeded && HostLog.IsEnabled(HostLogLevel.Information))
		{
			HostLog.Information($"Plugin {context!.PluginId} disabled.");
		}

		return cleanupSucceeded;
	}

	private static LifecycleStart TryStartDisable(
		out PluginContext? context,
		out CheatEnginePlugin? plugin)
	{
		context = null;
		plugin = null;
		if (!CanStartDisable())
		{
			return LifecycleStart.Refused;
		}

		if (!TryEnterLifecycleCallback("DisablePlugin"))
		{
			return LifecycleStart.Refused;
		}

		try
		{
			return SelectDisableStart(out context, out plugin);
		}
		finally
		{
			SGate.Exit();
		}
	}

	private static LifecycleStart SelectDisableStart(
		out PluginContext? context,
		out CheatEnginePlugin? plugin)
	{
		context = null;
		plugin = null;
		if (Phase is PluginHostLifecyclePhase.Registered or PluginHostLifecyclePhase.Uninitialized)
		{
			HostLog.Warning("DisablePlugin: the plugin is not enabled; the call is ignored.");
			return LifecycleStart.AlreadyStable;
		}

		context = s_context;
		if (context is null)
		{
			HostLog.Error("DisablePlugin: the plugin lifecycle is in " + Phase +
			              "; disable is valid only from Enabled or incomplete failed-enable cleanup.");
			return LifecycleStart.Refused;
		}

		if (Phase is PluginHostLifecyclePhase.Disabling)
		{
			if (Volatile.Read(ref s_incompleteEnableCleanup) == 0
			    || Volatile.Read(ref s_incompleteEnableCleanupActive) != 0)
			{
				HostLog.Error("DisablePlugin: the plugin lifecycle is in " + Phase +
				              "; a disable transition is already completing.");
				return LifecycleStart.Refused;
			}

			return TryStartIncompleteDisable(context, out plugin);
		}

		if (Phase is not PluginHostLifecyclePhase.Enabled)
		{
			HostLog.Error("DisablePlugin: the plugin lifecycle is in " + Phase +
			              "; disable is valid only from Enabled or incomplete failed-enable cleanup.");
			return LifecycleStart.Refused;
		}

		if (!context.IsMainThread)
		{
			HostLog.Error(
				"DisablePlugin: the host invoked disable from a thread other than the captured plugin main thread; cleanup is refused because it could not safely drain GUI-bound work.");
			return LifecycleStart.Refused;
		}

		SetPhase(PluginHostLifecyclePhase.Disabling);
		plugin = s_plugin;
		return LifecycleStart.Started;
	}

	private static bool CanStartDisable()
	{
		if (LuaRuntime.IsOperationAdmittedOnCurrentThread)
		{
			HostLog.Error(
				"DisablePlugin: disable was requested from an admitted Lua operation; the request is refused because shutdown would wait for that operation to return.");
			return false;
		}

		if (MainThreadDispatcher.IsExecutingInlineWorkOnCurrentThread)
		{
			HostLog.Error(
				"DisablePlugin: disable was requested from inline main-thread work; the request is refused because shutdown would wait for that work to return.");
			return false;
		}

		if (MainThreadDispatcher.IsExecutingWorkOnCurrentThread)
		{
			HostLog.Error(
				"DisablePlugin: disable was requested from dispatched main-thread work; the request is refused because shutdown would wait for that work to return.");
			return false;
		}

		return true;
	}

	private static LifecycleStart TryStartIncompleteDisable(PluginContext context, out CheatEnginePlugin? plugin)
	{
		plugin = null;
		if (!context.IsMainThread)
		{
			HostLog.Error(
				"DisablePlugin: retrying incomplete enable cleanup from a different thread is refused; use the captured plugin main thread.");
			return LifecycleStart.Refused;
		}

		// SGate is held by the caller, so this claim closes the re-entrant window before the transition is returned.
		Volatile.Write(ref s_incompleteEnableCleanupActive, 1);

		return LifecycleStart.Started;
	}

	private static void RunDisable(PluginContext context, CheatEnginePlugin? plugin)
	{
		// Main-thread work admission is closed outside SGate, so an admitted worker can finish and release its lease.
		CloseMainThreadWorkAdmissionAndSignalShutdown(context);
		// Subscription callbacks are made inert before plugin state is torn down. Their host-object unregister actions
		// still run later in LuaRuntime.Detach, while the attached state can be reached.
		LuaRuntime.CloseHostSubscriptionAdmissionAndDrain();

		if (plugin is not null)
		{
			try
			{
				// The runtime is still attached, so plugin cleanup can release Lua resources before Detach.
				plugin.OnDisable();
			}
			catch (Exception exception)
			{
				HostLog.Error(
					"DisablePlugin: OnDisable threw; cleanup continues and detach determines the final disable result.",
					exception);
			}
		}
	}

	private static bool CleanupDisable()
	{
		try
		{
			// The operation gate has already shut out every admitted Lua caller before callback neutralization.
			LuaRuntime.CloseOperationAdmissionAndDrain();
			LuaRuntime.Detach();

			Volatile.Write(ref s_context, null);
			Volatile.Write(ref s_incompleteEnableCleanup, 0);
			EndMainThreadWorkAdmission();
			SetPhase(PluginHostLifecyclePhase.Registered);
			return true;
		}
		catch (Exception exception)
		{
			HostLog.Error(
				"DisablePlugin: Lua detach threw; shutdown remains incomplete and the lifecycle stays Disabling.",
				exception);
			return false;
		}
	}

	private enum LifecycleStart
	{
		Refused,
		AlreadyStable,
		Started
	}
}
