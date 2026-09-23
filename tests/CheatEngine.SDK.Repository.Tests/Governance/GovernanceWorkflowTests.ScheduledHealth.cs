using System.Text.RegularExpressions;

using YamlDotNet.RepresentationModel;

namespace CheatEngine.SDK.Repository.Tests.Governance;

/// <summary>
///     <c>.github/workflows/scheduled-health.yml</c> (audit register PR-CQ-55, PR-CQ-30, A21-25): weekly, serialized,
///     issue writes from scheduled runs only, the canary SDK selected before .NET is installed, and the release bridge
///     rebuilt with the toolchain pins of the <c>native</c> job.
/// </summary>
public sealed partial class GovernanceWorkflowTests
{
	private const string PipelineWorkflow = ".github/workflows/ci.yml";
	private const string SetupAction = "./.github/actions/setup-dotnet";

	[Fact]
	public void Scheduled_health_runs_weekly_and_on_dispatch_one_run_at_a_time()
	{
		YamlDocument workflow = YamlDocument.Load(GovernanceWorkflows.ScheduledHealth);

		Assert.Equal(["schedule", "workflow_dispatch"], workflow.Triggers);
		YamlMappingNode schedule = Assert.Single(YamlDocument.Mappings(YamlDocument.Child(workflow.Root, "on"), "schedule"));
		Assert.Matches(WeeklyCron(), YamlDocument.Scalar(schedule, "cron") ?? "");
		// A queued run waits: cancelling a run that is about to report would lose its issue update.
		YamlNode? concurrency = YamlDocument.Child(workflow.Root, "concurrency");
		Assert.Equal("scheduled-health", YamlDocument.Scalar(concurrency, "group"));
		Assert.Equal("false", YamlDocument.Scalar(concurrency, "cancel-in-progress"));
		Assert.Equal(["audit", "canary", "test-repeat", "bridge-drift", "links", "notify"], YamlDocument.KeysOf(YamlDocument.Child(workflow.Root, "jobs")));
	}

	[Fact]
	public void Scheduled_health_opens_issues_only_from_scheduled_runs()
	{
		YamlDocument workflow = YamlDocument.Load(GovernanceWorkflows.ScheduledHealth);
		YamlMappingNode notify = workflow.Job("notify");

		Assert.Equal("${{ always() && github.event_name == 'schedule' }}", YamlDocument.Scalar(notify, "if"));
		Assert.Equal(["audit", "canary", "test-repeat", "bridge-drift", "links"], YamlDocument.Scalars(notify, "needs"));
		Assert.Equal(new Dictionary<string, string>(StringComparer.Ordinal) { ["contents"] = "read", ["issues"] = "write" },
			YamlDocument.Permissions(notify));

		// issues: write exists nowhere else among the governance workflows.
		foreach (string path in GovernanceWorkflows.Existing())
		{
			foreach (KeyValuePair<string, YamlMappingNode> job in YamlDocument.Load(path).Jobs)
			{
				bool writesIssues = YamlDocument.Permissions(job.Value)?.TryGetValue("issues", out string? access) == true
									&& string.Equals(access, "write", StringComparison.Ordinal);
				Assert.True(!writesIssues || (string.Equals(path, GovernanceWorkflows.ScheduledHealth, StringComparison.Ordinal)
											  && string.Equals(job.Key, "notify", StringComparison.Ordinal)),
					$"{path} job {job.Key} may not write issues.");
			}
		}

		// The only inputs of the issue: needs, two booleans, the run URL and the repository (closed vocabularies).
		YamlMappingNode publish = Assert.Single(YamlDocument.Steps(notify), static step => YamlDocument.Child(step, "env") is not null);
		Assert.Equal(["NEEDS", "DRIFT", "BROKEN_LINKS", "RUN_URL", "GH_REPO", "GH_TOKEN"], YamlDocument.KeysOf(YamlDocument.Child(publish, "env")));
		Assert.Equal("${{ toJSON(needs) }}", YamlDocument.Scalar(YamlDocument.Child(publish, "env"), "NEEDS"));
		YamlMappingNode checkout = Assert.Single(YamlDocument.Steps(notify), static step => YamlDocument.UsesAction(step, "actions/checkout"));
		Assert.Equal("eng/ci/health", YamlDocument.Scalar(YamlDocument.Child(checkout, "with"), "sparse-checkout"));
	}

