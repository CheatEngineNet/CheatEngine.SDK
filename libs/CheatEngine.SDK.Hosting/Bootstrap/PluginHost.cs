using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using CheatEngine.SDK.Abi;
using CheatEngine.SDK.Abi.Managed;
using CheatEngine.SDK.Hosting.Context;
using CheatEngine.SDK.Hosting.Diagnostics;
using CheatEngine.SDK.Hosting.Plugin;
using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.SDK.Hosting.Bootstrap;

/// <summary>
///     The plugin lifecycle runtime of the managed (hostfxr) load path: fills the init record for the generated
///     <c>CESDK.CESDK.CEPluginInitialize</c>, owns the three <c>stdcall</c> lifecycle callbacks Cheat Engine calls
///     through it, and drives the plugin between them. One plugin per assembly load context, which is what Cheat Engine
///     gives every plugin, so all state is static.
/// </summary>
/// <remarks>
///     <para>
///         <b>Sequence, as Cheat Engine drives it</b>: <see cref="InitializeManaged{TFactory}" /> twice (name query,
///         then load), then through the record's pointers <c>GetVersion</c>, <c>EnablePlugin</c>, and later
///         <c>DisablePlugin</c> / <c>EnablePlugin</c> for every cycle the user requests in the plugin dialog. The
///         assembly is never unloaded, so the static state below survives every cycle and the plugin object is
///         constructed once.
///     </para>
///     <para>
///         <b>Nothing escapes.</b> Every entry native code calls is a catch-all that converts a failure into 0 or
///         <c>FALSE</c> and writes the reason to <see cref="HostLog" />. <see cref="InitializeManaged{TFactory}" /> itself
///         never
///         throws; the generated caller wraps it in a second catch-all for the case where this assembly cannot even be
///         loaded.
///     </para>
///     <para>
///         <b>Threads.</b> Cheat Engine calls the bootstrap and the callbacks from its main thread (inferred from the
///         public
///         7.5 host source). The thread that runs a successful enable is captured as the main thread of that enable. The
///         readers (<see cref="IsInitialized" />, <see cref="IsEnabled" />, <see cref="Context" />) are lock-free and
///         usable
///         from any thread; the transitions are serialized by one lock.
///     </para>
/// </remarks>
public static unsafe partial class PluginHost
{
    private static readonly Lock SGate = new();
    private static readonly Lock SAdmissionGate = new();
    private static readonly ManualResetEventSlim SNoAdmittedMainThreadWork = new(initialState: true);

    // Written once by the first successful InitializeManaged, never cleared in production.
    private static PluginDescriptor? s_descriptor;
    private static byte* s_name;

    // Lifecycle state, written under s_gate.
    private static CheatEnginePlugin? s_plugin;
    private static PluginContext? s_context;
    private static CancellationTokenSource? s_shutdown;
    private static int s_admittedMainThreadWork;
    private static int s_acceptingMainThreadWork;
    private static int s_incompleteEnableCleanup;
    private static int s_phase;
    private static int s_lastInitRecordArgument = -1;
    private static int s_lastVersionRecordSize = -1;

    /// <summary>Gets a value indicating whether the bootstrap has run: a factory is registered and the name buffer exists.</summary>
    public static bool IsInitialized => Phase is not PluginHostLifecyclePhase.Uninitialized;

    /// <summary>Gets the current stable or transitional lifecycle phase. Lock-free; safe from any thread.</summary>
    public static PluginHostLifecyclePhase Phase => (PluginHostLifecyclePhase)Volatile.Read(ref s_phase);

    /// <summary>
    ///     Gets a value indicating whether the plugin is enabled: an enable succeeded and no disable followed. Lock-free;
    ///     any thread.
    /// </summary>
    public static bool IsEnabled => Phase is PluginHostLifecyclePhase.Enabled;

    /// <summary>
    ///     Gets the context of the current enable, or <see langword="null" /> while the plugin is disabled. Lock-free;
    ///     any thread.
    /// </summary>
    public static PluginContext? Context => Volatile.Read(ref s_context);

    /// <summary>
    ///     Gets the opaque second integer from the most recent bootstrap call, or -1 before the first one. Diagnostic
    ///     only: CE 7.7's meaning for this value is not established, so Hosting records and forwards it without treating
    ///     it as a record size, version, or capability value.
    /// </summary>
    public static int LastInitRecordArgument => Volatile.Read(ref s_lastInitRecordArgument);

    /// <summary>
    ///     Gets the size argument of the most recent <c>GetVersion</c> call, or -1 before the first one. This is a
    ///     distinct version-record contract; it does not establish the meaning of the bootstrap's opaque
    ///     <see cref="LastInitRecordArgument" />.
    /// </summary>
    public static int LastVersionRecordSize => Volatile.Read(ref s_lastVersionRecordSize);

    /// <summary>The plugin instance, once constructed. Tests only.</summary>
    internal static CheatEnginePlugin? PluginForTests => Volatile.Read(ref s_plugin);

