using System.Collections.Immutable;

using CheatEngine.SDK.SourceGenerators.Shared;

namespace CheatEngine.SDK.SourceGenerators.EntryPoint;

/// <summary>
///     Tracking names of every step of the entry-point pipeline, in pipeline order. The cacheability tests look the
///     steps up by these constants.
/// </summary>
internal static class EntryPointTrackingNames
{
	/// <summary><c>ForAttributeWithMetadataName</c> transform: one <c>PluginModel</c> per attributed class.</summary>
	public const string Plugin = Prefix + "Plugin";

	/// <summary><c>Collect</c>: all plugin models as an <c>ImmutableArray</c>.</summary>
	public const string CollectedPlugins = Prefix + "CollectedPlugins";

	/// <summary>The collected models wrapped in an <c>EquatableArray</c> (value equality).</summary>
	public const string Plugins = Prefix + "Plugins";

	/// <summary>The MSBuild switches, parsed.</summary>
	public const string Options = Prefix + "Options";

	/// <summary><c>Combine</c> of <see cref="Plugins" /> and <see cref="Options" />.</summary>
	public const string PluginsAndOptions = Prefix + "PluginsAndOptions";

	/// <summary>Checks whether user source already owns the host-mandated generated type identity.</summary>
	public const string EntryPointTypeCollision = Prefix + "EntryPointTypeCollision";

	/// <summary>Combines candidate plugins, the build switch and the generated-type collision result.</summary>
	public const string PluginsOptionsAndCollision = Prefix + "PluginsOptionsAndCollision";

	/// <summary>The final <c>BootstrapModel</c> (or null): the only input of the source output.</summary>
	public const string Bootstrap = Prefix + "Bootstrap";

	private const string Prefix = TrackingNames.Prefix + "EntryPoint.";

	/// <summary>All of the above, for tests that must not miss a step.</summary>
	public static readonly ImmutableArray<string> All =
	[
		Plugin, CollectedPlugins, Plugins, Options, PluginsAndOptions, EntryPointTypeCollision,
		PluginsOptionsAndCollision,
		Bootstrap
	];
}
