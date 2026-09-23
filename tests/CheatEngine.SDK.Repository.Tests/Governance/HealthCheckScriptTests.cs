using System.Text.Json;
using System.Text.Json.Nodes;

namespace CheatEngine.SDK.Repository.Tests.Governance;

/// <summary>
///     The scheduled health scripts (<c>eng/ci/health</c>): the pure decisions of <c>HealthCheck.psm1</c> against their
///     vectors, and the two scripts that can run offline end to end, the canary SDK pin and the health issue publisher
///     (with <c>gh</c> replaced by a recorder). The scripts that need the network, the .NET SDK or xmake are verified by
///     local runs (audit register PR-CQ-55, PR-CQ-30, A21-25).
/// </summary>
public sealed class HealthCheckScriptTests(HealthCheckFixture fixture) : IClassFixture<HealthCheckFixture>
{
	private const string CanaryPinScript = "eng/ci/health/Set-CanarySdkVersion.ps1";
	private const string IssueScript = "eng/ci/health/Publish-HealthIssue.ps1";
	private const string IssueTitle = "Scheduled health check needs attention";
	private const string Repository = "CheatEngineNet/CheatEngine.SDK";

	[Fact]
	public void Every_exported_health_function_has_vectors()
	{
		SortedSet<string> covered = new(StringComparer.Ordinal) { "Get-UpdatedGlobalJson" };
		foreach (PwshCall call in HealthCheckCases.All)
		{
			covered.Add(call.Function);
		}

		Assert.Equal(covered, new SortedSet<string>(fixture.ExportedFunctions, StringComparer.Ordinal));
	}

	[Fact]
	public void Release_tag_selection_takes_the_newest_plain_version()
	{
		Assert.Equal("v1.10.0", String("tag_newest_by_version"));
		Assert.Equal("v1.0.0", String("tag_ignores_prerelease_and_malformed"));
		Assert.Equal(JsonValueKind.Null, fixture.Result("tag_none").Single("tag_none").ValueKind);
		Assert.Equal(JsonValueKind.Null, fixture.Result("tag_empty").Single("tag_empty").ValueKind);
	}

	[Theory]
	[InlineData("drift_reproduced", "Reproduced", "byte for byte")]
	[InlineData("drift_toolchain", "ToolchainDrift", "same fingerprint")]
	[InlineData("drift_path_dependent", "Failed", "depends on its path")]
	[InlineData("drift_fingerprint_mismatch", "Failed", "exports the fingerprint '0000:1111'")]
	[InlineData("drift_fingerprint_missing", "Failed", "exports the fingerprint ''")]
	[InlineData("drift_release_unreadable", "Failed", "could not be read")]
	[InlineData("drift_committed_differs_from_release", "Failed", "is not the bridge the released package ships")]
	[InlineData("drift_uppercase_hash_is_malformed", "Failed", "64 lowercase hex digits")]
	public void Bridge_drift_classification_matches_the_vectors(string caseName, string classification, string reason)
	{
		JsonElement verdict = fixture.Result(caseName).Single(caseName);

		Assert.Equal(classification, verdict.GetProperty("Classification").GetString());
		List<string> reasons = [];
		foreach (JsonElement item in verdict.GetProperty("Reasons").EnumerateArray())
		{
			reasons.Add(item.GetString() ?? "");
		}

		Assert.Contains(reasons, text => text.Contains(reason, StringComparison.Ordinal));
	}

	[Fact]
	public void Sdk_versions_are_full_versions_of_the_pinned_channel()
	{
		Assert.Equal("10.0", String("channel_of_feature_band"));
		Assert.Equal("11.0", String("channel_of_next_major"));
		Assert.Contains("not a full .NET SDK version", Error("channel_rejects_short_version"), StringComparison.Ordinal);
		Assert.Contains("not a full .NET SDK version", Error("channel_rejects_prerelease"), StringComparison.Ordinal);

		Assert.Equal("10.0.402", String("newest_sdk"));
		Assert.Contains("not on the 10.0 channel", Error("newest_sdk_other_channel"), StringComparison.Ordinal);
		Assert.Contains("no latest-sdk property", Error("newest_sdk_missing"), StringComparison.Ordinal);
	}

