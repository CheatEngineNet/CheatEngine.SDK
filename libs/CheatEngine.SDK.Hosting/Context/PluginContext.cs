using System;
using CheatEngine.SDK.Abi.Managed;
using CheatEngine.SDK.Hosting.Bootstrap;
using CheatEngine.SDK.Hosting.Threading;
using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.SDK.Hosting.Context;

/// <summary>
///     What one enable established: the copy of Cheat Engine's exported-functions record, the plugin id, the identity
///     of the main thread and the runtime epoch. Immutable; a new instance is published by every successful enable and
///     withdrawn by the disable, so a reference a plugin keeps across a disable/enable cycle is stale
///     (<see cref="IsCurrent" />).
/// </summary>
/// <remarks>
///     <para>
///         The exports record lives in a host stack frame during the enable callback; this object holds the copy, so the
///         host function pointers (which are addresses of Cheat Engine code, valid for the process lifetime) can be called
///         at any later time. Per-operation Lua state acquisition is <see cref="LuaRuntime.AcquireState" />: this type
///         does
///         not duplicate it, it only exposes the binding that was attached (<see cref="HostBinding" />).
///     </para>
///     <para>Safe to read from any thread. The main-thread-only operations of the record are on <see cref="MainThread" />.</para>
/// </remarks>
public sealed unsafe class PluginContext
{
    private readonly ManagedExportedFunctions _exports;

    internal PluginContext(in ManagedExportedFunctions exports, uint pluginId, int epoch, int mainThreadId,
        in LuaHostBinding hostBinding)
    {
        _exports = exports;
        PluginId = pluginId;
        Epoch = epoch;
        MainThreadId = mainThreadId;
        HostBinding = hostBinding;
    }

    /// <summary>Gets the plugin id Cheat Engine assigned in the enable callback.</summary>
    public uint PluginId { get; }

    /// <summary>
    ///     Gets the <see cref="LuaRuntime.Epoch" /> this enable established. Every enable advances it, so a value cached
    ///     during an earlier enable identifies references and callbacks that are no longer valid.
    /// </summary>
    public int Epoch { get; }

    /// <summary>Gets the managed thread id of the thread that ran the enable callback: Cheat Engine's main thread.</summary>
    public int MainThreadId { get; }

    /// <summary>
    ///     Gets the binding that was attached to <see cref="LuaRuntime" /> for this enable: the state provider and
    ///     host-object pusher of the exports record.
    /// </summary>
    /// <remarks>
    ///     Internal on purpose: <see cref="LuaHostBinding" />'s <c>StateProvider</c>/<c>HostObjectPusher</c> are raw host
    ///     function addresses, and this <see cref="PluginContext" /> is reachable from ordinary, non-<c>unsafe</c> plugin
    ///     code via <c>CheatEnginePlugin.Context</c> — CheatEngine.SDK.Hosting's public surface speaks spans, structs
    ///     and handles, never raw pointers. This is <c>CheatEngine.SDK.Hosting</c>'s own wiring detail for attaching
    ///     <see cref="LuaRuntime" />, not a plugin-facing capability.
    /// </remarks>
    internal LuaHostBinding HostBinding { get; }

    /// <summary>Gets a value indicating whether the calling thread is the main thread of this context.</summary>
    public bool IsMainThread => Environment.CurrentManagedThreadId == MainThreadId;

    /// <summary>
    ///     Gets a value indicating whether this is the context of the current enable (the plugin is enabled and has not
    ///     been disabled or re-enabled since).
    /// </summary>
    public bool IsCurrent => ReferenceEquals(PluginHost.Context, this);

    /// <summary>
    ///     Gets the size the host reported for its exports record (<c>sizeofExportedFunctions</c>); 48 for the only known
    ///     revision on x64.
    /// </summary>
    public int ReportedExportsSize => _exports.SizeOfExportedFunctions;

    /// <summary>Gets a value indicating whether the host supplied the message-pump slot (<c>ProcessMessages</c>).</summary>
    public bool HasProcessMessages => _exports.ProcessMessages is not null;

    /// <summary>Gets a value indicating whether the host supplied the queued-call slot (<c>CheckSynchronize</c>).</summary>
    public bool HasCheckSynchronize => _exports.CheckSynchronize is not null;

    /// <summary>The copied record, for the host-side operations of this assembly.</summary>
    internal ref readonly ManagedExportedFunctions Exports => ref _exports;
}
