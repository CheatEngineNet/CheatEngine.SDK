using System;
using System.Threading;

namespace CheatEngine.SDK.Hosting.Diagnostics;

/// <summary>
///     The dependency-free logging seam of the hosting runtime. Every failure that a lifecycle callback converts into a
///     <c>FALSE</c> or 0 for Cheat Engine is written here first, so that "the plugin did not enable" is never silent.
///     A plugin may write its own entries and may replace the sink.
/// </summary>
/// <remarks>
///     <para>
///         Entries go to <see cref="Sink" /> (default: <see cref="DebugOutputLogSink" />) when their level is at least
///         <see cref="MinimumLevel" /> (default: <see cref="HostLogLevel.Information" />; set
///         <see cref="HostLogLevel.Trace" />
///         to see every bootstrap and lifecycle call with its arguments).
///     </para>
///     <para>
///         Thread-safe: the sink reference and the level are read with volatile semantics on every write. Never throws: an
///         exception from the sink is swallowed, because the callers are the catch blocks of methods native code invokes.
///         Filtered-out entries cost one volatile read. The hosting runtime builds the message strings of its own
///         <see cref="HostLogLevel.Trace" /> and <see cref="HostLogLevel.Information" /> entries only when the entry will
///         be
///         delivered (guarded by <see cref="IsEnabled" />); its <see cref="HostLogLevel.Warning" /> and
///         <see cref="HostLogLevel.Error" /> entries sit on failure paths and are built unconditionally.
///     </para>
///     <para>
///         <b>Reentrancy containment (A24-21, SRC02-06).</b> A per-thread guard drops, and counts, a write that a sink
///         makes back into <see cref="Write" /> from inside its own <see cref="IHostLogSink.Write" /> on the same
///         thread: a re-entrant recursive write can otherwise reach an uncatchable <see cref="StackOverflowException" />
///         that takes the host process down with it. Writes from other threads are never affected. See
///         <see cref="IHostLogSink" /> for the sink's complete containment contract.
///     </para>
/// </remarks>
public static class HostLog
{
	private static IHostLogSink s_sink = DebugOutputLogSink.Instance;
	private static int s_minimumLevel = (int) HostLogLevel.Information;

	// Per-thread reentrancy guard (A24-21, SRC02-06): a sink that writes back into HostLog.Write from inside its own
	// Write, directly or through a path that logs, would otherwise recurse until StackOverflowException, which
	// cannot be caught and would take Cheat Engine down with it. Other threads are unaffected: a sink already
	// running on thread A never blocks a write from thread B.
	[ThreadStatic] private static bool t_writing;

	private static long s_droppedReentrantEntries;

	/// <summary>
	///     Count of entries dropped because a sink re-entered <see cref="Write" /> on the same thread while its own
	///     sink call was still running. For tests and diagnostics only.
	/// </summary>
	internal static long DroppedReentrantEntries => Interlocked.Read(ref s_droppedReentrantEntries);

	/// <summary>Gets or sets the sink. Setting <see langword="null" /> restores <see cref="DebugOutputLogSink" />.</summary>
	public static IHostLogSink Sink
	{
		get => Volatile.Read(ref s_sink);
		set => Volatile.Write(ref s_sink, value ?? DebugOutputLogSink.Instance);
	}

	/// <summary>Gets or sets the lowest level that is delivered to the sink.</summary>
	public static HostLogLevel MinimumLevel
	{
		get => (HostLogLevel) Volatile.Read(ref s_minimumLevel);
		set => Volatile.Write(ref s_minimumLevel, (int) value);
	}

	/// <summary>
	///     Tells whether an entry of <paramref name="level" /> would be delivered; use it to skip building an expensive
	///     message.
	/// </summary>
	/// <param name="level">The level to test.</param>
	/// <returns><see langword="true" /> when <paramref name="level" /> is at least <see cref="MinimumLevel" />.</returns>
	public static bool IsEnabled(HostLogLevel level)
	{
		return (int) level >= Volatile.Read(ref s_minimumLevel);
	}

	/// <summary>Writes one entry to the sink, unless it is below <see cref="MinimumLevel" />. Never throws.</summary>
	/// <param name="level">The severity.</param>
	/// <param name="message">The text; <see langword="null" /> is written as an empty string.</param>
	/// <param name="exception">The exception that caused the entry, if any.</param>
	public static void Write(HostLogLevel level, string? message, Exception? exception = null)
	{
		if (!IsEnabled(level))
		{
			return;
		}

		if (t_writing)
		{
			// A sink calling back into HostLog.Write from its own Write, directly or through a path that logs, is
			// dropped and counted instead of recursing: an uncatchable StackOverflowException would otherwise take
			// the host process down with it.
			Interlocked.Increment(ref s_droppedReentrantEntries);
			return;
		}

		t_writing = true;
		try
		{
			Volatile.Read(ref s_sink).Write(level, message ?? string.Empty, exception);
		}
		catch (Exception)
		{
			// A sink that throws must not turn a logged failure into an exception at the native boundary.
		}
		finally
		{
			t_writing = false;
		}
	}

	internal static void Error(string message, Exception? exception = null)
	{
		Write(HostLogLevel.Error, message, exception);
	}

	internal static void Warning(string message, Exception? exception = null)
	{
		Write(HostLogLevel.Warning, message, exception);
	}

	internal static void Information(string message)
	{
		Write(HostLogLevel.Information, message);
	}

	internal static void Trace(string message)
	{
		Write(HostLogLevel.Trace, message);
	}

	/// <summary>Resets the sink, level and reentrancy counter to their defaults. For tests.</summary>
	internal static void ResetForTests()
	{
		Sink = DebugOutputLogSink.Instance;
		MinimumLevel = HostLogLevel.Information;
		Volatile.Write(ref s_droppedReentrantEntries, 0);
		t_writing = false;
	}
}
