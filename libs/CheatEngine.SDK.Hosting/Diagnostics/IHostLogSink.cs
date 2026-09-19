using System;

namespace CheatEngine.SDK.Hosting.Diagnostics;

/// <summary>
///     Where <see cref="HostLog" /> entries go. The default is <see cref="DebugOutputLogSink" />; a plugin replaces it
///     through <see cref="HostLog.Sink" /> to route host diagnostics into its own logging.
/// </summary>
/// <remarks>
///     <see cref="Write" /> may be called from any thread, including from inside the lifecycle callbacks Cheat Engine
///     invokes, and must not block. It may throw: <see cref="HostLog" /> swallows sink exceptions, because a sink failure
///     inside a callback's catch block would otherwise escape into native code.
/// </remarks>
public interface IHostLogSink
{
    /// <summary>Records one entry.</summary>
    /// <param name="level">The severity; entries below <see cref="HostLog.MinimumLevel" /> are not delivered.</param>
    /// <param name="message">The text; never <see langword="null" />.</param>
    /// <param name="exception">The exception that caused the entry, if any.</param>
    public void Write(HostLogLevel level, string message, Exception? exception);
}
