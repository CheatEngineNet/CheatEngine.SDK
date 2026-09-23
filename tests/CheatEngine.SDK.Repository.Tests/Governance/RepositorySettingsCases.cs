using System.Text.Json;
using System.Text.Json.Nodes;

namespace CheatEngine.SDK.Repository.Tests.Governance;

/// <summary>
///     Vectors for <c>eng/github/RepositorySettings.psm1</c> and GitHub API responses for the end-to-end runs of
///     <c>Set-RepositorySettings.ps1</c>: the live state read on 2026-09-23 (before any application) and the state the
///     payloads describe (after one).
/// </summary>
internal static class RepositorySettingsCases
{
	public const string Repository = "CheatEngineNet/CheatEngine.SDK";
	public const string Base = "repos/" + Repository;
	public const long ReviewerId = 424242;
	public const long MainRulesetId = 23712947;
	public const long TagRulesetId = 99;

	/// <summary>The module vectors.</summary>
	public static List<PwshCall> Calls()
	{
		string payloads = RepositoryFile.FullPath("eng/github");
		return
		[
			Compare("same", """{ "a": 1, "b": { "c": "x" } }""", """{ "a": 1, "b": { "c": "x", "d": 2 }, "e": 3 }"""),
			Compare("scalar_differs", """{ "t": "PR_TITLE" }""", """{ "t": "COMMIT_OR_PR_TITLE" }"""),
			Compare("false_is_not_zero", """{ "a": false }""", """{ "a": 0 }"""),
			Compare("sets_ignore_order", """{ "m": [ "squash", "merge" ] }""", """{ "m": [ "merge", "squash" ] }"""),
			Compare("sets_differ", """{ "m": [ "squash" ] }""", """{ "m": [ "merge", "squash", "rebase" ] }"""),
			Compare("rule_missing",
				"""{ "rules": [ { "type": "deletion" }, { "type": "required_status_checks", "parameters": { "strict_required_status_checks_policy": false } } ] }""",
				"""{ "rules": [ { "type": "deletion" } ] }"""),
			Compare("rule_extra", """{ "rules": [ { "type": "deletion" } ] }""",
				"""{ "rules": [ { "type": "deletion" }, { "type": "non_fast_forward" } ] }"""),
			Compare("nested_rule_parameter",
				"""{ "rules": [ { "type": "pull_request", "parameters": { "allowed_merge_methods": [ "squash" ] } } ] }""",
				"""{ "rules": [ { "type": "pull_request", "parameters": { "allowed_merge_methods": [ "merge", "squash", "rebase" ], "x": 1 } } ] }"""),
			Compare("status_checks_by_context",
				"""{ "c": [ { "context": "CI / Gate", "integration_id": 15368 }, { "context": "PR policy", "integration_id": 15368 } ] }""",
				"""{ "c": [ { "context": "CI / Gate", "integration_id": 15368 } ] }"""),
			Compare("reviewers_by_type_and_id", """{ "reviewers": [ { "type": "User", "id": 1 } ] }""",
				"""{ "reviewers": [ { "type": "User", "id": 2 } ] }"""),
			Compare("bypass_by_actor", """{ "b": [ { "actor_type": "RepositoryRole", "actor_id": 5, "bypass_mode": "always" } ] }""",
				"""{ "b": [ { "actor_type": "RepositoryRole", "actor_id": 5, "bypass_mode": "pull_request" } ] }"""),
			Compare("object_absent", """{ "a": { "b": 1 } }""", "{}"),
			new PwshCall("unmanaged_nested", "Get-UnmanagedSetting", Arguments(
				("Desired", Json("""{ "rules": [ { "type": "pull_request", "parameters": { "a": 1 } } ] }""")),
				("Actual", Json("""
					{ "id": 1, "node_id": "x", "enforcement": "active",
					  "rules": [ { "type": "pull_request", "parameters": { "a": 1, "require_extra_approval_for_unattributed_changes": true } } ] }
					""")))),
			new PwshCall("environment_without_reviewers", "ConvertFrom-EnvironmentResponse",
				Arguments(("Response", Json(LiveEnvironment)))),
			new PwshCall("environment_with_reviewers", "ConvertFrom-EnvironmentResponse", Arguments(("Response", Json($$"""
				{ "can_admins_bypass": false, "deployment_branch_policy": { "protected_branches": false, "custom_branch_policies": true },
				  "protection_rules": [
				    { "id": 1, "type": "wait_timer", "wait_timer": 5 },
				    { "id": 2, "type": "required_reviewers", "prevent_self_review": true,
				      "reviewers": [ { "type": "User", "reviewer": { "login": "AriusII", "id": {{ReviewerId}} } } ] },
				    { "id": 3, "type": "branch_policy" } ] }
				""")))),
			new PwshCall("plan_default", "Get-RepositorySettingsPlan", Arguments(("PayloadDirectory", payloads), ("Repository", Repository))),
			new PwshCall("plan_skip_required_checks", "Get-RepositorySettingsPlan",
				Arguments(("PayloadDirectory", payloads), ("Repository", Repository), ("SkipRequiredChecks", true))),
			new PwshCall("plan_immutable_releases", "Get-RepositorySettingsPlan",
				Arguments(("PayloadDirectory", payloads), ("Repository", Repository), ("EnableImmutableReleases", true)))
		];
	}

