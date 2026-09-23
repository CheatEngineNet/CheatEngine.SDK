using System.Text.Json;
using System.Text.RegularExpressions;

using YamlDotNet.RepresentationModel;

namespace CheatEngine.SDK.Repository.Tests.Governance;

/// <summary>
///     The JSON payloads of <c>eng/github</c>, applied by <c>Set-RepositorySettings.ps1</c> (audit register PR-CQ-17;
///     shared-contracts §1.2): exactly two required checks from GitHub Actions, squash-only merges titled by the pull
///     request, admin-only release tags, a reviewed <c>nuget</c> environment without admin bypass, SHA-pinned actions.
/// </summary>
public sealed partial class RepositorySettingsPayloadTests
{
	private const string Directory = "eng/github";
	private const int GitHubActionsAppId = 15368;
	private const int RepositoryAdminRole = 5;

	/// <summary>Top-level fields each payload may set (documented REST body fields, plus the script's own keys).</summary>
	private static readonly Dictionary<string, string[]> s_allowedFields = new(StringComparer.Ordinal)
	{
		["repository.json"] =
		[
			"allow_squash_merge", "allow_merge_commit", "allow_rebase_merge", "squash_merge_commit_title", "squash_merge_commit_message",
			"delete_branch_on_merge", "has_issues", "has_discussions", "security_and_analysis"
		],
		["actions-permissions.json"] = ["enabled", "allowed_actions", "sha_pinning_required"],
		["actions-workflow-permissions.json"] = ["default_workflow_permissions", "can_approve_pull_request_reviews"],
		["labels.json"] = ["labels"],
		["rulesets/protect-main.json"] = ["name", "target", "enforcement", "bypass_actors", "conditions", "rules"],
		["rulesets/protect-release-tags.json"] = ["name", "target", "enforcement", "bypass_actors", "conditions", "rules"],
		["environments/nuget.json"] = ["name", "settings", "deployment_branch_policies"]
	};

	[Fact]
	public void Every_payload_explains_itself_and_sets_only_known_fields()
	{
		foreach ((string file, string[] allowed) in s_allowedFields)
		{
			using JsonDocument payload = Load(file);
			JsonElement comment = payload.RootElement.GetProperty("_comment");
			Assert.Equal(JsonValueKind.Array, comment.ValueKind);
			Assert.All(comment.EnumerateArray(), static line => Assert.False(string.IsNullOrWhiteSpace(line.GetString())));
			foreach (JsonProperty property in payload.RootElement.EnumerateObject())
			{
				Assert.True(property.Name.StartsWith('_') || Array.IndexOf(allowed, property.Name) >= 0,
					$"{Directory}/{file} sets '{property.Name}', which is not a field of its GitHub endpoint.");
			}
		}

		// Every payload of the folder is covered: a new file needs a row above.
		List<string> files = [];
		foreach (string path in System.IO.Directory.EnumerateFiles(RepositoryFile.FullPath(Directory), "*.json", SearchOption.AllDirectories))
		{
			files.Add(Path.GetRelativePath(RepositoryFile.FullPath(Directory), path).Replace('\\', '/'));
		}

		Assert.Equal(s_allowedFields.Keys.Order(StringComparer.Ordinal), files.Order(StringComparer.Ordinal));
	}

	[Fact]
	public void Main_ruleset_requires_exactly_the_gate_and_pr_policy_checks_from_github_actions()
	{
		using JsonDocument ruleset = Load("rulesets/protect-main.json");
		JsonElement parameters = Rule(ruleset, "required_status_checks").GetProperty("parameters");

		List<(string Context, int App)> checks = [];
		foreach (JsonElement check in parameters.GetProperty("required_status_checks").EnumerateArray())
		{
			checks.Add((check.GetProperty("context").GetString() ?? "", check.GetProperty("integration_id").GetInt32()));
		}

		Assert.Equal([("CI / Gate", GitHubActionsAppId), ("PR policy", GitHubActionsAppId)], checks);
		// Without a merge queue, strict mode would force a rebase and a full run before every merge.
		Assert.False(parameters.GetProperty("strict_required_status_checks_policy").GetBoolean());
		Assert.Equal("~DEFAULT_BRANCH", Assert.Single(ruleset.RootElement.GetProperty("conditions").GetProperty("ref_name").GetProperty("include")
			.EnumerateArray()).GetString());
		Assert.Equal("active", ruleset.RootElement.GetProperty("enforcement").GetString());
	}

	[Fact]
	public void Main_ruleset_allows_only_squash_merges_without_bypass_or_code_owner_review()
	{
		using JsonDocument ruleset = Load("rulesets/protect-main.json");
		JsonElement pullRequest = Rule(ruleset, "pull_request").GetProperty("parameters");

		Assert.Equal("squash", Assert.Single(pullRequest.GetProperty("allowed_merge_methods").EnumerateArray()).GetString());
		Assert.False(pullRequest.GetProperty("require_code_owner_review").GetBoolean());
		Assert.Equal(0, pullRequest.GetProperty("required_approving_review_count").GetInt32());
		Assert.True(pullRequest.GetProperty("dismiss_stale_reviews_on_push").GetBoolean());
		Assert.Equal(0, ruleset.RootElement.GetProperty("bypass_actors").GetArrayLength());
		Rule(ruleset, "deletion");
		Rule(ruleset, "non_fast_forward");
	}

