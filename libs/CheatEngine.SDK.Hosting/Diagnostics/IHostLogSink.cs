using System;

namespace CheatEngine.SDK.Hosting.Diagnostics;

/// <summary>
///     Where <see cref="HostLog" /> entries go. The default is <see cref="DebugOutputLogSink" />; a plugin replaces it
///     through <see cref="HostLog.Sink" /> to route host diagnostics into its own logging.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="Write" /> may be called from any thread, including from inside the lifecycle callbacks Cheat Engine
///         invokes, and must not block. It may throw: <see cref="HostLog" /> swallows sink exceptions, because a sink
///         failure inside a callback's catch block would otherwise escape into native code.
///     </para>
///     <para>
///         <b>Containment (A24-21, SRC02-06).</b> A sink must not call back into <see cref="HostLog.Write" />, directly or
///         through a path that logs, from inside its own <see cref="Write" />: <see cref="HostLog" /> drops that
///         re-entrant entry on the same thread instead of recursing, and counts it. A sink must not re-enter a plugin
///         lifecycle transition (for example calling back into <c>EnablePlugin</c> or <c>DisablePlugin</c>); the
///         lifecycle refuses that immediately rather than waiting for the sink. A sink must not acquire a Lua operation
///         while a lifecycle transition owns admission; that acquisition is refused like any other during a transition.
///         None of these containments retry on the sink's behalf: a sink that needs one of these must schedule it for
///         after <see cref="Write" /> returns.
///     </para>
/// </remarks>
public interface IHostLogSink
{
	/// <summary>Records one entry.</summary>
	/// <param name="level">The severity; entries below <see cref="HostLog.MinimumLevel" /> are not delivered.</param>
	/// <param name="message">The text; never <see langword="null" />.</param>
	/// <param name="exception">The exception that caused the entry, if any.</param>
	public void Write(HostLogLevel level, string message, Exception? exception);
}
