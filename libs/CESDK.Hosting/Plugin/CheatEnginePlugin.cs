using System;
using CESDK.Annotations.Lifetime;
using CESDK.Annotations.Threading;
using CESDK.Hosting.Bootstrap;
using CESDK.Hosting.Context;
using CESDK.Hosting.Diagnostics;

namespace CESDK.Hosting.Plugin;

/// <summary>
///     The base class of a Cheat Engine plugin. A plugin author derives one class from it, marks that class with
///     <c>[CheatEnginePlugin("display name")]</c> and gives it a public parameterless constructor; the entry-point
///     generator and <see cref="PluginHost" /> do the rest.
/// </summary>
/// <remarks>
///     <para>
///         <b>Lifecycle.</b> The instance is created by <see cref="IPluginFactory.Create" /> the first time Cheat Engine
///         enables the plugin, after the Lua API has been bound and checked but <i>before</i> the ambient runtime binding
///         (<c>CESDK.Lua.Runtime.LuaRuntime</c>) is attached: no SDK API is usable from the constructor, field
///         initializers or a
///         static constructor, and one that is tried there fails with an exception that turns the enable into a failure
///         reported to Cheat Engine. The same instance is reused for every later enable: the assembly is never unloaded
///         and
///         a disable/enable cycle calls <see cref="OnDisable" /> then <see cref="OnEnable" /> again on it.
///     </para>
///     <para>
///         <b>Threads.</b> Cheat Engine calls the lifecycle callbacks from its main (GUI) thread; that thread is captured
///         as
///         the main thread when the plugin is enabled, so <see cref="OnEnable" /> and <see cref="OnDisable" /> run on it
///         by
///         construction.
///     </para>
///     <para>
///         <b>Failures.</b> An exception thrown by <see cref="OnEnable" /> makes the enable fail (Cheat Engine is told
///         <c>FALSE</c>, the runtime binding is withdrawn, the exception is logged through <see cref="HostLog" />); one
///         thrown
///         by <see cref="OnDisable" /> is logged, then cleanup completes and Cheat Engine is told <c>TRUE</c> so its
///         bookkeeping matches the disabled plugin. No exception ever reaches Cheat Engine.
///     </para>
/// </remarks>
public abstract class CheatEnginePlugin
{
    /// <summary>Initializes the plugin. Runs before the plugin is enabled: do not use SDK APIs here.</summary>
    protected CheatEnginePlugin()
    {
    }

    /// <summary>
    ///     Gets the context of the current enable: plugin id, epoch, main thread. Available from <see cref="OnEnable" />
    ///     until the end of <see cref="OnDisable" />.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     The plugin is not enabled (called from the constructor, or after a
    ///     disable).
    /// </exception>
    [RequiresPluginEnabled]
    protected static PluginContext Context => PluginHost.RequireContext();

    /// <summary>
    ///     Called by the host each time Cheat Engine enables the plugin, on the main thread, with the Lua API bound and
    ///     the runtime binding attached. Register Lua functions, menus and callbacks here.
    /// </summary>
    /// <remarks>Throwing makes the enable fail; the exception is logged and does not reach Cheat Engine.</remarks>
    [RunsOnMainThread]
    protected internal abstract void OnEnable();

    /// <summary>
    ///     Called by the host each time Cheat Engine disables the plugin, on the main thread, while the Lua API and the
    ///     runtime binding are still usable. Release what <see cref="OnEnable" /> created; the runtime binding is
    ///     withdrawn right after this method returns and every remaining Lua callback is neutralized then.
    /// </summary>
    /// <remarks>Throwing is logged and reported to Cheat Engine as a failed disable; the plugin is disabled anyway.</remarks>
    [RunsOnMainThread]
    protected internal abstract void OnDisable();
}