	[Fact]
	public void Scheduled_health_canary_rewrites_global_json_before_the_composite_action()
	{
		IReadOnlyList<YamlMappingNode> steps = YamlDocument.Steps(YamlDocument.Load(GovernanceWorkflows.ScheduledHealth).Job("canary"));

		int checkout = IndexWhere(steps, static step => YamlDocument.UsesAction(step, "actions/checkout"));
		int pin = IndexWhere(steps, static step => RunText(step).Contains("./eng/ci/health/Set-CanarySdkVersion.ps1", StringComparison.Ordinal));
		int setup = IndexOf(steps, SetupAction);
		int canary = IndexWhere(steps, static step => RunText(step).Contains("./eng/ci/health/Invoke-SdkCanary.ps1", StringComparison.Ordinal));

		// global.json has rollForward: disable, so the composite action installs exactly the SDK the pin step wrote.
		Assert.True(checkout >= 0 && checkout < pin && pin < setup && setup < canary,
			$"The canary must check out, select the SDK, set up .NET, then run (indexes {checkout}, {pin}, {setup}, {canary}).");
		Assert.Equal("sdk", YamlDocument.Scalar(steps[pin], "id"));
		Assert.Equal("0", YamlDocument.Scalar(YamlDocument.Child(steps[checkout], "with"), "fetch-depth"));
		Assert.Null(YamlDocument.Scalar(YamlDocument.Child(steps[setup], "with"), "cache"));
	}

	[Fact]
	public void Every_scheduled_health_dotnet_job_uses_the_composite_action()
	{
		// Same rule as WorkflowContractTests: a job whose run steps, or the repository scripts they start, run the .NET CLI
		// installs the pinned SDK through the composite action first.
		List<string> dotnetJobs = [];
		foreach (KeyValuePair<string, YamlMappingNode> job in YamlDocument.Load(GovernanceWorkflows.ScheduledHealth).Jobs)
		{
			IReadOnlyList<YamlMappingNode> steps = YamlDocument.Steps(job.Value);
			int firstDotnet = IndexWhere(steps, static step => RunsDotnet(RunText(step)));
			if (firstDotnet < 0)
			{
				continue;
			}

			dotnetJobs.Add(job.Key);
			int setup = IndexOf(steps, SetupAction);
			Assert.True(setup >= 0 && setup < firstDotnet,
				$"scheduled-health job {job.Key} runs the .NET CLI at step {firstDotnet} without first using {SetupAction}.");
		}

		Assert.Equal(["audit", "canary", "test-repeat"], dotnetJobs);
	}

	[Fact]
	public void Bridge_drift_uses_the_toolchain_pins_of_the_native_job()
	{
		YamlMappingNode drift = YamlDocument.Load(GovernanceWorkflows.ScheduledHealth).Job("bridge-drift");
		Dictionary<string, string> driftPins = BridgePins(drift);
		Assert.Equal(["BRIDGE_VS_SDKVER", "BRIDGE_VS_TOOLSET"], driftPins.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal);
		string run = RunText(Assert.Single(YamlDocument.Steps(drift), static step => YamlDocument.Scalar(step, "id") is "drift"));
		Assert.Contains("--vs_toolset=$env:BRIDGE_VS_TOOLSET", run, StringComparison.Ordinal);
		Assert.Contains("--vs_sdkver=$env:BRIDGE_VS_SDKVER", run, StringComparison.Ordinal);

		// Once the native job pins its toolchain, both jobs must name the same toolset and Windows SDK: a drift then means
		// that today's pinned CI toolchain no longer rebuilds the released bridge.
		YamlMappingNode native = YamlDocument.Load(PipelineWorkflow).Job("native");
		Dictionary<string, string> nativePins = BridgePins(native);
		string nativeText = string.Join('\n', YamlDocument.Steps(native).Select(RunText));
		if (nativePins.Count == 0)
		{
			Assert.True(!nativeText.Contains("vs_toolset", StringComparison.OrdinalIgnoreCase) && !nativeText.Contains("VsToolset", StringComparison.Ordinal),
				"The native job pins its toolchain without BRIDGE_VS_* job variables: update scheduled-health.yml bridge-drift and this test together.");
			return;
		}

		Assert.Equal(nativePins.OrderBy(static pin => pin.Key, StringComparer.Ordinal),
			driftPins.OrderBy(static pin => pin.Key, StringComparer.Ordinal));
	}

