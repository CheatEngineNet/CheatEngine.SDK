using CheatEngine.SDK.Hosting.Plugin;

namespace CheatEngine.SDK.Hosting.Tests.Support;

/// <summary>What the entry-point generator emits for <see cref="RecordingPlugin" />, written by hand.</summary>
internal sealed class RecordingPluginFactory : IPluginFactory
{
    public const string Name = "Hosting Test Plugin";

    public static ReadOnlySpan<byte> Utf8Name => "Hosting Test Plugin"u8;

    public static CheatEnginePlugin Create()
    {
        return new RecordingPlugin();
    }
}
