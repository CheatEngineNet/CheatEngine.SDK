using CheatEngine.SDK.Hosting.Plugin;

namespace CheatEngine.SDK.Hosting.Tests.Support;

/// <summary>
///     A second factory type: what a second plugin class in the same load context would produce. Must be rejected
///     after the first one registered.
/// </summary>
internal sealed class AlternatePluginFactory : IPluginFactory
{
    public static ReadOnlySpan<byte> Utf8Name => "Alternate Plugin"u8;

    public static CheatEnginePlugin Create()
    {
        return new RecordingPlugin();
    }
}