    /// <summary>
    ///     Admits one synchronous main-thread dispatch associated with <paramref name="context" />.
    /// </summary>
    /// <remarks>
    ///     The returned lease must be disposed after the host's <c>synchronize</c> call returns. Disable closes this
    ///     gate before signalling <see cref="PluginContext.ShutdownToken" /> and waits for all such leases before it
    ///     detaches Lua and neutralizes its callbacks.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///     <paramref name="context" /> is stale, or the plugin is stopping and no longer accepts new main-thread work.
    /// </exception>
    internal static MainThreadWorkAdmission AdmitMainThreadWork(PluginContext context)
    {
        lock (SAdmissionGate)
        {
            if (Phase is not PluginHostLifecyclePhase.Enabled ||
                !ReferenceEquals(context, Volatile.Read(ref s_context)) ||
                Volatile.Read(ref s_acceptingMainThreadWork) == 0)
                throw new InvalidOperationException(
                    "The plugin is stopping or disabled and no longer accepts new main-thread dispatch work.");

            if (s_admittedMainThreadWork == 0) SNoAdmittedMainThreadWork.Reset();

            checked
            {
                s_admittedMainThreadWork++;
            }

            return new MainThreadWorkAdmission();
        }
    }

    /// <summary>
    ///     The managed bootstrap: fills the <see cref="PluginInitRecord" /> at <paramref name="initRecord" /> with the
    ///     plugin name and the three lifecycle callbacks. Called by the generated <c>CESDK.CESDK.CEPluginInitialize</c>;
    ///     Cheat Engine calls that twice per plugin, so this method is idempotent: the second call writes the same
    ///     values, including the same name pointer.
    /// </summary>
    /// <typeparam name="TFactory">The factory the entry-point generator emitted (or a hand-written one).</typeparam>
    /// <param name="initRecord">Address of the host-owned record; 36 bytes, byte-packed.</param>
    /// <param name="hostArgument">
    ///     The opaque second integer received from Cheat Engine. Its CE 7.7 meaning has not been live-verified; this
    ///     method records it for diagnostics but does not derive a record size or any other behavior from it.
    /// </param>
    /// <returns>1 (<see cref="ManagedEntryPoint.Success" />) when the record was written; 0 otherwise.</returns>
    /// <remarks>
    ///     Never throws. Fails, with an entry in <see cref="HostLog" />, when <paramref name="initRecord" /> is zero, the
    ///     process is not x64, the factory's name cannot be
    ///     allocated, or a <i>different</i> factory type was registered by an earlier call in this load context (one
    ///     plugin per load context; the first factory wins deterministically). The name buffer is allocated on the first
    ///     successful call and never freed: Cheat Engine keeps reading through the pointer.
    /// </remarks>
    public static int InitializeManaged<TFactory>(nint initRecord, int hostArgument)
        where TFactory : IPluginFactory
    {
        try
        {
            Volatile.Write(ref s_lastInitRecordArgument, hostArgument);
            if (HostLog.IsEnabled(HostLogLevel.Trace))
                HostLog.Trace(string.Create(CultureInfo.InvariantCulture,
                    $"InitializeManaged<{typeof(TFactory)}>(0x{initRecord:X}, host argument {hostArgument})"));

            if (initRecord == 0)
            {
                HostLog.Error("InitializeManaged: the init record address is zero.");
                return ManagedEntryPoint.Failure;
            }

            if (!AbiArchitecture.IsSupported)
            {
                HostLog.Error("InitializeManaged: this SDK is validated for x64 processes only.");
                return ManagedEntryPoint.Failure;
            }

            if (!TryRegisterFactory<TFactory>(out var name)) return ManagedEntryPoint.Failure;

            // Written field by field through the packed layout: 36 bytes, no tail padding, no structure marshalling.
            var record = (PluginInitRecord*)initRecord;
            record->Name = name;
            record->GetVersion = &GetVersion;
            record->EnablePlugin = &EnablePlugin;
            record->DisablePlugin = &DisablePlugin;
            record->Version = AbiConstants.SdkVersion;
            return ManagedEntryPoint.Success;
        }
        catch (Exception exception)
        {
            HostLog.Error("InitializeManaged failed.", exception);
            return ManagedEntryPoint.Failure;
        }
    }

    // First call: copies the name out of the factory and pins the factory type. Later calls: the same type gets the
    // same name pointer; a different type is rejected, deterministically, for the rest of the process.
    private static bool TryRegisterFactory<TFactory>(out byte* name)
        where TFactory : IPluginFactory
    {
        lock (SGate)
        {
            var registered = s_descriptor;
            if (registered is null)
            {
                name = AnsiNameBuffer.Allocate(TFactory.Utf8Name);
                s_name = name;
                Volatile.Write(ref s_descriptor, new PluginDescriptor<TFactory>());
                SetPhase(PluginHostLifecyclePhase.Registered);
                return true;
            }

            if (registered.FactoryType != typeof(TFactory))
            {
                HostLog.Error(
                    "InitializeManaged: a plugin factory of type " + registered.FactoryType +
                    " is already registered in this load context; "
                    + typeof(TFactory) + " is rejected. One plugin per assembly.");
                name = null;
                return false;
            }

            name = s_name;
            return true;
        }
    }

