using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;

using CheatEngine.SDK.Hosting.Bootstrap;
using CheatEngine.SDK.Hosting.Context;

namespace LiveProbe;

/// <summary>
///     Facts the SDK host recorded about this process, read without Lua. Qualification scenarios Q03 (observed exports
///     size), Q04 (raw second bootstrap integer), Q05 (plugin id and epoch) and Q40 (where the assemblies were loaded
///     from) record them from <c>ce77_live_probe_status_json()</c>.
/// </summary>
internal readonly record struct LiveProbeHostFacts(
	bool HasContext,
	uint PluginId,
	int Epoch,
	int ReportedExportsSize,
	bool HasProcessMessages,
	bool HasCheckSynchronize,
	string Phase,
	int LastInitRecordArgument,
	int LastVersionRecordSize,
	string PluginAssemblyLocation,
	string PluginAssemblyMvid,
	string HostingAssemblyLocation,
	string HostingAssemblyMvid,
	string HostingLoadContext)
{
	/// <summary>Reads the current facts from <see cref="PluginHost" /> and the loaded assemblies.</summary>
	internal static LiveProbeHostFacts Capture()
	{
		PluginContext? context = PluginHost.Context;
		Assembly plugin = typeof(LiveProbeHostFacts).Assembly;
		Assembly hosting = typeof(PluginHost).Assembly;
		AssemblyLoadContext? hostingLoadContext = AssemblyLoadContext.GetLoadContext(hosting);
		return new LiveProbeHostFacts(
			context is not null,
			context?.PluginId ?? 0,
			context?.Epoch ?? 0,
			context?.ReportedExportsSize ?? 0,
			context?.HasProcessMessages ?? false,
			context?.HasCheckSynchronize ?? false,
			PluginHost.Phase.ToString(),
			PluginHost.LastInitRecordArgument,
			PluginHost.LastVersionRecordSize,
			plugin.Location,
			plugin.ManifestModule.ModuleVersionId.ToString("D", CultureInfo.InvariantCulture),
			hosting.Location,
			hosting.ManifestModule.ModuleVersionId.ToString("D", CultureInfo.InvariantCulture),
			hostingLoadContext is null
				? "<none>"
				: string.Create(CultureInfo.InvariantCulture,
					$"{hostingLoadContext.Name ?? "<unnamed>"} (collectible={hostingLoadContext.IsCollectible})"));
	}
}
