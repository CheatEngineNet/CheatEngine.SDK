using CheatEngine.SDK.SourceGenerators.Shared;

namespace CheatEngine.SDK.SourceGenerators.EntryPoint.Model;

/// <summary>
///     Final model of the pipeline: what the emitted file depends on, and nothing else. The source output re-runs
///     exactly when one of these three strings changes (or when the model appears or disappears).
/// </summary>
/// <param name="FullyQualifiedTypeName">The plugin class, <c>global::</c>-qualified.</param>
/// <param name="DisplayName">The plugin's display name, not yet escaped.</param>
/// <param name="DeclaredDiagnosticIds">
///     Operand of the extra <c>#pragma warning disable</c> (see <see cref="PluginModel" />); empty for no such line.
/// </param>
internal sealed record BootstrapModel(string FullyQualifiedTypeName, string DisplayName, string DeclaredDiagnosticIds)
{
    /// <summary>
    ///     Decides whether there is something to emit: generation is on and exactly one <b>valid</b> plugin class
    ///     exists. Every other situation yields <see langword="null" /> and is explained by the analyzers
    ///     (CESDK0001 invalid shape, CESDK0002 several plugins).
    /// </summary>
    public static BootstrapModel? Select(EquatableArray<PluginModel> plugins, EntryPointOptions options)
    {
        return Select(plugins, options, false);
    }

    /// <summary>
    ///     Decides whether there is something to emit while accounting for a user-declared host-mandated bootstrap
    ///     type. A collision leaves the generator silent so that the analyzer can report the source location instead of
    ///     generated code causing a duplicate-type error.
    /// </summary>
    public static BootstrapModel? Select(
        EquatableArray<PluginModel> plugins,
        EntryPointOptions options,
        bool entryPointTypeCollision)
    {
        if (!options.GenerateEntryPoint || entryPointTypeCollision) return null;

        PluginModel? single = null;
        foreach (var plugin in plugins)
        {
            if (!plugin.IsValid) continue;

            if (single is not null) return null;

            single = plugin;
        }

        return single is null
            ? null
            : new BootstrapModel(single.FullyQualifiedTypeName, single.DisplayName, single.DeclaredDiagnosticIds);
    }
}
