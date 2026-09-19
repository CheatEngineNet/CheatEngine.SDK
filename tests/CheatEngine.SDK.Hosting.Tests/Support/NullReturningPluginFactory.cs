using CheatEngine.SDK.Hosting.Plugin;

namespace CheatEngine.SDK.Hosting.Tests.Support;

/// <summary>
///     A factory that violates its contract by returning <see langword="null" />; the host must report a failed
///     enable, not crash.
/// </summary>
internal sealed class NullReturningPluginFactory : IPluginFactory
{
    public static ReadOnlySpan<byte> Utf8Name => "Null Plugin"u8;

    public static CheatEnginePlugin Create()
    {
        return null!;
    }
}
