using System;

using CheatEngine.SDK.Hosting.Plugin;

namespace CheatEngine.SDK.Hosting.Bootstrap;

/// <summary>
///     The <see cref="PluginDescriptor" /> over one <see cref="IPluginFactory" /> type: forwards to its static
///     abstract members.
/// </summary>
/// <typeparam name="TFactory">The generated (or hand-written) factory.</typeparam>
internal sealed class PluginDescriptor<TFactory> : PluginDescriptor
	where TFactory : IPluginFactory
{
	internal override Type FactoryType => typeof(TFactory);

	internal override CheatEnginePlugin CreatePlugin()
	{
		return TFactory.Create();
	}
}
