using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using CheatEngine.SDK.Hosting.Context;
using CheatEngine.SDK.Hosting.Diagnostics;

namespace LivePlugin.Coexistence;

/// <summary>
///     Emits observations that let a live-test operator distinguish plugin identity from the identity of the loaded
///     Hosting assembly. This is diagnostic-only: it neither creates nor selects an <see cref="AssemblyLoadContext" />.
/// </summary>
internal static class CoexistenceDiagnostics
{
    internal static void LogEnabled(string pluginLabel, Assembly pluginAssembly, PluginContext context)
    {
        HostLog.Write(HostLogLevel.Information, string.Create(
            CultureInfo.InvariantCulture,
            $"CheatEngine.SDK coexistence {pluginLabel}: {GetIdentity(pluginLabel, pluginAssembly)}; PluginId={context.PluginId}; Epoch={context.Epoch}."));
    }

    internal static string GetIdentity(string pluginLabel, Assembly pluginAssembly)
    {
        var hostingAssembly = typeof(CheatEngine.SDK.Hosting.Bootstrap.PluginHost).Assembly;
        var pluginLoadContext = AssemblyLoadContext.GetLoadContext(pluginAssembly);
        var hostingLoadContext = AssemblyLoadContext.GetLoadContext(hostingAssembly);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Plugin={pluginLabel}; PluginAssembly={pluginAssembly.FullName}; PluginMvid={pluginAssembly.ManifestModule.ModuleVersionId}; " +
            $"HostingAssembly={hostingAssembly.FullName}; HostingMvid={hostingAssembly.ManifestModule.ModuleVersionId}; " +
            $"PluginALC={Describe(pluginLoadContext)}; HostingALC={Describe(hostingLoadContext)}; " +
            $"SameALC={ReferenceEquals(pluginLoadContext, hostingLoadContext)}");
    }

    private static string Describe(AssemblyLoadContext? loadContext)
    {
        if (loadContext is null) return "<none>";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Name={loadContext.Name ?? "<unnamed>"}, Collectible={loadContext.IsCollectible}");
    }
}
