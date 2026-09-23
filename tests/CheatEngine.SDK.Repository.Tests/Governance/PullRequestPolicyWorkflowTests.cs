using System.Text.RegularExpressions;

using YamlDotNet.RepresentationModel;

namespace CheatEngine.SDK.Repository.Tests.Governance;

/// <summary>
///     <c>.github/workflows/pr-policy.yml</c>, the second required check (shared-contracts §1.2, §1.3, §1.12): it runs on
///     every title edit, cannot be skipped by a filter or a condition, and receives pull-request text only through the
///     environment (no template injection).
/// </summary>
public sealed partial class PullRequestPolicyWorkflowTests
{
	private const string WorkflowPath = ".github/workflows/pr-policy.yml";
	private const string ModulePath = "eng/ci/PullRequestPolicy.psm1";

	[Fact]
	public void Pr_policy_triggers_on_edited_and_has_no_path_filter()
	{
		YamlDocument workflow = YamlDocument.Load(WorkflowPath);

		Assert.Equal(["pull_request"], workflow.Triggers);
		YamlNode? pullRequest = workflow.Trigger("pull_request");
		Assert.Equal(["opened", "edited", "synchronize", "reopened", "ready_for_review"],
			YamlDocument.Scalars(pullRequest, "types"));
		foreach (string filter in (string[]) ["paths", "paths-ignore", "branches", "branches-ignore"])
		{
			Assert.True(YamlDocument.Child(pullRequest, filter) is null,
				$"{WorkflowPath} must not filter on '{filter}': a filtered required check stays pending forever.");
		}
	}

	[Fact]
	public void Pr_policy_job_is_named_PR_policy_and_runs_on_ubuntu_24_04()
	{
		YamlDocument workflow = YamlDocument.Load(WorkflowPath);

		KeyValuePair<string, YamlMappingNode> job = Assert.Single(workflow.Jobs);
		Assert.Equal("policy", job.Key);
		Assert.Equal("PR policy", YamlDocument.Scalar(job.Value, "name"));
		Assert.Equal("ubuntu-24.04", YamlDocument.Scalar(job.Value, "runs-on"));
		Assert.NotNull(YamlDocument.Scalar(job.Value, "timeout-minutes"));
		// A skipped job reports success to a required check, so the job has no condition at all (drafts included).
		Assert.Null(YamlDocument.Child(job.Value, "if"));
		Assert.Equal(new Dictionary<string, string>(StringComparer.Ordinal) { ["contents"] = "read" },
			YamlDocument.Permissions(workflow.Root));
		Assert.Null(YamlDocument.Permissions(job.Value));

		YamlMappingNode checkout = Assert.Single(YamlDocument.Steps(job.Value),
			static step => YamlDocument.UsesAction(step, "actions/checkout"));
		Assert.Equal("0", YamlDocument.Scalar(YamlDocument.Child(checkout, "with"), "fetch-depth"));
		Assert.Equal("false", YamlDocument.Scalar(YamlDocument.Child(checkout, "with"), "persist-credentials"));
	}

	[Fact]
	public void Pull_request_title_body_and_author_reach_the_script_only_through_env()
	{
		YamlDocument workflow = YamlDocument.Load(WorkflowPath);
		YamlMappingNode step = Assert.Single(YamlDocument.Steps(workflow.Job("policy")),
			static step => YamlDocument.Scalar(step, "run") is not null);

		// The script's exit code fails the step explicitly (the repository rule for every native command in a workflow).
		string[] script = (YamlDocument.Scalar(step, "run") ?? "").ReplaceLineEndings("\n").Trim().Split('\n');
		Assert.Equal("$ErrorActionPreference = 'Stop'", script[0]);
		Assert.Equal("./eng/ci/Test-PullRequestPolicy.ps1", script[1]);
		Assert.StartsWith("if ($LASTEXITCODE -ne 0)", script[2], StringComparison.Ordinal);
		YamlNode? env = YamlDocument.Child(step, "env");
		Assert.Equal("${{ github.event.pull_request.title }}", YamlDocument.Scalar(env, "PR_TITLE"));
		Assert.Equal("${{ github.event.pull_request.body }}", YamlDocument.Scalar(env, "PR_BODY"));
		Assert.Equal("${{ github.event.pull_request.user.login }}", YamlDocument.Scalar(env, "PR_AUTHOR"));
		Assert.Equal("${{ github.event.pull_request.base.sha }}", YamlDocument.Scalar(env, "BASE_SHA"));
		Assert.Equal("${{ github.event.pull_request.head.sha }}", YamlDocument.Scalar(env, "HEAD_SHA"));

		// No workflow of the repository expands attacker-controlled pull-request text inside a script.
		List<string> offenders = [];
		foreach (string path in GovernanceWorkflows.AllWorkflowPaths())
		{
			YamlDocument document = YamlDocument.Load(path);
			foreach (KeyValuePair<string, YamlMappingNode> job in document.Jobs)
			{
				foreach (YamlMappingNode jobStep in YamlDocument.Steps(job.Value))
				{
					string? run = YamlDocument.Scalar(jobStep, "run");
					if (run is not null && UntrustedExpression().IsMatch(run))
					{
						offenders.Add($"{path} job {job.Key} step '{YamlDocument.Scalar(jobStep, "name")}'");
					}
				}
			}
		}

		Assert.True(offenders.Count == 0,
			$"Pass pull-request text through env: instead of expanding it in run: (template injection): {string.Join("; ", offenders)}");
	}

	[Fact]
	public void Changelog_path_pattern_matches_the_shared_contract()
	{
		string module = RepositoryFile.ReadText(ModulePath);

		Assert.Equal("^(libs|src|analyzers|source-generators|native)/", ModuleConstant(module, "ChangelogPathPattern"));
		Assert.Equal("(^|/)packages\\.lock\\.json$", ModuleConstant(module, "ChangelogExcludedPattern"));
		Assert.Equal("CHANGELOG.md", ModuleConstant(module, "ChangelogFile"));
		Assert.Equal("<!--\\s*changelog:\\s*not-needed\\s*-->", ModuleConstant(module, "ChangelogWaiverPattern"));
		Assert.Equal("dependabot[bot]", ModuleConstant(module, "DependabotLogin"));
		Assert.Equal("^\\w+(\\([^)]*\\))?!?:\\s", ModuleConstant(module, "ConventionalPrefixPattern"));
	}

	[Fact]
	public void Every_consumer_visible_root_of_the_changelog_rule_exists()
	{
		// A renamed root would silently switch the CHANGELOG rule off for it.
		foreach (string root in (string[]) ["libs", "src", "analyzers", "source-generators", "native"])
		{
			Assert.True(RepositoryFile.ExistsWithExactCase(root, out bool isDirectory) && isDirectory,
				$"The CHANGELOG rule of {ModulePath} names '{root}/', which is not a folder of the repository.");
		}

		Assert.True(RepositoryFile.ExistsWithExactCase("CHANGELOG.md", out bool changelogIsDirectory) && !changelogIsDirectory);
	}

	private static string ModuleConstant(string module, string name)
	{
		Match match = Regex.Match(module, "^\\$" + name + " = '(?<value>[^']*)'\\r?$",
			RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));
		Assert.True(match.Success, $"{ModulePath} must declare ${name} = '...' in its constants block.");
		return match.Groups["value"].Value;
	}

	[GeneratedRegex(@"\$\{\{[^}]*\b(github\.event\.(pull_request|issue|comment|review|head_commit|commits)\b|github\.head_ref\b)",
		RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
	private static partial Regex UntrustedExpression();
}
