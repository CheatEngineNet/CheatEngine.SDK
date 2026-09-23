namespace CheatEngine.SDK.Repository.Tests.Governance;

/// <summary>
///     The PR policy rules (<c>eng/ci/PullRequestPolicy.psm1</c>) against their vectors, and the entry point
///     <c>eng/ci/Test-PullRequestPolicy.ps1</c> end to end: exit code, one annotation per failed rule, the job summary
///     table, and no echo of the pull request description (audit register PR-CQ-34).
/// </summary>
public sealed class PullRequestPolicyScriptTests(PullRequestPolicyFixture fixture) : IClassFixture<PullRequestPolicyFixture>
{
	private const string EntryScript = "eng/ci/Test-PullRequestPolicy.ps1";
	private const string ErrorAnnotation = "::error title=PR policy::";

	public static TheoryData<string> CaseNames => PullRequestPolicyCases.Names;

	[Theory]
	[MemberData(nameof(CaseNames))]
	public void Policy_verdict_matches_the_expected_rules(string caseName)
	{
		PullRequestPolicyCase policyCase = PullRequestPolicyCases.Get(caseName);
		IReadOnlyList<PolicyRuleResult> results = fixture.ResultsOf(caseName);

		List<string> reported = [];
		List<string> failed = [];
		foreach (PolicyRuleResult result in results)
		{
			reported.Add(result.Rule);
			Assert.False(string.IsNullOrWhiteSpace(result.Message), $"{caseName}: rule {result.Rule} has no message.");
			if (!result.Passed)
			{
				failed.Add(result.Rule);
			}
		}

		Assert.Equal(PullRequestPolicyCases.Rules, reported, StringComparer.Ordinal);
		Assert.True(policyCase.ExpectedFailures.Order(StringComparer.Ordinal).SequenceEqual(failed.Order(StringComparer.Ordinal), StringComparer.Ordinal),
			$"{caseName}: expected failures [{string.Join(", ", policyCase.ExpectedFailures)}], got [{string.Join(", ", failed)}].");
	}

	[Fact]
	public void Every_rule_is_exercised_by_a_failing_vector()
	{
		HashSet<string> exercised = new(StringComparer.Ordinal);
		foreach (PullRequestPolicyCase policyCase in PullRequestPolicyCases.All)
		{
			exercised.UnionWith(policyCase.ExpectedFailures);
		}

		Assert.Equal(PullRequestPolicyCases.Rules.Order(StringComparer.Ordinal), exercised.Order(StringComparer.Ordinal));
	}

	[Fact]
	public void Dependabot_authored_pull_requests_are_exempt_from_every_rule()
	{
		foreach (string caseName in (string[]) ["dependabot_exempt", "dependabot_exempt_even_for_a_broken_title"])
		{
			foreach (PolicyRuleResult result in fixture.ResultsOf(caseName))
			{
				Assert.True(result.Passed, $"{caseName}: {result.Rule} failed.");
				Assert.Equal("Dependabot pull request: title and changelog rules exempt.", result.Message);
			}
		}

		// Only the exact bot login is exempt; a user named "dependabot" is not.
		Assert.Contains(fixture.ResultsOf("dependabot_lookalike_login_is_not_exempt"), static result => !result.Passed);
	}

