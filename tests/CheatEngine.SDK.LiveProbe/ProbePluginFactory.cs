using CheatEngine.SDK.Hosting.Plugin;

namespace LiveProbe;

// This is intentionally handwritten.  The live probe must see CEPluginInitialize's second int exactly as supplied by
// Cheat Engine before PluginHost sees it, and must not give that value a size/version meaning.
internal sealed class ProbePluginFactory : IPluginFactory
{
	public static ReadOnlySpan<byte> Utf8Name => "CheatEngine.SDK CE 7.7 Live Probe"u8;

	public static CheatEnginePlugin Create()
	{
		return new Ce77LiveProbePlugin();
	}
}