    /// <summary>The current context, or an exception for code that cannot proceed without one.</summary>
    /// <exception cref="InvalidOperationException">The plugin is not enabled.</exception>
    internal static PluginContext RequireContext()
    {
        var context = Volatile.Read(ref s_context);
        if (context is null) ThrowNotEnabled();

        return context;
    }

    /// <summary>
    ///     Returns the host to its never-bootstrapped state: disables the plugin if it is enabled (without calling
    ///     <see cref="CheatEnginePlugin.OnDisable" />), forgets the factory, the plugin instance and the name buffer (which
    ///     is leaked, as in production). Tests only: in Cheat Engine the state lives as long as the process.
    /// </summary>
    internal static void ResetForTests()
    {
        lock (SGate)
        {
            CloseMainThreadWorkAdmissionAndSignalShutdown(Volatile.Read(ref s_context));
            if (s_context is not null)
            {
                LuaRuntime.CloseOperationAdmissionAndDrain();
                LuaRuntime.Detach();
                Volatile.Write(ref s_context, null);
            }

            EndMainThreadWorkAdmission();

            s_plugin = null;
            s_name = null;
            Volatile.Write(ref s_descriptor, null);
            Volatile.Write(ref s_incompleteEnableCleanup, 0);
            SetPhase(PluginHostLifecyclePhase.Uninitialized);
            Volatile.Write(ref s_lastInitRecordArgument, -1);
            Volatile.Write(ref s_lastVersionRecordSize, -1);
        }
    }

    private static void SetPhase(PluginHostLifecyclePhase phase)
    {
        Volatile.Write(ref s_phase, (int)phase);
    }

    // The admission lock makes the "close then drain" boundary exact: a worker either obtains a lease before
    // Disable closes the gate, or observes the closed gate and never becomes work Disable must wait for.
    private static CancellationTokenSource CreateShutdownSource()
    {
        lock (SAdmissionGate)
        {
            var shutdown = new CancellationTokenSource();
            s_shutdown = shutdown;
            return shutdown;
        }
    }

    private static void OpenMainThreadWorkAdmission(CancellationTokenSource shutdown)
    {
        lock (SAdmissionGate)
        {
            if (!ReferenceEquals(s_shutdown, shutdown))
                throw new InvalidOperationException(
                    "The lifecycle shutdown source was replaced before work admission opened.");

            Volatile.Write(ref s_acceptingMainThreadWork, 1);
        }
    }

    private static void CloseMainThreadWorkAdmissionAndSignalShutdown(PluginContext? context)
    {
        CancellationTokenSource? shutdown;
        lock (SAdmissionGate)
        {
            Volatile.Write(ref s_acceptingMainThreadWork, 0);
            shutdown = s_shutdown;
        }

        if (shutdown is not null)
            try
            {
                shutdown.Cancel(throwOnFirstException: false);
            }
            catch (Exception exception)
            {
                // A cancellation registration is plugin code. It cannot prevent the required drain and detach.
                HostLog.Error("A plugin shutdown callback threw while DisablePlugin was signalling shutdown.",
                    exception);
            }

        DrainAdmittedMainThreadWork(context);
    }

    private static void DrainAdmittedMainThreadWork(PluginContext? context)
    {
        // A worker already admitted through synchronize may be waiting for the GUI queue at exactly the point Disable
        // begins. Waiting blindly on the GUI thread would deadlock it. When the host supplied CheckSynchronize, pump
        // its queue until the last admitted work item releases its lease. MainThread.Invoke refuses worker dispatches
        // without that slot, so an admitted GUI-bound work item always has this drain route.
        while (!SNoAdmittedMainThreadWork.Wait(0, CancellationToken.None))
        {
            if (context is not null && context.IsMainThread)
            {
                var checkSynchronize = context.Exports.CheckSynchronize;
                if (checkSynchronize is not null)
                {
                    checkSynchronize(0);
                    Thread.Yield();
                    continue;
                }
            }

            SNoAdmittedMainThreadWork.Wait(CancellationToken.None);
        }
    }

    private static void EndMainThreadWorkAdmission()
    {
        CancellationTokenSource? shutdown;
        lock (SAdmissionGate)
        {
            Volatile.Write(ref s_acceptingMainThreadWork, 0);
            shutdown = s_shutdown;
            s_shutdown = null;
        }

        shutdown?.Dispose();
    }

    private static void ReleaseMainThreadWorkAdmission()
    {
        lock (SAdmissionGate)
        {
            if (s_admittedMainThreadWork <= 0)
                throw new InvalidOperationException("The main-thread work admission was released more than once.");

            s_admittedMainThreadWork--;
            if (s_admittedMainThreadWork == 0) SNoAdmittedMainThreadWork.Set();
        }
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowNotEnabled()
    {
        throw new InvalidOperationException(
            "The plugin is not enabled: Cheat Engine has not called EnablePlugin, or has called DisablePlugin since.");
    }

    /// <summary>A single admitted main-thread dispatch. Internal so only Hosting can close the lifecycle work gate.</summary>
    internal sealed class MainThreadWorkAdmission : IDisposable
    {
        private int _released;

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) ReleaseMainThreadWorkAdmission();
        }
    }
}
