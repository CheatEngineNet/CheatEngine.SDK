using System;

using CheatEngine.SDK.Hosting.Plugin;

namespace CheatEngine.SDK.Hosting.Bootstrap;

/// <summary>
///     The non-generic face of the plugin factory: what the <c>[UnmanagedCallersOnly]</c> lifecycle callbacks, which
///     cannot be generic or live in a generic type, need from the factory type that
///     <see cref="PluginHost.InitializeManaged{TFactory}" /> received. One instance per process, created by the first
///     bootstrap call and stored in a static; a virtual call instead of a delegate, reflection or a generic dictionary
///     lookup on the callback path.
/// </summary>
internal abstract class PluginDescriptor
{
	/// <summary>Gets the factory type, for the deterministic rejection of a second, different factory.</summary>
	internal abstract Type FactoryType
	{
		get;
	}

	/// <summary>Constructs the plugin (<see cref="IPluginFactory.Create" />).</summary>
	internal abstract CheatEnginePlugin CreatePlugin();
}
