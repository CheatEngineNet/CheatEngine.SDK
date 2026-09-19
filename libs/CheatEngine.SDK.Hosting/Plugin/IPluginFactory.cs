using System;
using CheatEngine.SDK.Hosting.Bootstrap;

namespace CheatEngine.SDK.Hosting.Plugin;

/// <summary>
///     What <see cref="PluginHost.InitializeManaged{TFactory}" /> needs from the plugin assembly: a way to construct the
///     plugin without reflection and its display name as UTF-8. Implemented by the <c>file</c>-scoped factory that
///     <c>CheatEngine.SDK.SourceGenerators.EntryPoint</c> emits next to the entry point; a hand-written bootstrap
///     implements it the same way.
/// </summary>
/// <remarks>
///     Static abstract members make the factory a type argument rather than an object: the host reaches
///     <see cref="Create" /> and <see cref="Utf8Name" /> through the generic parameter, allocation-free and trim-safe.
///     <see cref="Utf8Name" /> is read once, during the first bootstrap call, and copied to native memory; a <c>u8</c>
///     literal is the intended implementation. <see cref="Create" /> is called on the first enable that succeeds in
///     constructing the plugin: the instance it returns is kept for the rest of the process, while a constructor that
///     throws (or a <see langword="null" /> result) fails that enable and is tried again on the next one.
/// </remarks>
public interface IPluginFactory
{
    /// <summary>Gets the plugin's display name in UTF-8, without a terminating NUL. Read once, never on a hot path.</summary>
    public static abstract ReadOnlySpan<byte> Utf8Name { get; }

    /// <summary>
    ///     Constructs the plugin instance, after the Lua API is bound and before the runtime binding is attached. Called
    ///     until it returns an instance: once when it succeeds on the first enable; again on the next enable when it
    ///     threw or returned <see langword="null" /> (that enable fails and is reported to Cheat Engine).
    /// </summary>
    /// <returns>A new plugin; must not be <see langword="null" />.</returns>
    public static abstract CheatEnginePlugin Create();
}