	[Fact]
	public void Global_json_rewrite_changes_only_the_sdk_version()
	{
		JsonObject original = Assert.IsType<JsonObject>(JsonNode.Parse(RepositoryFile.ReadText("global.json")));
		JsonObject rewritten = Assert.IsType<JsonObject>(JsonNode.Parse(String(HealthCheckFixture.GlobalJsonCase)!));

		Assert.Equal(HealthCheckFixture.CanaryVersion, (string?) rewritten["sdk"]!["version"]);
		original["sdk"]!["version"] = HealthCheckFixture.CanaryVersion;
		// rollForward, allowPrerelease, errorMessage and the Microsoft.Testing.Platform runner survive, in order.
		Assert.True(JsonNode.DeepEquals(original, rewritten), $"Only sdk.version may change:{Environment.NewLine}{rewritten}");
		Assert.Equal(KeysOf(original), KeysOf(rewritten));
		Assert.Equal(KeysOf(original["sdk"]!.AsObject()), KeysOf(rewritten["sdk"]!.AsObject()));

		Assert.Contains("not a full .NET SDK version", Error("global_json_rejects_invalid_version"), StringComparison.Ordinal);
		Assert.Contains("no sdk.version property", Error("global_json_without_sdk_version"), StringComparison.Ordinal);
	}

	[Fact]
	public void Hang_dump_detection_needs_the_exact_package()
	{
		Assert.True(fixture.Result("package_reference_present").Single("present").GetBoolean());
		Assert.False(fixture.Result("package_reference_absent").Single("absent").GetBoolean());
		Assert.False(fixture.Result("package_reference_prefix_is_not_a_match").Single("prefix").GetBoolean());
	}

	[Fact]
	public void Trx_summary_reads_the_result_counters()
	{
		JsonElement summary = fixture.Result("trx_counters").Single("trx_counters");

		Assert.Equal("Failed", summary.GetProperty("Outcome").GetString());
		Assert.Equal(12, summary.GetProperty("Total").GetInt32());
		Assert.Equal(11, summary.GetProperty("Executed").GetInt32());
		Assert.Equal(10, summary.GetProperty("Passed").GetInt32());
		Assert.Equal(1, summary.GetProperty("Failed").GetInt32());
		Assert.Equal(1, summary.GetProperty("NotExecuted").GetInt32());
		Assert.Contains("no ResultSummary/Counters", Error("trx_without_summary"), StringComparison.Ordinal);
	}

	[Fact]
	public void Package_list_reports_flatten_to_one_row_per_package()
	{
		List<JsonElement> vulnerable = fixture.Result("package_list_vulnerable").Items("vulnerable");
		Assert.Equal(3, vulnerable.Count);
		Assert.Equal("(problem)", vulnerable[0].GetProperty("Package").GetString());
		Assert.Equal("tests/X.Tests/X.Tests.csproj", vulnerable[0].GetProperty("Project").GetString());
		Assert.Equal("warning: No assets file.", vulnerable[0].GetProperty("Detail").GetString());

		Assert.Equal("Contoso.Direct", vulnerable[1].GetProperty("Package").GetString());
		Assert.Equal("tests/B.Tests/B.Tests.csproj", vulnerable[1].GetProperty("Project").GetString());
		Assert.Equal("net10.0", vulnerable[1].GetProperty("Framework").GetString());
		Assert.False(vulnerable[1].GetProperty("Transitive").GetBoolean());
		Assert.Equal("High https://github.com/advisories/GHSA-aaaa-bbbb-cccc", vulnerable[1].GetProperty("Detail").GetString());

		Assert.Equal("Contoso.Transitive", vulnerable[2].GetProperty("Package").GetString());
		Assert.Equal("2.1.0", vulnerable[2].GetProperty("Resolved").GetString());
		Assert.True(vulnerable[2].GetProperty("Transitive").GetBoolean());
		Assert.Equal("Low https://github.com/advisories/GHSA-1111-2222-3333; Moderate https://github.com/advisories/GHSA-4444-5555-6666",
			vulnerable[2].GetProperty("Detail").GetString());

		JsonElement deprecated = Assert.Single(fixture.Result("package_list_deprecated").Items("deprecated"));
		Assert.Equal("src/P/P.csproj", deprecated.GetProperty("Project").GetString());
		Assert.Equal("Legacy, CriticalBugs; use Contoso.New >= 4.0.0", deprecated.GetProperty("Detail").GetString());

		Assert.Empty(fixture.Result("package_list_clean").Items("clean"));
		Assert.Contains("\"version\": 1", Error("package_list_unknown_version"), StringComparison.Ordinal);
	}