	[Fact]
	public void Release_tag_ruleset_protects_v_tags_and_lets_only_admins_bypass()
	{
		using JsonDocument ruleset = Load("rulesets/protect-release-tags.json");
		JsonElement root = ruleset.RootElement;

		Assert.Equal("tag", root.GetProperty("target").GetString());
		Assert.Equal("refs/tags/v*", Assert.Single(root.GetProperty("conditions").GetProperty("ref_name").GetProperty("include").EnumerateArray()).GetString());
		List<string> rules = [];
		foreach (JsonElement rule in root.GetProperty("rules").EnumerateArray())
		{
			rules.Add(rule.GetProperty("type").GetString() ?? "");
		}

		Assert.Equal(["creation", "update", "deletion"], rules);
		Assert.False(Rule(ruleset, "update").GetProperty("parameters").GetProperty("update_allows_fetch_and_merge").GetBoolean());
		JsonElement bypass = Assert.Single(root.GetProperty("bypass_actors").EnumerateArray());
		Assert.Equal("RepositoryRole", bypass.GetProperty("actor_type").GetString());
		Assert.Equal(RepositoryAdminRole, bypass.GetProperty("actor_id").GetInt32());
		Assert.Equal("always", bypass.GetProperty("bypass_mode").GetString());
	}

	[Fact]
	public void Nuget_environment_payload_disables_admin_bypass_and_self_review_prevention()
	{
		using JsonDocument environment = Load("environments/nuget.json");
		JsonElement settings = environment.RootElement.GetProperty("settings");

		Assert.Equal("nuget", environment.RootElement.GetProperty("name").GetString());
		Assert.False(settings.GetProperty("can_admins_bypass").GetBoolean());
		// Single active maintainer: the person who pushes the tag must be able to approve the publication.
		Assert.False(settings.GetProperty("prevent_self_review").GetBoolean());
		Assert.Equal(0, settings.GetProperty("wait_timer").GetInt32());
		JsonElement branchPolicy = settings.GetProperty("deployment_branch_policy");
		Assert.False(branchPolicy.GetProperty("protected_branches").GetBoolean());
		Assert.True(branchPolicy.GetProperty("custom_branch_policies").GetBoolean());
		JsonElement tagPolicy = Assert.Single(environment.RootElement.GetProperty("deployment_branch_policies").EnumerateArray());
		Assert.Equal("v*.*.*", tagPolicy.GetProperty("name").GetString());
		Assert.Equal("tag", tagPolicy.GetProperty("type").GetString());
		// Reviewer ids are resolved from logins at run time; the release workflow must still publish through this environment.
		Assert.False(settings.TryGetProperty("reviewers", out _));
		List<string> environments = [];
		foreach (KeyValuePair<string, YamlMappingNode> job in YamlDocument.Load(".github/workflows/release.yml").Jobs)
		{
			YamlNode? environmentNode = YamlDocument.Child(job.Value, "environment");
			string? name = environmentNode is YamlScalarNode scalar ? scalar.Value : YamlDocument.Scalar(environmentNode, "name");
			if (name is not null)
			{
				environments.Add(name);
			}
		}

		Assert.Contains("nuget", environments, StringComparer.Ordinal);
	}

	[Fact]
	public void Required_check_names_match_the_workflow_job_names()
	{
		// "CI / Gate" = caller job name "CI" + reusable job name "Gate"; "PR policy" = the job name of pr-policy.yml.
		foreach (string caller in (string[]) [".github/workflows/pull-request-ci.yml", ".github/workflows/main-ci.yml", ".github/workflows/release.yml"])
		{
			YamlMappingNode job = YamlDocument.Load(caller).Job("ci");
			Assert.Equal("CI", YamlDocument.Scalar(job, "name"));
			Assert.Equal("./.github/workflows/ci.yml", YamlDocument.Scalar(job, "uses"));
		}

		Assert.Equal("Gate", YamlDocument.Scalar(YamlDocument.Load(".github/workflows/ci.yml").Job("gate"), "name"));
		Assert.Equal("PR policy", YamlDocument.Scalar(YamlDocument.Load(GovernanceWorkflows.PrPolicy).Job("policy"), "name"));

		using JsonDocument ruleset = Load("rulesets/protect-main.json");
		List<string> contexts = [];
		foreach (JsonElement check in Rule(ruleset, "required_status_checks").GetProperty("parameters").GetProperty("required_status_checks").EnumerateArray())
		{
			contexts.Add(check.GetProperty("context").GetString() ?? "");
		}

		Assert.Equal(["CI / Gate", "PR policy"], contexts);
	}

