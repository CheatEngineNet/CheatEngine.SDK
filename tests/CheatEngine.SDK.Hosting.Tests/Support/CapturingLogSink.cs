using CheatEngine.SDK.Hosting.Diagnostics;

namespace CheatEngine.SDK.Hosting.Tests.Support;

/// <summary>Collects <see cref="HostLog" /> entries so that tests can assert that a failure was reported, and how.</summary>
internal sealed class CapturingLogSink : IHostLogSink
{
	private readonly List<(HostLogLevel Level, string Message, Exception? Exception)> _entries = [];

	public Action<string>? OnMessage
	{
		get;
		set;
	}

	public IReadOnlyList<(HostLogLevel Level, string Message, Exception? Exception)> Entries
	{
		get
		{
			lock (_entries)
			{
				return [.. _entries];
			}
		}
	}

	public void Write(HostLogLevel level, string message, Exception? exception)
	{
		lock (_entries)
		{
			_entries.Add((level, message, exception));
		}

		OnMessage?.Invoke(message);
	}

	/// <summary>The error entries whose message contains <paramref name="fragment" /> (ordinal).</summary>
	public IReadOnlyList<(HostLogLevel Level, string Message, Exception? Exception)> Errors(string fragment)
	{
		List<(HostLogLevel, string, Exception?)> matches = [];
		foreach ((HostLogLevel level, string message, Exception? exception) in Entries)
		{
			if (level == HostLogLevel.Error && message.Contains(fragment, StringComparison.Ordinal))
			{
				matches.Add((level, message, exception));
			}
		}

		return matches;
	}

	public bool HasEntry(HostLogLevel level, string fragment)
	{
		foreach ((HostLogLevel entryLevel, string message, Exception? _) in Entries)
		{
			if (entryLevel == level && message.Contains(fragment, StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}
}