	[Fact]
	public void Changelog_failure_names_the_paths_and_both_remedies()
	{
		PolicyRuleResult changelog = Assert.Single(fixture.ResultsOf("libs_change_without_changelog"),
			static result => string.Equals(result.Rule, PullRequestPolicyCases.ChangelogEntry, StringComparison.Ordinal));

		Assert.False(changelog.Passed);
		Assert.Contains("`libs/CheatEngine.SDK.Engine/Scanning/A.cs`", changelog.Message, StringComparison.Ordinal);
		Assert.Contains("## [Unreleased]", changelog.Message, StringComparison.Ordinal);
		Assert.Contains("<!-- changelog: not-needed -->", changelog.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Entry_script_exits_non_zero_and_annotates_each_failed_rule()
	{
		using TemporaryDirectory directory = new();
		string changed = await WriteChangedFilesAsync(directory, "libs/CheatEngine.SDK.Engine/Scanning/A.cs");

		PwshResult run = await PwshScript.RunFileAsync(EntryScript, ["-ChangedFilesPath", changed],
			PullRequestEnvironment("Added CodeQL."));

		Assert.True(run.ExitCode == 1, run.Transcript);
		List<string> annotations = Annotations(run.StandardOutput);
		Assert.Equal(3, annotations.Count);
		Assert.Contains(annotations, static line => line.StartsWith(ErrorAnnotation + "TitleNoTrailingPeriod: ", StringComparison.Ordinal));
		Assert.Contains(annotations, static line => line.StartsWith(ErrorAnnotation + "TitleImperative: ", StringComparison.Ordinal));
		Assert.Contains(annotations, static line => line.StartsWith(ErrorAnnotation + "ChangelogEntry: ", StringComparison.Ordinal)
													&& line.Contains("libs/CheatEngine.SDK.Engine/Scanning/A.cs", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Entry_script_writes_a_rule_table_to_the_step_summary()
	{
		using TemporaryDirectory directory = new();
		string changed = await WriteChangedFilesAsync(directory, "libs/X/A.cs", "CHANGELOG.md");
		string summary = directory.File("summary.md");
		Dictionary<string, string> environment = PullRequestEnvironment("Add CodeQL analysis");
		environment["GITHUB_STEP_SUMMARY"] = summary;

		PwshResult run = await PwshScript.RunFileAsync(EntryScript, ["-ChangedFilesPath", changed], environment);

		Assert.True(run.ExitCode == 0, run.Transcript);
		Assert.Empty(Annotations(run.StandardOutput));
		string[] lines = await File.ReadAllLinesAsync(summary, TestContext.Current.CancellationToken);
		Assert.Contains("| Rule | Result | Detail |", lines, StringComparer.Ordinal);
		foreach (string rule in PullRequestPolicyCases.Rules)
		{
			Assert.Contains(lines, line => line.StartsWith($"| {rule} | Passed | ", StringComparison.Ordinal));
		}
	}

	[Fact]
	public async Task Entry_script_exempts_dependabot_and_never_prints_the_description()
	{
		using TemporaryDirectory directory = new();
		string changed = await WriteChangedFilesAsync(directory, "libs/CheatEngine.SDK.Lua/packages.lock.json", "libs/X/A.cs");
		const string Sentinel = "DESCRIPTION-SENTINEL-7f3a";
		Dictionary<string, string> environment = PullRequestEnvironment("bump stuff.", "dependabot[bot]");
		environment["PR_BODY"] = "Release notes " + Sentinel;

		PwshResult run = await PwshScript.RunFileAsync(EntryScript, ["-ChangedFilesPath", changed], environment);

		Assert.True(run.ExitCode == 0, run.Transcript);
		Assert.Contains("Dependabot pull request: title and changelog rules exempt.", run.StandardOutput, StringComparison.Ordinal);
		Assert.DoesNotContain(Sentinel, run.StandardOutput + run.StandardError, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Entry_script_refuses_commit_ids_that_are_not_full_hashes()
	{
		Dictionary<string, string> environment = PullRequestEnvironment("Add CodeQL analysis");
		environment["BASE_SHA"] = "main";
		environment["HEAD_SHA"] = "HEAD";

		PwshResult run = await PwshScript.RunFileAsync(EntryScript, [], environment);

		Assert.NotEqual(0, run.ExitCode);
		Assert.Contains("must be full commit ids", run.StandardOutput + run.StandardError, StringComparison.Ordinal);
	}

	private static Dictionary<string, string> PullRequestEnvironment(string title, string author = "contributor")
	{
		return new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["PR_TITLE"] = title,
			["PR_BODY"] = "",
			["PR_AUTHOR"] = author
		};
	}

	private static async Task<string> WriteChangedFilesAsync(TemporaryDirectory directory, params string[] paths)
	{
		string path = directory.File("changed.txt");
		await File.WriteAllLinesAsync(path, paths, TestContext.Current.CancellationToken);
		return path;
	}

	private static List<string> Annotations(string output)
	{
		List<string> annotations = [];
		foreach (string line in output.ReplaceLineEndings("\n").Split('\n'))
		{
			if (line.StartsWith(ErrorAnnotation, StringComparison.Ordinal))
			{
				annotations.Add(line);
			}
		}

		return annotations;
	}
}
