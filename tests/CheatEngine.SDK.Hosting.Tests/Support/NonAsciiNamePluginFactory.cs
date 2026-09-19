using CheatEngine.SDK.Hosting.Plugin;

namespace CheatEngine.SDK.Hosting.Tests.Support;

/// <summary>
///     A factory whose display name has a non-ASCII character (U+00E9), to exercise the ANSI conversion of the name
///     buffer.
/// </summary>
internal sealed class NonAsciiNamePluginFactory : IPluginFactory
{
    public const string Name = "Plugin \u00E9";

    public static ReadOnlySpan<byte> Utf8Name => "Plugin \u00E9"u8;

    public static CheatEnginePlugin Create()
    {
        return new RecordingPlugin();
    }
}
