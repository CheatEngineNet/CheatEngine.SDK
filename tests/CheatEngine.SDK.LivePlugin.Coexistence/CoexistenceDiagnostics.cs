using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;

using CheatEngine.SDK.Hosting.Bootstrap;
using CheatEngine.SDK.Hosting.Context;
using CheatEngine.SDK.Hosting.Diagnostics;
using CheatEngine.SDK.Lua.Runtime;

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
		Assembly hostingAssembly = typeof(PluginHost).Assembly;
		AssemblyLoadContext? pluginLoadContext = AssemblyLoadContext.GetLoadContext(pluginAssembly);
		AssemblyLoadContext? hostingLoadContext = AssemblyLoadContext.GetLoadContext(hostingAssembly);

		// Appended (WI-7, F03 C2): the SDK's own attach epoch and the PluginHost static-state identity, so a C2
		// host-emulated run and a future exact-host run can tell whether two enabled plugins share one Hosting
		// instance. Appended at the end, after the existing fields: the Checkpoint B runner and receipts parse the
		// existing prefix and must keep working unmodified.
		return string.Create(
			CultureInfo.InvariantCulture,
			$"Plugin={pluginLabel}; PluginAssembly={pluginAssembly.FullName}; PluginMvid={pluginAssembly.ManifestModule.ModuleVersionId}; " +
			$"HostingAssembly={hostingAssembly.FullName}; HostingMvid={hostingAssembly.ManifestModule.ModuleVersionId}; " +
			$"PluginALC={Describe(pluginLoadContext)}; HostingALC={Describe(hostingLoadContext)}; " +
			$"SameALC={ReferenceEquals(pluginLoadContext, hostingLoadContext)}; " +
			$"Epoch={LuaRuntime.Epoch}; HostingTypeHandle=0x{typeof(PluginHost).TypeHandle.Value:X}");
	}

	private static string Describe(AssemblyLoadContext? loadContext)
	{
		if (loadContext is null)
		{
			return "<none>";
		}

		return string.Create(
			CultureInfo.InvariantCulture,
			$"Name={loadContext.Name ?? "<unnamed>"}, Collectible={loadContext.IsCollectible}");
	}
}
