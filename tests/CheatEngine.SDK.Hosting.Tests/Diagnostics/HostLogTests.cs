using CheatEngine.SDK.Hosting.Diagnostics;
using CheatEngine.SDK.Hosting.Tests.Support;

namespace CheatEngine.SDK.Hosting.Tests.Diagnostics;

/// <summary>
///     The logging seam: defaults, replacement, filtering, and that a sink can never make the host throw. No Lua
///     needed.
/// </summary>
public sealed class HostLogTests : IDisposable
{
	public HostLogTests()
	{
		HostLog.ResetForTests();
	}

	public void Dispose()
	{
		HostLog.ResetForTests();
	}

	[Fact]
	public void Defaults_are_the_debug_output_sink_at_Information()
	{
		Assert.Same(DebugOutputLogSink.Instance, HostLog.Sink);
		Assert.Equal(HostLogLevel.Information, HostLog.MinimumLevel);
		Assert.False(HostLog.IsEnabled(HostLogLevel.Trace));
		Assert.True(HostLog.IsEnabled(HostLogLevel.Information));
		Assert.True(HostLog.IsEnabled(HostLogLevel.Error));
	}

	[Fact]
	public void Setting_a_null_sink_restores_the_default()
	{
		HostLog.Sink = new CapturingLogSink();
		Assert.IsType<CapturingLogSink>(HostLog.Sink);

		HostLog.Sink = null!;

		Assert.Same(DebugOutputLogSink.Instance, HostLog.Sink);
	}

	[Fact]
	public void Entries_below_the_minimum_level_are_not_delivered()
	{
		CapturingLogSink sink = new();
		HostLog.Sink = sink;
		HostLog.MinimumLevel = HostLogLevel.Warning;

		HostLog.Write(HostLogLevel.Trace, "trace");
		HostLog.Write(HostLogLevel.Information, "info");
		HostLog.Write(HostLogLevel.Warning, "warn");
		HostLog.Write(HostLogLevel.Error, "error", new InvalidOperationException("boom"));

		Assert.Equal(2, sink.Entries.Count);
		Assert.Equal((HostLogLevel.Warning, "warn", null), sink.Entries[0]);
		Assert.Equal(HostLogLevel.Error, sink.Entries[1].Level);
		Assert.Equal("error", sink.Entries[1].Message);
		Assert.IsType<InvalidOperationException>(sink.Entries[1].Exception);
	}

	[Fact]
	public void A_null_message_is_delivered_as_empty()
	{
		CapturingLogSink sink = new();
		HostLog.Sink = sink;

		HostLog.Write(HostLogLevel.Error, null);

		Assert.Equal(string.Empty, Assert.Single(sink.Entries).Message);
	}

	[Fact]
	public void A_throwing_sink_never_escapes()
	{
		HostLog.Sink = new ThrowingLogSink();

		Exception? escaped = Record.Exception(() => HostLog.Write(HostLogLevel.Error, "anything"));

		Assert.Null(escaped);
	}

	[Fact]
	public void The_debug_output_sink_accepts_every_shape_of_entry()
	{
		Exception? escaped = Record.Exception(() =>
		{
			DebugOutputLogSink.Instance.Write(HostLogLevel.Trace, "plain", null);
			DebugOutputLogSink.Instance.Write(HostLogLevel.Error, "with exception",
				new InvalidOperationException("boom"));
			DebugOutputLogSink.Instance.Write(HostLogLevel.Warning, string.Empty, null);
		});

		Assert.Null(escaped);
	}

	private sealed class ThrowingLogSink : IHostLogSink
	{
		public void Write(HostLogLevel level, string message, Exception? exception)
		{
			throw new NotSupportedException("sink failure");
		}
	}
}