	[Fact]
	public void Solution_project_paths_use_forward_slashes()
	{
		List<string> paths = [];
		foreach (JsonElement path in fixture.Result("solution_projects").Items("solution"))
		{
			paths.Add(path.GetString() ?? "");
		}

		Assert.Equal(["libs/A/A.csproj", "tests/B.Tests/B.Tests.csproj"], paths);
	}

	[Fact]
	public void External_links_skip_fences_local_hosts_templates_and_offline_checked_self_links()
	{
		List<string> links = [];
		foreach (JsonElement link in fixture.Result("links_extracted").Items("links"))
		{
			links.Add(link.GetString() ?? "");
		}

		Assert.Equal(
			[
				"https://learn.microsoft.com/nuget/concepts/auditing-packages#running-nuget-audit-in-ci",
				"https://www.contributor-covenant.org/faq",
				"https://docs.zizmor.sh/usage/",
				"https://github.com/CheatEngineNet/CheatEngine.SDK/security/advisories/new",
				"https://github.com/cheat-engine/cheat-engine"
			],
			links);
	}

	[Theory]
	[InlineData("verdict_200", "Ok")]
	[InlineData("verdict_301", "Ok")]
	[InlineData("verdict_404", "Broken")]
	[InlineData("verdict_410", "Broken")]
	[InlineData("verdict_429", "Inconclusive")]
	[InlineData("verdict_403", "Inconclusive")]
	[InlineData("verdict_500", "Inconclusive")]
	[InlineData("verdict_no_answer", "Inconclusive")]
	public void Only_not_found_and_gone_count_as_broken_links(string caseName, string verdict)
	{
		Assert.Equal(verdict, String(caseName));
	}

	[Fact]
	public void Health_issue_is_needed_only_for_a_failed_job_drift_or_broken_links()
	{
		JsonElement healthy = fixture.Result("issue_healthy").Single("healthy");
		Assert.False(healthy.GetProperty("NeedsAttention").GetBoolean());
		Assert.Equal(IssueTitle, healthy.GetProperty("Title").GetString());

		JsonElement failed = fixture.Result("issue_failed_job").Single("failed");
		Assert.True(failed.GetProperty("NeedsAttention").GetBoolean());
		string body = failed.GetProperty("Body").GetString() ?? "";
		Assert.Contains("| audit | failure |", body, StringComparison.Ordinal);
		Assert.Contains("| canary | success |", body, StringComparison.Ordinal);
		Assert.Contains("| test-repeat | cancelled |", body, StringComparison.Ordinal);
		Assert.Contains($"Run: {HealthCheckCases.RunUrl}", body, StringComparison.Ordinal);

		JsonElement drift = fixture.Result("issue_drift_only").Single("drift");
		Assert.True(drift.GetProperty("NeedsAttention").GetBoolean());
		Assert.Contains("health-bridge-drift", drift.GetProperty("Body").GetString(), StringComparison.Ordinal);

		JsonElement links = fixture.Result("issue_broken_links_only").Single("links");
		Assert.True(links.GetProperty("NeedsAttention").GetBoolean());
		Assert.Contains("External documentation links are broken", links.GetProperty("Body").GetString(), StringComparison.Ordinal);

		Assert.Contains("non-empty toJSON(needs)", Error("issue_rejects_empty_needs"), StringComparison.Ordinal);
	}

	[Fact]
	public void Health_issue_body_carries_only_closed_vocabularies()
	{
		string body = fixture.Result("issue_sanitizes_values").Single("sanitized").GetProperty("Body").GetString() ?? "";

		Assert.Contains("| (unexpected) | success |", body, StringComparison.Ordinal);
		Assert.Contains("| canary | (unexpected) |", body, StringComparison.Ordinal);
		foreach (string injected in (string[]) ["evil", "<script>", "click", "Run:"])
		{
			Assert.DoesNotContain(injected, body, StringComparison.Ordinal);
		}
	}

	[Fact]
	public void Health_issue_selection_takes_the_open_issue_with_the_exact_title()
	{
		Assert.Equal(11, fixture.Result("issue_selected_exact_title_lowest_number").Single("selected").GetInt32());
		Assert.Equal(JsonValueKind.Null, fixture.Result("issue_none_open").Single("none").ValueKind);
		Assert.Equal(JsonValueKind.Null, fixture.Result("issue_list_empty").Single("empty").ValueKind);
	}

