using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using CESDK.Abi;
using CESDK.Abi.Managed;
using CESDK.Hosting.Context;
using CESDK.Hosting.Diagnostics;
using CESDK.Hosting.Plugin;
using CESDK.Lua.Runtime;

namespace CESDK.Hosting.Bootstrap;

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

    // Written once by the first successful InitializeManaged, never cleared in production.
    private static PluginDescriptor? s_descriptor;
    private static byte* s_name;

    // Lifecycle state, written under s_gate.
    private static CheatEnginePlugin? s_plugin;
    private static PluginContext? s_context;
    private static int s_lastInitRecordSize = -1;
    private static int s_lastVersionRecordSize = -1;

    /// <summary>Gets a value indicating whether the bootstrap has run: a factory is registered and the name buffer exists.</summary>
    public static bool IsInitialized => Volatile.Read(ref s_descriptor) is not null;

    /// <summary>
    ///     Gets a value indicating whether the plugin is enabled: an enable succeeded and no disable followed. Lock-free;
    ///     any thread.
    /// </summary>
    public static bool IsEnabled => Volatile.Read(ref s_context) is not null;

    /// <summary>
    ///     Gets the context of the current enable, or <see langword="null" /> while the plugin is disabled. Lock-free;
    ///     any thread.
    /// </summary>
    public static PluginContext? Context => Volatile.Read(ref s_context);

    /// <summary>
    ///     Gets the <c>size</c> argument of the most recent bootstrap call, or -1 before the first one. Diagnostic: the
    ///     value Cheat Engine passes is unverified (36, the packed record; 40, the unpacked one; or something else).
    /// </summary>
    public static int LastInitRecordSize => Volatile.Read(ref s_lastInitRecordSize);

    /// <summary>
    ///     Gets the size argument of the most recent <c>GetVersion</c> call, or -1 before the first one. Diagnostic, like
    ///     <see cref="LastInitRecordSize" />.
    /// </summary>
    public static int LastVersionRecordSize => Volatile.Read(ref s_lastVersionRecordSize);

    /// <summary>The plugin instance, once constructed. Tests only.</summary>
    internal static CheatEnginePlugin? PluginForTests => Volatile.Read(ref s_plugin);

    /// <summary>
    ///     The managed bootstrap: fills the <see cref="PluginInitRecord" /> at <paramref name="initRecord" /> with the
    ///     plugin name and the three lifecycle callbacks. Called by the generated <c>CESDK.CESDK.CEPluginInitialize</c>;
    ///     Cheat Engine calls that twice per plugin, so this method is idempotent: the second call writes the same
    ///     values, including the same name pointer.
    /// </summary>
    /// <typeparam name="TFactory">The factory the entry-point generator emitted (or a hand-written one).</typeparam>
    /// <param name="initRecord">Address of the host-owned record; 36 bytes, byte-packed.</param>
    /// <param name="size">
    ///     The byte size the host reports for the record. Its value is unverified, so it is only used defensively: a
    ///     positive value smaller than the record refuses the call (writing would overrun the host's variable); zero or
    ///     a negative value is treated as unknown and the record is written.
    /// </param>
    /// <returns>1 (<see cref="ManagedEntryPoint.Success" />) when the record was written; 0 otherwise.</returns>
    /// <remarks>
    ///     Never throws. Fails, with an entry in <see cref="HostLog" />, when <paramref name="initRecord" /> is zero, the
    ///     process is not x64, <paramref name="size" /> is positive and too small, the factory's name cannot be
    ///     allocated, or a <i>different</i> factory type was registered by an earlier call in this load context (one
    ///     plugin per load context; the first factory wins deterministically). The name buffer is allocated on the first
    ///     successful call and never freed: Cheat Engine keeps reading through the pointer.
    /// </remarks>
    public static int InitializeManaged<TFactory>(nint initRecord, int size)
        where TFactory : IPluginFactory
    {
        try
        {
            Volatile.Write(ref s_lastInitRecordSize, size);
            if (HostLog.IsEnabled(HostLogLevel.Trace))
                HostLog.Trace(string.Create(CultureInfo.InvariantCulture,
                    $"InitializeManaged<{typeof(TFactory)}>(0x{initRecord:X}, size {size})"));

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

            if (size > 0 && size < sizeof(PluginInitRecord))
            {
                HostLog.Error(string.Create(
                    CultureInfo.InvariantCulture,
                    $"InitializeManaged: the host reports a {size}-byte init record, smaller than the {sizeof(PluginInitRecord)}-byte record this SDK writes."));
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
            if (s_context is not null)
            {
                LuaRuntime.Detach();
                Volatile.Write(ref s_context, null);
            }

            s_plugin = null;
            s_name = null;
            Volatile.Write(ref s_descriptor, null);
            Volatile.Write(ref s_lastInitRecordSize, -1);
            Volatile.Write(ref s_lastVersionRecordSize, -1);
        }
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowNotEnabled()
    {
        throw new InvalidOperationException(
            "The plugin is not enabled: Cheat Engine has not called EnablePlugin, or has called DisablePlugin since.");
    }
}