	/// <summary>The nuget environment as GitHub returned it on 2026-09-23: a tag policy, admin bypass, no reviewer.</summary>
	public const string LiveEnvironment = """
		{ "id": 22327872460, "name": "nuget", "can_admins_bypass": true,
		  "protection_rules": [ { "id": 66141268, "type": "branch_policy" } ],
		  "deployment_branch_policy": { "protected_branches": false, "custom_branch_policies": true } }
		""";

	/// <summary>The GitHub responses before any application of the payloads (values read on 2026-09-23).</summary>
	public static Dictionary<string, string> LiveResponses()
	{
		Dictionary<string, string> responses = CommonResponses();
		responses[Base] = """
			{ "full_name": "CheatEngineNet/CheatEngine.SDK", "permissions": { "admin": true },
			  "allow_squash_merge": true, "allow_merge_commit": true, "allow_rebase_merge": true,
			  "squash_merge_commit_title": "COMMIT_OR_PR_TITLE", "squash_merge_commit_message": "COMMIT_MESSAGES",
			  "delete_branch_on_merge": true, "has_issues": true, "has_discussions": true,
			  "security_and_analysis": { "secret_scanning": { "status": "enabled" }, "secret_scanning_push_protection": { "status": "enabled" },
			                             "dependabot_security_updates": { "status": "enabled" } } }
			""";
		responses[$"{Base}/rulesets"] = $$"""[ { "id": {{MainRulesetId}}, "name": "Protect main", "target": "branch", "enforcement": "active" } ]""";
		responses[$"{Base}/rulesets/{MainRulesetId}"] = $$"""
			{ "id": {{MainRulesetId}}, "name": "Protect main", "target": "branch", "source_type": "Repository", "enforcement": "active",
			  "conditions": { "ref_name": { "exclude": [], "include": [ "~DEFAULT_BRANCH" ] } },
			  "rules": [ { "type": "deletion" }, { "type": "non_fast_forward" },
			    { "type": "pull_request", "parameters": { "required_approving_review_count": 0, "dismiss_stale_reviews_on_push": true,
			      "required_reviewers": [], "require_code_owner_review": false, "dismissal_restriction": { "enabled": false, "allowed_actors": [] },
			      "require_last_push_approval": false, "required_review_thread_resolution": false,
			      "require_extra_approval_for_unattributed_changes": true, "allowed_merge_methods": [ "merge", "squash", "rebase" ] } } ],
			  "bypass_actors": [], "current_user_can_bypass": "never" }
			""";
		responses[$"{Base}/environments/nuget"] = LiveEnvironment;
		responses[$"{Base}/actions/permissions"] = """{ "enabled": true, "allowed_actions": "all", "sha_pinning_required": false }""";
		return responses;
	}