	[Fact]
	public async Task Canary_pin_rewrites_only_the_sdk_version_and_writes_the_step_outputs()
	{
		using TemporaryDirectory directory = new();
		string globalJson = directory.File("global.json");
		string original = RepositoryFile.ReadText("global.json");
		await File.WriteAllTextAsync(globalJson, original, TestContext.Current.CancellationToken);
		string pinned = (string?) JsonNode.Parse(original)!["sdk"]!["version"] ?? "";

		PwshResult run = await PwshScript.RunFileAsync(CanaryPinScript, ["-SdkVersion", "10.0.499", "-GlobalJsonPath", globalJson],
			ActionsFiles(directory));

		Assert.True(run.ExitCode == 0, run.Transcript);
		JsonObject rewritten = Assert.IsType<JsonObject>(JsonNode.Parse(await File.ReadAllTextAsync(globalJson, TestContext.Current.CancellationToken)));
		Assert.Equal("10.0.499", (string?) rewritten["sdk"]!["version"]);
		Assert.Equal("Microsoft.Testing.Platform", (string?) rewritten["test"]!["runner"]);
		Assert.Equal("disable", (string?) rewritten["sdk"]!["rollForward"]);
		string[] outputs = await File.ReadAllLinesAsync(directory.File("output.txt"), TestContext.Current.CancellationToken);
		Assert.Equal([$"pinned-version={pinned}", "sdk-version=10.0.499", "changed=true"], outputs);
		Assert.Contains("global.json now selects 10.0.499", await File.ReadAllTextAsync(directory.File("summary.md"),
			TestContext.Current.CancellationToken), StringComparison.Ordinal);
	}

	[Fact]
	public async Task Canary_pin_keeps_global_json_when_the_newest_sdk_is_the_pinned_one()
	{
		using TemporaryDirectory directory = new();
		string globalJson = directory.File("global.json");
		byte[] original = await File.ReadAllBytesAsync(RepositoryFile.FullPath("global.json"), TestContext.Current.CancellationToken);
		await File.WriteAllBytesAsync(globalJson, original, TestContext.Current.CancellationToken);
		string pinned = (string?) JsonNode.Parse(original)!["sdk"]!["version"] ?? "";

		PwshResult run = await PwshScript.RunFileAsync(CanaryPinScript, ["-SdkVersion", pinned, "-GlobalJsonPath", globalJson],
			ActionsFiles(directory));

		Assert.True(run.ExitCode == 0, run.Transcript);
		Assert.Equal(original, await File.ReadAllBytesAsync(globalJson, TestContext.Current.CancellationToken));
		Assert.Contains("changed=false", await File.ReadAllLinesAsync(directory.File("output.txt"), TestContext.Current.CancellationToken), StringComparer.Ordinal);
	}