	[Fact]
	public void Actions_payload_requires_sha_pinning()
	{
		using JsonDocument actions = Load("actions-permissions.json");
		Assert.True(actions.RootElement.GetProperty("enabled").GetBoolean());
		Assert.True(actions.RootElement.GetProperty("sha_pinning_required").GetBoolean());

		using JsonDocument token = Load("actions-workflow-permissions.json");
		Assert.Equal("read", token.RootElement.GetProperty("default_workflow_permissions").GetString());
		Assert.False(token.RootElement.GetProperty("can_approve_pull_request_reviews").GetBoolean());

		// SHA pinning is only safe to require because every uses: of every workflow and action is already pinned.
		List<string> unpinned = [];
		foreach (string path in AllActionFiles())
		{
			foreach (string line in RepositoryFile.ReadLines(path))
			{
				Match uses = UsesLine().Match(line);
				if (uses.Success && !uses.Groups["reference"].Value.StartsWith("./", StringComparison.Ordinal)
								 && !FullShaReference().IsMatch(uses.Groups["reference"].Value))
				{
					unpinned.Add($"{path}: {line.Trim()}");
				}
			}
		}

		Assert.True(unpinned.Count == 0, $"sha_pinning_required would reject: {string.Join("; ", unpinned)}");
	}

	[Fact]
	public void Repository_payload_allows_only_squash_merges_titled_by_the_pull_request()
	{
		using JsonDocument repository = Load("repository.json");
		JsonElement root = repository.RootElement;

		Assert.True(root.GetProperty("allow_squash_merge").GetBoolean());
		Assert.False(root.GetProperty("allow_merge_commit").GetBoolean());
		Assert.False(root.GetProperty("allow_rebase_merge").GetBoolean());
		// The subject on main is the title the PR policy check verified.
		Assert.Equal("PR_TITLE", root.GetProperty("squash_merge_commit_title").GetString());
		Assert.Equal("COMMIT_MESSAGES", root.GetProperty("squash_merge_commit_message").GetString());
		Assert.True(root.GetProperty("has_issues").GetBoolean());
		Assert.True(root.GetProperty("has_discussions").GetBoolean());
		JsonElement security = root.GetProperty("security_and_analysis");
		Assert.Equal("enabled", security.GetProperty("secret_scanning").GetProperty("status").GetString());
		Assert.Equal("enabled", security.GetProperty("secret_scanning_push_protection").GetProperty("status").GetString());
	}

	[Fact]
	public void Labels_payload_covers_every_label_the_automation_applies()
	{
		using JsonDocument labels = Load("labels.json");
		HashSet<string> defined = new(StringComparer.Ordinal);
		foreach (JsonElement label in labels.RootElement.GetProperty("labels").EnumerateArray())
		{
			Assert.Matches(Color(), label.GetProperty("color").GetString() ?? "");
			Assert.False(string.IsNullOrWhiteSpace(label.GetProperty("description").GetString()));
			Assert.True(defined.Add(label.GetProperty("name").GetString() ?? ""), "A label is listed twice.");
		}

		// GitHub drops a label that does not exist: every label an issue form, Dependabot or the health issue applies.
		List<string> applied = ["ci"];
		foreach (string file in System.IO.Directory.EnumerateFiles(RepositoryFile.FullPath(".github/ISSUE_TEMPLATE"), "*.yml"))
		{
			applied.AddRange(YamlDocument.Scalars(YamlDocument.Load(".github/ISSUE_TEMPLATE/" + Path.GetFileName(file)).Root, "labels"));
		}

		foreach (YamlMappingNode update in YamlDocument.Mappings(YamlDocument.Load(".github/dependabot.yml").Root, "updates"))
		{
			applied.AddRange(YamlDocument.Scalars(update, "labels"));
		}

		Assert.Contains("[string] $Label = 'ci'", RepositoryFile.ReadText("eng/ci/health/Publish-HealthIssue.ps1"), StringComparison.Ordinal);
		foreach (string label in applied)
		{
			Assert.True(defined.Contains(label), $"The label '{label}' is applied by the repository's automation but missing from {Directory}/labels.json.");
		}
	}

	private static JsonDocument Load(string file)
	{
		return JsonDocument.Parse(RepositoryFile.ReadText($"{Directory}/{file}"));
	}

	private static JsonElement Rule(JsonDocument ruleset, string type)
	{
		List<JsonElement> matches = [];
		foreach (JsonElement rule in ruleset.RootElement.GetProperty("rules").EnumerateArray())
		{
			if (string.Equals(rule.GetProperty("type").GetString(), type, StringComparison.Ordinal))
			{
				matches.Add(rule);
			}
		}

		return Assert.Single(matches);
	}

	private static List<string> AllActionFiles()
	{
		List<string> files = GovernanceWorkflows.AllWorkflowPaths();
		foreach (string action in System.IO.Directory.EnumerateFiles(RepositoryFile.FullPath(".github/actions"), "action.yml", SearchOption.AllDirectories))
		{
			files.Add(Path.GetRelativePath(RepositoryFile.FullPath(""), action).Replace('\\', '/'));
		}

		return files;
	}

	[GeneratedRegex(@"^\s*(-\s+)?uses:\s+(?<reference>\S+)", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
	private static partial Regex UsesLine();

	[GeneratedRegex(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_./-]+@[0-9a-f]{40}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex FullShaReference();

	[GeneratedRegex("^[0-9a-f]{6}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex Color();
}