	/// <summary>The GitHub responses once the payloads are applied (with the extra fields GitHub adds).</summary>
	public static Dictionary<string, string> AppliedResponses()
	{
		Dictionary<string, string> responses = CommonResponses();
		JsonObject repository = Payload("repository.json");
		repository["full_name"] = Repository;
		repository["permissions"] = new JsonObject { ["admin"] = true };
		responses[Base] = repository.ToJsonString();

		JsonArray labels = Payload("labels.json")["labels"]!.AsArray();
		foreach (JsonNode? label in labels)
		{
			responses[$"{Base}/labels/{Uri.EscapeDataString((string) label!["name"]!)}"] = label.ToJsonString();
		}

		responses[$"{Base}/rulesets"] = $$"""
			[ { "id": {{MainRulesetId}}, "name": "Protect main", "target": "branch" },
			  { "id": {{TagRulesetId}}, "name": "Protect release tags", "target": "tag" } ]
			""";
		JsonObject main = Payload("rulesets/protect-main.json");
		main["id"] = MainRulesetId;
		// GitHub adds parameters the payload does not manage; they must not count as differences.
		foreach (JsonNode? rule in main["rules"]!.AsArray())
		{
			if (string.Equals((string?) rule!["type"], "pull_request", StringComparison.Ordinal))
			{
				rule["parameters"]!["require_extra_approval_for_unattributed_changes"] = true;
				rule["parameters"]!["required_reviewers"] = new JsonArray();
			}
		}

		responses[$"{Base}/rulesets/{MainRulesetId}"] = main.ToJsonString();
		JsonObject tags = Payload("rulesets/protect-release-tags.json");
		tags["id"] = TagRulesetId;
		responses[$"{Base}/rulesets/{TagRulesetId}"] = tags.ToJsonString();

		responses[$"{Base}/environments/nuget"] = $$"""
			{ "name": "nuget", "can_admins_bypass": false,
			  "deployment_branch_policy": { "protected_branches": false, "custom_branch_policies": true },
			  "protection_rules": [
			    { "id": 1, "type": "required_reviewers", "prevent_self_review": false,
			      "reviewers": [ { "type": "User", "reviewer": { "login": "AriusII", "id": {{ReviewerId}} } } ] },
			    { "id": 2, "type": "branch_policy" } ] }
			""";
		responses[$"{Base}/actions/permissions"] = Payload("actions-permissions.json").ToJsonString();
		return responses;
	}

	private static Dictionary<string, string> CommonResponses()
	{
		return new Dictionary<string, string>(StringComparer.Ordinal)
		{
			[$"users/AriusII"] = $$"""{ "login": "AriusII", "id": {{ReviewerId}} }""",
			// Dependabot alerts answer 204 No Content when they are enabled.
			[$"{Base}/vulnerability-alerts"] = "",
			[$"{Base}/automated-security-fixes"] = """{ "enabled": true, "paused": false }""",
			[$"{Base}/private-vulnerability-reporting"] = """{ "enabled": true }""",
			[$"{Base}/labels/bug"] = """{ "id": 1, "name": "bug", "color": "d73a4a", "description": "Something isn't working", "default": true }""",
			[$"{Base}/labels/ci"] = """{ "id": 2, "name": "ci", "color": "fbca04", "description": "Build, release, and workflow automation" }""",
			[$"{Base}/labels/dependencies"] = """{ "id": 3, "name": "dependencies", "color": "0366d6", "description": "Pull requests that update a dependency file" }""",
			[$"{Base}/labels/.NET"] = """{ "id": 4, "name": ".NET", "color": "7121c6", "description": "Pull requests that update .NET code" }""",
			[$"{Base}/environments/nuget/deployment-branch-policies"] =
				"""{ "total_count": 1, "branch_policies": [ { "id": 60482410, "name": "v*.*.*", "type": "tag" } ] }""",
			[$"{Base}/actions/permissions/workflow"] = """{ "default_workflow_permissions": "read", "can_approve_pull_request_reviews": false }""",
			[$"{Base}/code-scanning/default-setup"] = """{ "state": "not-configured", "languages": [ "actions", "c-cpp", "csharp", "python" ] }"""
		};
	}

	private static JsonObject Payload(string file)
	{
		JsonObject payload = Assert.IsType<JsonObject>(JsonNode.Parse(RepositoryFile.ReadText($"eng/github/{file}")));
		payload.Remove("_comment");
		return payload;
	}

	private static PwshCall Compare(string name, string desired, string actual)
	{
		return new PwshCall(name, "Compare-SettingsObject", Arguments(("Desired", Json(desired)), ("Actual", Json(actual))));
	}

	private static JsonElement Json(string text)
	{
		using JsonDocument document = JsonDocument.Parse(text);
		return document.RootElement.Clone();
	}

	private static Dictionary<string, object?> Arguments(params (string Name, object? Value)[] arguments)
	{
		Dictionary<string, object?> values = new(StringComparer.Ordinal);
		foreach ((string name, object? value) in arguments)
		{
			values[name] = value;
		}

		return values;
	}
}
