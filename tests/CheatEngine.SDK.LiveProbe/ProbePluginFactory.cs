using CheatEngine.SDK.Hosting.Plugin;

namespace LiveProbe;

// This is intentionally handwritten.  The live probe must see CEPluginInitialize's second int exactly as supplied by
// Cheat Engine before PluginHost sees it, and must not give that value a size/version meaning.
internal sealed class ProbePluginFactory : IPluginFactory
{
	public static ReadOnlySpan<byte> Utf8Name => "CheatEngine.SDK CE 7.7 Live Probe"u8;

	public static CheatEnginePlugin Create()
	{
		// Checkpoint B, Q06: an authorized liveprobe.fault.json can make construction fail for this enable.
		LiveProbeFaultInjection.EnterFactoryCreate();
		return new Ce77LiveProbePlugin();
	}
}
