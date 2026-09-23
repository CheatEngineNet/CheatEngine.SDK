using LiveProbe;

namespace CheatEngine.SDK.LiveProbe.Tests;

/// <summary>
///     The Checkpoint-B fault switch (<c>liveprobe.fault.json</c>, qualification scenarios Q06 and Q08 at C3), evaluated
///     with an injected authorization gate and an injected file reader. No host, no Lua, no file system.
/// </summary>
public sealed class LiveProbeFaultInjectionTests
{
	private const string PluginDirectory = "plugin-directory";

	[Fact]
	public void Fault_switch_is_ignored_when_authorization_is_denied()
	{
		bool fileRead = false;

		LiveProbeFaultDecision decision = LiveProbeFaultInjection.Evaluate(
			static () => AuthorizationDecision.Denied("The authorization manifest has expired."),
			PluginDirectory,
			_ =>
			{
				fileRead = true;
				return Switch("OnEnable");
			});

		Assert.False(fileRead);
		Assert.Equal(LiveProbeFaultStage.None, decision.Stage);
		Assert.False(decision.FileFound);
		Assert.Contains("authorization denied", decision.Reason, StringComparison.Ordinal);
		Assert.Contains("The authorization manifest has expired.", decision.Reason, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("""{"schema":"ce77-live-probe-fault-v0","throwIn":"OnEnable"}""", "unknown schema")]
	[InlineData("""{"throwIn":"OnEnable"}""", "no schema")]
	[InlineData("""{"schema":"ce77-live-probe-fault-v1","throwIn":"OnEnable "}""", "unknown throwIn")]
	[InlineData("""{"schema":"ce77-live-probe-fault-v1","throwIn":"onenable"}""", "unknown throwIn")]
	[InlineData("""{"schema":"ce77-live-probe-fault-v1"}""", "no throwIn")]
	[InlineData("""["ce77-live-probe-fault-v1"]""", "no schema")]
	[InlineData("""{"schema":""", "not valid JSON")]
	public void Fault_switch_with_an_unknown_schema_is_ignored_and_reported(string content, string reported)
	{
		LiveProbeFaultDecision decision = LiveProbeFaultInjection.Evaluate(Allowed, PluginDirectory, _ => content);

		Assert.Equal(LiveProbeFaultStage.None, decision.Stage);
		Assert.True(decision.FileFound);
		Assert.Contains("ignored and reported", decision.Reason, StringComparison.Ordinal);
		Assert.Contains(reported, decision.Reason, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("None")]
	[InlineData("FactoryCreate")]
	[InlineData("OnEnable")]
	[InlineData("OnDisable")]
	public void Fault_switch_selects_exactly_the_requested_stage(string throwIn)
	{
		LiveProbeFaultStage requested = Enum.Parse<LiveProbeFaultStage>(throwIn);
		string? readPath = null;

		LiveProbeFaultDecision decision = LiveProbeFaultInjection.Evaluate(Allowed, PluginDirectory, path =>
		{
			readPath = path;
			return Switch(requested.ToString());
		});

		Assert.Equal(Path.Combine(PluginDirectory, LiveProbeFaultInjection.FileName), readPath);
		Assert.Equal(requested, decision.Stage);
		Assert.True(decision.FileFound);
		foreach (LiveProbeFaultStage stage in Enum.GetValues<LiveProbeFaultStage>())
		{
			bool expected = stage == requested && stage != LiveProbeFaultStage.None;
			Assert.Equal(expected, LiveProbeFaultInjection.ThrowsAt(decision, stage));
		}
	}

	[Fact]
	public void Absent_fault_file_means_no_fault()
	{
		LiveProbeFaultDecision decision = LiveProbeFaultInjection.Evaluate(Allowed, PluginDirectory,
			static _ => null);

		Assert.Equal(LiveProbeFaultStage.None, decision.Stage);
		Assert.False(decision.FileFound);
		Assert.Contains("no fault", decision.Reason, StringComparison.Ordinal);
		foreach (LiveProbeFaultStage stage in Enum.GetValues<LiveProbeFaultStage>())
		{
			Assert.False(LiveProbeFaultInjection.ThrowsAt(decision, stage));
		}
	}

	[Fact]
	public void An_unreadable_fault_file_is_ignored_and_reported_without_throwing()
	{
		LiveProbeFaultDecision decision = LiveProbeFaultInjection.Evaluate(Allowed, PluginDirectory,
			static _ => throw new IOException("Synthetic sharing violation."));

		Assert.Equal(LiveProbeFaultStage.None, decision.Stage);
		Assert.True(decision.FileFound);
		Assert.Contains("IOException", decision.Reason, StringComparison.Ordinal);
	}

	private static AuthorizationDecision Allowed()
	{
		return AuthorizationDecision.Allowed("C:\\ce.exe", "HOST", 401, "C:\\disposable-target.exe", "TARGET",
			DateTimeOffset.MaxValue);
	}

	private static string Switch(string throwIn)
	{
		return "{\"schema\":\"" + LiveProbeFaultInjection.Schema + "\",\"throwIn\":\"" + throwIn + "\"}";
	}
}