	[Fact]
	public void Scheduled_health_uploads_diagnostics_under_their_reserved_names()
	{
		Dictionary<string, (string Job, string Condition, string Retention)> expected = new(StringComparer.Ordinal)
		{
			["health-sdk-canary"] = ("canary", "${{ !cancelled() }}", "14"),
			["health-test-repeat"] = ("test-repeat", "failure()", "14"),
			["health-bridge-drift"] = ("bridge-drift", "${{ !cancelled() }}", "30")
		};

		Dictionary<string, (string Job, string Condition, string Retention)> actual = new(StringComparer.Ordinal);
		foreach (KeyValuePair<string, YamlMappingNode> job in YamlDocument.Load(GovernanceWorkflows.ScheduledHealth).Jobs)
		{
			foreach (YamlMappingNode step in YamlDocument.Steps(job.Value))
			{
				if (YamlDocument.UsesAction(step, "actions/upload-artifact"))
				{
					YamlNode? with = YamlDocument.Child(step, "with");
					actual[YamlDocument.Scalar(with, "name") ?? ""] =
						(job.Key, YamlDocument.Scalar(step, "if") ?? "", YamlDocument.Scalar(with, "retention-days") ?? "");
				}
			}
		}

		Assert.Equal(expected.OrderBy(static item => item.Key, StringComparer.Ordinal), actual.OrderBy(static item => item.Key, StringComparer.Ordinal));
	}

	private static Dictionary<string, string> BridgePins(YamlMappingNode job)
	{
		Dictionary<string, string> pins = new(StringComparer.Ordinal);
		YamlNode? env = YamlDocument.Child(job, "env");
		foreach (string key in YamlDocument.KeysOf(env))
		{
			if (key.StartsWith("BRIDGE_VS_", StringComparison.Ordinal))
			{
				pins[key] = YamlDocument.Scalar(env, key) ?? "";
			}
		}

		return pins;
	}

	private static string RunText(YamlMappingNode step)
	{
		return YamlDocument.Scalar(step, "run") ?? "";
	}

	private static int IndexWhere(IReadOnlyList<YamlMappingNode> steps, Func<YamlMappingNode, bool> predicate)
	{
		for (int i = 0; i < steps.Count; i++)
		{
			if (predicate(steps[i]))
			{
				return i;
			}
		}

		return -1;
	}

	/// <summary>Whether a run script, or a repository script it starts, runs the .NET CLI (comments ignored).</summary>
	private static bool RunsDotnet(string run)
	{
		if (DotnetInvocation().IsMatch(StripComments(run)))
		{
			return true;
		}

		foreach (Match script in ScriptReference().Matches(run))
		{
			string path = RepositoryFile.FullPath(script.Groups["path"].Value);
			if (File.Exists(path) && DotnetInvocation().IsMatch(StripComments(File.ReadAllText(path))))
			{
				return true;
			}
		}

		return false;
	}

	private static string StripComments(string script)
	{
		List<string> lines = [];
		foreach (string line in CommentBlock().Replace(script, "").ReplaceLineEndings("\n").Split('\n'))
		{
			if (!line.TrimStart().StartsWith('#'))
			{
				lines.Add(line);
			}
		}

		return string.Join('\n', lines);
	}

	[GeneratedRegex(@"^\d{1,2} \d{1,2} \* \* [0-6]$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex WeeklyCron();

	/// <summary>
	///     A .NET CLI invocation: <c>dotnet build ...</c> (the WorkflowContractTests pattern), or the executable passed as a
	///     quoted name, as the health scripts do through <c>Invoke-NativeCommand -FilePath 'dotnet'</c>.
	/// </summary>
	[GeneratedRegex(@"(?m)(?:^|[\s;(|{&])dotnet\s|(?<![\w.-])['""]dotnet['""]", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex DotnetInvocation();

	[GeneratedRegex(@"(?:^|[\s(])\.?/?(?<path>(?:eng|tests)/[\w./-]+\.ps1)\b", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
		matchTimeoutMilliseconds: 1000)]
	private static partial Regex ScriptReference();

	[GeneratedRegex(@"<#.*?#>", RegexOptions.Singleline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex CommentBlock();
}
