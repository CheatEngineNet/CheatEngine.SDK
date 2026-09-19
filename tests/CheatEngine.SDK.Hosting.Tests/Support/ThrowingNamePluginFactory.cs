using CheatEngine.SDK.Hosting.Plugin;

namespace CheatEngine.SDK.Hosting.Tests.Support;

/// <summary>
///     A factory whose <see cref="Utf8Name" /> getter throws: the only way plugin code can make the body of
///     <c>PluginHost.InitializeManaged</c> itself fail, which is what proves its catch-all (0, a log entry, nothing
///     registered).
/// </summary>
internal sealed class ThrowingNamePluginFactory : IPluginFactory
{
    public static ReadOnlySpan<byte> Utf8Name => throw new NotSupportedException("name failure requested by the test");

    public static CheatEnginePlugin Create()
    {
        return new RecordingPlugin();
    }
}