	[Fact]
	public async Task Canary_pin_refuses_a_version_of_another_channel()
	{
		using TemporaryDirectory directory = new();
		string globalJson = directory.File("global.json");
		await File.WriteAllTextAsync(globalJson, RepositoryFile.ReadText("global.json"), TestContext.Current.CancellationToken);

		PwshResult run = await PwshScript.RunFileAsync(CanaryPinScript, ["-SdkVersion", "11.0.100", "-GlobalJsonPath", globalJson],
			ActionsFiles(directory));

		Assert.NotEqual(0, run.ExitCode);
		Assert.Contains("a new major is a migration", run.StandardError + run.StandardOutput, StringComparison.Ordinal);
		Assert.Equal(RepositoryFile.ReadText("global.json"), await File.ReadAllTextAsync(globalJson, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task Health_issue_publisher_creates_the_issue_when_none_is_open()
	{
		using TemporaryDirectory directory = new();

		(PwshResult run, string log) = await PublishAsync(directory, HealthCheckCases.NeedsOneFailed, "true", "[]");

		Assert.True(run.ExitCode == 0, run.Transcript);
		string[] calls = GhCalls(log);
		Assert.Equal(3, calls.Length);
		Assert.Equal($"api repos/{Repository} --jq .has_issues", calls[0]);
		Assert.StartsWith($"issue list --repo {Repository} --state open --json number,title", calls[1], StringComparison.Ordinal);
		Assert.StartsWith($"issue create --repo {Repository} --title {IssueTitle} --label ci --body-file ", calls[2], StringComparison.Ordinal);
		Assert.Contains("| audit | failure |", log, StringComparison.Ordinal);
		Assert.Contains($"Run: {HealthCheckCases.RunUrl}", log, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Health_issue_publisher_comments_on_the_open_issue()
	{
		using TemporaryDirectory directory = new();

		(PwshResult run, string log) = await PublishAsync(directory, HealthCheckCases.NeedsOneFailed, "true", HealthCheckCases.IssueList);

		Assert.True(run.ExitCode == 0, run.Transcript);
		string[] calls = GhCalls(log);
		Assert.Equal(3, calls.Length);
		Assert.StartsWith($"issue comment 11 --repo {Repository} --body-file ", calls[2], StringComparison.Ordinal);
		Assert.DoesNotContain(calls, static call => call.StartsWith("issue create", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Health_issue_publisher_stays_silent_for_a_healthy_run()
	{
		using TemporaryDirectory directory = new();

		(PwshResult run, string log) = await PublishAsync(directory, HealthCheckCases.NeedsAllSucceeded, "true", "[]");

		Assert.True(run.ExitCode == 0, run.Transcript);
		Assert.Empty(GhCalls(log));
		Assert.Contains("no issue to open or update", run.StandardOutput, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Health_issue_publisher_only_warns_when_issues_are_disabled()
	{
		using TemporaryDirectory directory = new();

		(PwshResult run, string log) = await PublishAsync(directory, HealthCheckCases.NeedsOneFailed, "false", "[]");

		Assert.True(run.ExitCode == 0, run.Transcript);
		Assert.Equal([$"api repos/{Repository} --jq .has_issues"], GhCalls(log));
		Assert.Contains("::warning title=Scheduled health::Issues are disabled", run.StandardOutput, StringComparison.Ordinal);
	}

	private string? String(string caseName)
	{
		return fixture.Result(caseName).Single(caseName).GetString();
	}

	private string Error(string caseName)
	{
		string? error = fixture.Result(caseName).Error;
		Assert.True(error is not null, $"{caseName} was expected to throw.");
		return error;
	}

	private static List<string> KeysOf(JsonObject node)
	{
		List<string> keys = [];
		foreach (KeyValuePair<string, JsonNode?> property in node)
		{
			keys.Add(property.Key);
		}

		return keys;
	}

	private static Dictionary<string, string> ActionsFiles(TemporaryDirectory directory)
	{
		return new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["GITHUB_OUTPUT"] = directory.File("output.txt"),
			["GITHUB_STEP_SUMMARY"] = directory.File("summary.md")
		};
	}

	/// <summary>Runs the publisher with <c>gh</c> replaced by a recorder that answers the two queries it makes.</summary>
	private static async Task<(PwshResult Run, string Log)> PublishAsync(TemporaryDirectory directory, string needs, string hasIssues,
		string openIssues)
	{
		string bin = directory.File("bin");
		Directory.CreateDirectory(bin);
		string log = directory.File("gh.log");
		await File.WriteAllTextAsync(Path.Combine(bin, "gh.ps1"), """
			$line = $args -join ' '
			Add-Content -LiteralPath $env:FAKE_GH_LOG -Value "CALL:$line" -Encoding utf8
			$bodyIndex = [Array]::IndexOf($args, '--body-file')
			if ($bodyIndex -ge 0) {
			    Add-Content -LiteralPath $env:FAKE_GH_LOG -Value (Get-Content -Raw -LiteralPath $args[$bodyIndex + 1]) -Encoding utf8
			}
			if ($args[0] -eq 'api') { Write-Output $env:FAKE_GH_HAS_ISSUES }
			if ($args[0] -eq 'issue' -and $args[1] -eq 'list') { Write-Output $env:FAKE_GH_ISSUES }
			exit 0
			""", TestContext.Current.CancellationToken);
		await File.WriteAllTextAsync(log, "", TestContext.Current.CancellationToken);

		Dictionary<string, string> environment = ActionsFiles(directory);
		environment["PATH"] = bin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
		environment["FAKE_GH_LOG"] = log;
		environment["FAKE_GH_HAS_ISSUES"] = hasIssues;
		environment["FAKE_GH_ISSUES"] = openIssues;
		environment["NEEDS"] = needs;
		environment["RUN_URL"] = HealthCheckCases.RunUrl;
		environment["GH_REPO"] = Repository;

		PwshResult run = await PwshScript.RunFileAsync(IssueScript, [], environment);
		return (run, await File.ReadAllTextAsync(log, TestContext.Current.CancellationToken));
	}

	private static string[] GhCalls(string log)
	{
		List<string> calls = [];
		foreach (string line in log.ReplaceLineEndings("\n").Split('\n'))
		{
			if (line.StartsWith("CALL:", StringComparison.Ordinal))
			{
				calls.Add(line["CALL:".Length..]);
			}
		}

		return [.. calls];
	}
}
