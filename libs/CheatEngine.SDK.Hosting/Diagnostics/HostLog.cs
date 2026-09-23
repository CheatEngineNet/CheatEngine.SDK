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
/// </remarks>
public static class HostLog
{
	/// <summary>The environment variable that opts every enable attempt into the identification diagnostic.</summary>
	/// <seealso cref="IdentifyOnEnable" />
	public const string IdentifyOnEnableEnvironmentVariable = "CHEATENGINE_SDK_IDENTIFY_ON_ENABLE";

	private static IHostLogSink s_sink = DebugOutputLogSink.Instance;
	private static int s_minimumLevel = (int) HostLogLevel.Information;
	private static int s_identifyOnEnable;

	// Test seam: production reads the real process environment through DefaultIdentifyOnEnableEnvironmentReader.
	private static Func<string?>? s_identifyOnEnableEnvironmentReader;

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
	///     Gets or sets whether the bounded <c>CheatEngineSdkIdentification</c> diagnostic (SDK version, bridge
	///     fingerprint, bound Lua module hash, CE and runtime versions; never a user path) is written once at the start of
	///     every enable attempt. Default <see langword="false" />: identification is opt-in.
	/// </summary>
	/// <remarks>
	///     <para>
	///         A plugin that wants identification on its very first enable sets this from a
	///         <see cref="System.Runtime.CompilerServices.ModuleInitializerAttribute" />-annotated method, which the
	///         runtime runs before any plugin code. Setting it later takes effect at the next enable attempt.
	///     </para>
	///     <para>
	///         The environment variable named by <see cref="IdentifyOnEnableEnvironmentVariable" />
	///         (<c>CHEATENGINE_SDK_IDENTIFY_ON_ENABLE=1</c>) opts in as well, without rebuilding the plugin: either this
	///         property or the environment variable being set is enough. The environment is read once per enable attempt,
	///         inside a <see langword="try" />/<see langword="catch" />, so a hostile or unavailable environment never
	///         faults the callback.
	///     </para>
	/// </remarks>
	public static bool IdentifyOnEnable
	{
		get => Volatile.Read(ref s_identifyOnEnable) != 0;
		set => Volatile.Write(ref s_identifyOnEnable, value ? 1 : 0);
	}

	/// <summary>
	///     Test seam for the <see cref="IdentifyOnEnableEnvironmentVariable" /> reader. <see langword="null" /> (the
	///     default) reads the real process environment; a test replaces it so the real environment is never touched.
	/// </summary>
	internal static Func<string?>? IdentifyOnEnableEnvironmentReader
	{
		get => Volatile.Read(ref s_identifyOnEnableEnvironmentReader);
		set => Volatile.Write(ref s_identifyOnEnableEnvironmentReader, value);
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

		try
		{
			Volatile.Read(ref s_sink).Write(level, message ?? string.Empty, exception);
		}
		catch (Exception)
		{
			// A sink that throws must not turn a logged failure into an exception at the native boundary.
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

	/// <summary>
	///     Tells whether an enable attempt should build and emit the identification diagnostic: either
	///     <see cref="IdentifyOnEnable" /> is set, or the <see cref="IdentifyOnEnableEnvironmentVariable" /> reads
	///     <c>"1"</c>. The environment read never throws.
	/// </summary>
	internal static bool IsIdentifyOnEnableRequested()
	{
		return IdentifyOnEnable || IsIdentifyOnEnableEnvironmentSet();
	}

	private static bool IsIdentifyOnEnableEnvironmentSet()
	{
		try
		{
			Func<string?> reader = IdentifyOnEnableEnvironmentReader ?? ReadIdentifyOnEnableEnvironmentVariable;
			return string.Equals(reader(), "1", StringComparison.Ordinal);
		}
		catch (Exception)
		{
			// The environment must never fault a native lifecycle callback.
			return false;
		}
	}

	private static string? ReadIdentifyOnEnableEnvironmentVariable()
	{
		return Environment.GetEnvironmentVariable(IdentifyOnEnableEnvironmentVariable);
	}

	/// <summary>Resets the sink, level and identification opt-in to their defaults. For tests.</summary>
	internal static void ResetForTests()
	{
		Sink = DebugOutputLogSink.Instance;
		MinimumLevel = HostLogLevel.Information;
		IdentifyOnEnable = false;
		IdentifyOnEnableEnvironmentReader = null;
	}
}
