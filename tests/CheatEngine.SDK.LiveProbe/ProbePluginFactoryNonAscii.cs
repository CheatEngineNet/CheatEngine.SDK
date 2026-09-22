#if LIVEPROBE_NON_ASCII_NAME
using CheatEngine.SDK.Hosting.Plugin;

namespace LiveProbe;

// Built only with -p:LiveProbeNonAsciiName=true (Checkpoint B, Q05.a). The name mixes one Latin-1 character and two
// characters outside code page 1252, so the run shows how Cheat Engine 7.7 decodes the name the SDK converts to the
// process ANSI code page (libs/CheatEngine.SDK.Hosting/Bootstrap/AnsiNameBuffer.cs).
internal sealed class ProbePluginFactoryNonAscii : IPluginFactory
{
	public static ReadOnlySpan<byte> Utf8Name => "CheatEngine.SDK Live Probe \u00e9 \u65e5\u672c"u8;

	public static CheatEnginePlugin Create()
	{
		return ProbePluginFactory.Create();
	}
}
#endif
