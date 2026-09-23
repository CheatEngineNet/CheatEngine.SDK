using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

using YamlDotNet.RepresentationModel;

namespace CheatEngine.SDK.Repository.Tests.Governance;

/// <summary>
///     The advisory governance workflows outside <c>CI / Gate</c> (CodeQL, Scorecard, online zizmor, dependency
///     submission, scheduled health) and the PR policy: repository conventions (shared-contracts §1.6, §1.13) and the
///     choices that keep each one safe and useful (audit register PR-CQ-19, -22, -24, -30, -45, -55).
/// </summary>
public sealed partial class GovernanceWorkflowTests
{
	private const string ZizmorVersion = "1.30.1";

	private static readonly string[] s_runnerLabels = ["windows-2025", "ubuntu-24.04"];

	/// <summary>Artifact names the governance workflows may upload (requested for shared-contracts §1.9).</summary>
	private static readonly string[] s_governanceArtifacts =
		["binlogs-codeql", "dependency-snapshot", "health-sdk-canary", "health-bridge-drift", "health-test-repeat"];

	[Fact]
	public void Governance_workflows_pin_every_action_by_full_sha_with_a_version_comment()
	{
		List<string> offenders = [];
		foreach (string path in GovernanceWorkflows.Existing())
		{
			string[] lines = RepositoryFile.ReadLines(path);
			for (int i = 0; i < lines.Length; i++)
			{
				Match uses = UsesLine().Match(lines[i]);
				if (uses.Success && !uses.Groups["reference"].Value.StartsWith("./", StringComparison.Ordinal)
								 && !(PinnedReference().IsMatch(uses.Groups["reference"].Value)
									  && VersionComment().IsMatch(uses.Groups["comment"].Value)))
				{
					offenders.Add($"{path}:{i + 1}: {lines[i].Trim()}");
				}
			}
		}

		Assert.True(offenders.Count == 0,
			$"Pin every action as owner/repo@<40-hex> # vX.Y.Z (shared-contracts 1.13): {string.Join("; ", offenders)}");
	}

	[Fact]
	public void Governance_jobs_use_literal_runner_labels_and_timeouts()
	{
		foreach (string path in GovernanceWorkflows.Existing())
		{
			foreach (KeyValuePair<string, YamlMappingNode> job in YamlDocument.Load(path).Jobs)
			{
				string? runner = YamlDocument.Scalar(job.Value, "runs-on");
				Assert.True(runner is not null && Array.IndexOf(s_runnerLabels, runner) >= 0,
					$"{path} job {job.Key}: runs-on '{runner}' must be a literal windows-2025 or ubuntu-24.04 label.");
				Assert.True(int.TryParse(YamlDocument.Scalar(job.Value, "timeout-minutes"), NumberStyles.None, CultureInfo.InvariantCulture,
						out int minutes) && minutes > 0,
					$"{path} job {job.Key} needs timeout-minutes.");
			}
		}
	}

	[Fact]
	public void Governance_workflows_start_read_only_and_comment_every_job_elevation()
	{
		foreach (string path in GovernanceWorkflows.Existing())
		{
			YamlDocument workflow = YamlDocument.Load(path);
			Assert.Equal(new Dictionary<string, string>(StringComparer.Ordinal) { ["contents"] = "read" },
				YamlDocument.Permissions(workflow.Root));
			string[] lines = RepositoryFile.ReadLines(path);
			foreach (KeyValuePair<string, YamlMappingNode> job in workflow.Jobs)
			{
				if (YamlDocument.Child(job.Value, "permissions") is not YamlMappingNode permissions)
				{
					continue;
				}

				foreach (KeyValuePair<YamlNode, YamlNode> scope in permissions.Children)
				{
					string name = ((YamlScalarNode) scope.Key).Value ?? "";
					string access = ((YamlScalarNode) scope.Value).Value ?? "";
					if (string.Equals(name, "contents", StringComparison.Ordinal)
						&& string.Equals(access, "read", StringComparison.Ordinal))
					{
						continue;
					}

					string line = lines[(int) scope.Key.Start.Line - 1];
					Assert.True(line.Contains(" # ", StringComparison.Ordinal),
						$"{path} job {job.Key}: '{name}: {access}' needs a trailing comment giving the reason (.coderabbit.yaml workflow hygiene).");
				}
			}
		}
	}

	[Fact]
	public void Governance_checkouts_never_persist_credentials()
	{
		foreach (string path in GovernanceWorkflows.Existing())
		{
			foreach (KeyValuePair<string, YamlMappingNode> job in YamlDocument.Load(path).Jobs)
			{
				foreach (YamlMappingNode step in YamlDocument.Steps(job.Value))
				{
					if (YamlDocument.UsesAction(step, "actions/checkout"))
					{
						Assert.True(string.Equals(YamlDocument.Scalar(YamlDocument.Child(step, "with"), "persist-credentials"), "false",
								StringComparison.Ordinal),
							$"{path} job {job.Key}: actions/checkout must set persist-credentials: false.");
					}
				}
			}
		}
	}

	[Fact]
	public void Governance_multi_line_scripts_stop_on_the_first_error()
	{
		// shared.md 7: a multi-line pwsh step starts with $ErrorActionPreference = 'Stop', so a failing cmdlet stops the
		// step in the script itself rather than relying on the wrapper GitHub prepends today.
		List<string> offenders = [];
		foreach (string path in GovernanceWorkflows.Existing())
		{
			foreach (KeyValuePair<string, YamlMappingNode> job in YamlDocument.Load(path).Jobs)
			{
				foreach (YamlMappingNode step in YamlDocument.Steps(job.Value))
				{
					string[] lines = (YamlDocument.Scalar(step, "run") ?? "").ReplaceLineEndings("\n").Trim().Split('\n');
					if (lines.Length > 1 && !string.Equals(lines[0].Trim(), "$ErrorActionPreference = 'Stop'", StringComparison.Ordinal))
					{
						offenders.Add($"{path} job {job.Key} step '{YamlDocument.Scalar(step, "name")}'");
					}
				}
			}
		}

		Assert.True(offenders.Count == 0,
			$"Start every multi-line run script with $ErrorActionPreference = 'Stop': {string.Join("; ", offenders)}");
	}

	[Fact]
	public void Governance_scripts_check_the_exit_code_of_every_native_command()
	{
		// A failing native command (or repository script) in the middle of a multi-line pwsh step does not stop the step:
		// every one is followed by an explicit $LASTEXITCODE check (shared.md 7, the same rule as WorkflowContractTests).
		List<string> offenders = [];
		foreach (string path in GovernanceWorkflows.Existing())
		{
			foreach (KeyValuePair<string, YamlMappingNode> job in YamlDocument.Load(path).Jobs)
			{
				foreach (YamlMappingNode step in YamlDocument.Steps(job.Value))
				{
					string[] lines = (YamlDocument.Scalar(step, "run") ?? "").ReplaceLineEndings("\n").Split('\n');
					for (int i = 0; i < lines.Length; i++)
					{
						if (!NativeInvocation().IsMatch(lines[i]))
						{
							continue;
						}

						int next = i + 1;
						while (next < lines.Length && string.IsNullOrWhiteSpace(lines[next]))
						{
							next++;
						}

						if (next >= lines.Length || !lines[next].TrimStart().StartsWith("if ($LASTEXITCODE -ne 0)", StringComparison.Ordinal))
						{
							offenders.Add($"{path} job {job.Key}: '{lines[i].Trim()}'");
						}
					}
				}
			}
		}

		Assert.True(offenders.Count == 0,
			$"Follow every native command with if ($LASTEXITCODE -ne 0) {{ throw ... }}: {string.Join("; ", offenders)}");
	}

	[Fact]
	public void Advisory_workflows_never_use_pull_request_target_or_merge_group()
	{
		foreach (string path in GovernanceWorkflows.Existing())
		{
			IReadOnlyList<string> triggers = YamlDocument.Load(path).Triggers;
			Assert.DoesNotContain("pull_request_target", triggers, StringComparer.Ordinal);
			Assert.DoesNotContain("merge_group", triggers, StringComparer.Ordinal);
			Assert.DoesNotContain("workflow_run", triggers, StringComparer.Ordinal);
		}
	}

	[Fact]
	public void Governance_workflows_never_enable_a_package_cache()
	{
		foreach (string path in GovernanceWorkflows.Existing())
		{
			foreach (KeyValuePair<string, YamlMappingNode> job in YamlDocument.Load(path).Jobs)
			{
				foreach (YamlMappingNode step in YamlDocument.Steps(job.Value))
				{
					string? uses = YamlDocument.Uses(step);
					Assert.False(uses is not null && uses.StartsWith("actions/cache", StringComparison.Ordinal),
						$"{path} job {job.Key} uses actions/cache.");
					YamlNode? with = YamlDocument.Child(step, "with");
					foreach (string key in (string[]) ["cache", "dependency-caching", "trap-caching"])
					{
						Assert.False(string.Equals(YamlDocument.Scalar(with, key), "true", StringComparison.Ordinal),
							$"{path} job {job.Key} enables '{key}': no cache on a path reachable by codeql or the audits.");
					}
				}
			}
		}
	}

	[Fact]
	public void Governance_workflows_upload_only_their_reserved_artifact_names()
	{
		foreach (string path in GovernanceWorkflows.Existing())
		{
			foreach (KeyValuePair<string, YamlMappingNode> job in YamlDocument.Load(path).Jobs)
			{
				foreach (YamlMappingNode step in YamlDocument.Steps(job.Value))
				{
					if (YamlDocument.UsesAction(step, "actions/upload-artifact"))
					{
						string? name = YamlDocument.Scalar(YamlDocument.Child(step, "with"), "name");
						Assert.True(name is not null && Array.IndexOf(s_governanceArtifacts, name) >= 0,
							$"{path} job {job.Key} uploads '{name}', which is not a reserved artifact name.");
					}
				}
			}
		}
	}

	[Fact]
	public void Codeql_analyzes_csharp_cpp_and_actions_with_literal_runner_labels()
	{
		YamlDocument workflow = YamlDocument.Load(GovernanceWorkflows.CodeQl);

		Dictionary<string, (string Language, string BuildMode, string Runner)> expected = new(StringComparer.Ordinal)
		{
			["csharp"] = ("csharp", "manual", "windows-2025"),
			["cpp"] = ("c-cpp", "none", "windows-2025"),
			["actions"] = ("actions", "none", "ubuntu-24.04")
		};
		Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), YamlDocument.KeysOf(YamlDocument.Child(workflow.Root, "jobs")).Order(StringComparer.Ordinal));
		foreach ((string id, (string language, string buildMode, string runner)) in expected)
		{
			YamlMappingNode job = workflow.Job(id);
			Assert.Equal(runner, YamlDocument.Scalar(job, "runs-on"));
			Assert.Equal($"Analyze ({language})", YamlDocument.Scalar(job, "name"));
			IReadOnlyDictionary<string, string>? permissions = YamlDocument.Permissions(job);
			Assert.NotNull(permissions);
			Assert.Equal("write", permissions["security-events"]);

			YamlNode? init = WithOf(job, "github/codeql-action/init");
			Assert.Equal(language, YamlDocument.Scalar(init, "languages"));
			Assert.Equal(buildMode, YamlDocument.Scalar(init, "build-mode"));
			Assert.Equal("security-extended", YamlDocument.Scalar(init, "queries"));
			Assert.Equal($"/language:{language}", YamlDocument.Scalar(WithOf(job, "github/codeql-action/analyze"), "category"));
		}

		// Python is not analysed: the repository has no Python source left, and a language without sources fails.
		Assert.DoesNotContain("python", workflow.Text, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void Codeql_csharp_job_builds_the_product_graph_manually_without_shared_compilation()
	{
		YamlMappingNode job = YamlDocument.Load(GovernanceWorkflows.CodeQl).Job("csharp");
		IReadOnlyList<YamlMappingNode> steps = YamlDocument.Steps(job);

		YamlMappingNode checkout = Assert.Single(steps, static step => YamlDocument.UsesAction(step, "actions/checkout"));
		Assert.Equal("0", YamlDocument.Scalar(YamlDocument.Child(checkout, "with"), "fetch-depth"));
		YamlMappingNode setup = Assert.Single(steps, static step =>
			string.Equals(YamlDocument.Uses(step), "./.github/actions/setup-dotnet", StringComparison.Ordinal));
		Assert.Equal("src/CheatEngine.SDK/CheatEngine.SDK.csproj", YamlDocument.Scalar(YamlDocument.Child(setup, "with"), "restore"));

		YamlMappingNode build = Assert.Single(steps, static step => YamlDocument.Scalar(step, "run") is not null);
		string run = YamlDocument.NormalizeWhitespace(YamlDocument.Scalar(build, "run")!);
		foreach (string fragment in (string[])
				 [
					 "dotnet build src/CheatEngine.SDK/CheatEngine.SDK.csproj", "-c Release", "--no-restore", "--no-incremental",
					 "--disable-build-servers", "-p:UseSharedCompilation=false", "$LASTEXITCODE"
				 ])
		{
			Assert.Contains(fragment, run, StringComparison.Ordinal);
		}

		// The traced build runs between init and analyze.
		int init = IndexOf(steps, "github/codeql-action/init");
		int analyze = IndexOf(steps, "github/codeql-action/analyze");
		int buildIndex = -1;
		for (int i = 0; i < steps.Count; i++)
		{
			if (ReferenceEquals(steps[i], build))
			{
				buildIndex = i;
			}
		}

		Assert.True(IndexOf(steps, "./.github/actions/setup-dotnet") < init && init < buildIndex && buildIndex < analyze,
			"The CodeQL C# job must restore, then init, then build, then analyze.");
	}

	[Fact]
	public void Codeql_workflow_never_enables_a_package_cache()
	{
		YamlDocument workflow = YamlDocument.Load(GovernanceWorkflows.CodeQl);
		Assert.DoesNotContain("actions/cache", workflow.Text, StringComparison.Ordinal);
		foreach (KeyValuePair<string, YamlMappingNode> job in workflow.Jobs)
		{
			YamlNode? init = WithOf(job.Value, "github/codeql-action/init");
			Assert.Equal("false", YamlDocument.Scalar(init, "dependency-caching"));
			Assert.Equal("false", YamlDocument.Scalar(init, "trap-caching"));
			foreach (YamlMappingNode step in YamlDocument.Steps(job.Value))
			{
				Assert.Null(YamlDocument.Child(YamlDocument.Child(step, "with"), "cache"));
			}
		}
	}

	[Fact]
	public void Codeql_runs_on_pull_requests_main_a_weekly_schedule_and_dispatch()
	{
		// A new workflow cannot be dispatched before it is on main: the pull_request trigger validates it on the vehicle
		// pull request first (audit register PR-SEQ-15).
		YamlDocument workflow = YamlDocument.Load(GovernanceWorkflows.CodeQl);
		Assert.Equal(["push", "pull_request", "schedule", "workflow_dispatch"], workflow.Triggers);
		Assert.Equal(["main"], YamlDocument.Scalars(workflow.Trigger("push"), "branches"));
		foreach (KeyValuePair<string, YamlMappingNode> job in workflow.Jobs)
		{
			Assert.Equal("${{ github.event_name != 'pull_request' || !github.event.pull_request.draft }}",
				YamlDocument.Scalar(job.Value, "if"));
		}
	}

	[Fact]
	public void Scorecard_workflow_has_no_defaults_env_or_run_steps()
	{
		// The Scorecard API refuses to publish results of a workflow that breaks these rules, silently for the repository.
		YamlDocument workflow = YamlDocument.Load(GovernanceWorkflows.Scorecard);
		Assert.Null(YamlDocument.Child(workflow.Root, "defaults"));
		Assert.Null(YamlDocument.Child(workflow.Root, "env"));

		KeyValuePair<string, YamlMappingNode> job = Assert.Single(workflow.Jobs);
		Assert.Equal("analysis", job.Key);
		foreach (string key in (string[]) ["defaults", "env", "container", "services"])
		{
			Assert.True(YamlDocument.Child(job.Value, key) is null, $"The Scorecard job must not set '{key}'.");
		}

		foreach (YamlMappingNode step in YamlDocument.Steps(job.Value))
		{
			Assert.Null(YamlDocument.Child(step, "run"));
			Assert.NotNull(YamlDocument.Uses(step));
		}

		Assert.Equal("ubuntu-24.04", YamlDocument.Scalar(job.Value, "runs-on"));
		Assert.Equal(["branch_protection_rule", "push", "schedule"], workflow.Triggers);
		Assert.Equal(["main"], YamlDocument.Scalars(workflow.Trigger("push"), "branches"));
		Assert.Equal("true", YamlDocument.Scalar(WithOf(job.Value, "ossf/scorecard-action"), "publish_results"));
	}

	[Fact]
	public void Scorecard_steps_use_only_the_actions_the_verifier_allows()
	{
		string[] allowed =
		[
			"actions/checkout", "actions/create-github-app-token", "ossf/scorecard-action", "actions/upload-artifact",
			"github/codeql-action/upload-sarif", "step-security/harden-runner"
		];
		foreach (YamlMappingNode step in YamlDocument.Steps(YamlDocument.Load(GovernanceWorkflows.Scorecard).Job("analysis")))
		{
			string uses = YamlDocument.Uses(step) ?? "";
			string action = uses.Split('@')[0];
			Assert.True(Array.IndexOf(allowed, action) >= 0, $"The Scorecard verifier rejects the step '{uses}'.");
		}
	}

	[Fact]
	public void Only_the_scorecard_job_requests_an_id_token()
	{
		// An OIDC token is a publication credential: the Scorecard upload among the governance workflows, and the release
		// chain (NuGet trusted publishing, attestations) among the others. Never at workflow level.
		List<string> holders = [];
		foreach (string path in GovernanceWorkflows.AllWorkflowPaths())
		{
			YamlDocument workflow = YamlDocument.Load(path);
			Assert.False(YamlDocument.Permissions(workflow.Root)?.ContainsKey("id-token") ?? false,
				$"{path} requests id-token at workflow level.");
			foreach (KeyValuePair<string, YamlMappingNode> job in workflow.Jobs)
			{
				if (YamlDocument.Permissions(job.Value)?.ContainsKey("id-token") ?? false)
				{
					holders.Add($"{path}#{job.Key}");
				}
			}
		}

		foreach (string holder in holders)
		{
			Assert.True(string.Equals(holder, GovernanceWorkflows.Scorecard + "#analysis", StringComparison.Ordinal)
						|| holder.StartsWith(".github/workflows/release.yml#", StringComparison.Ordinal),
				$"{holder} requests an id-token; only the Scorecard analysis job and the release workflow may.");
		}

		Assert.Contains(GovernanceWorkflows.Scorecard + "#analysis", holders, StringComparer.Ordinal);
	}

	[Fact]
	public void Zizmor_online_pins_the_tool_version_and_enables_online_audits()
	{
		YamlDocument workflow = YamlDocument.Load(GovernanceWorkflows.ZizmorOnline);
		YamlMappingNode job = workflow.Job("zizmor");

		YamlNode? with = WithOf(job, "zizmorcore/zizmor-action");
		Assert.Equal(ZizmorVersion, YamlDocument.Scalar(with, "version"));
		Assert.Equal("true", YamlDocument.Scalar(with, "online-audits"));
		Assert.Equal("true", YamlDocument.Scalar(with, "advanced-security"));
		Assert.Equal("regular", YamlDocument.Scalar(with, "persona"));
		// zizmor discovers .github/zizmor.yml itself; an explicit path would break before that file exists.
		Assert.Null(YamlDocument.Child(with, "config"));
		Assert.Equal("write", YamlDocument.Permissions(job)!["security-events"]);

		// Fork pull requests cannot upload SARIF, and drafts wait.
		string condition = YamlDocument.NormalizeWhitespace(YamlDocument.Scalar(job, "if") ?? "");
		Assert.Contains("github.event.pull_request.head.repo.full_name == github.repository", condition, StringComparison.Ordinal);
		Assert.Contains("!github.event.pull_request.draft", condition, StringComparison.Ordinal);
		Assert.Equal(["push", "pull_request", "schedule", "workflow_dispatch"], workflow.Triggers);
	}

	[Fact]
	public void Online_and_gate_zizmor_runs_pin_the_same_version()
	{
		// The blocking offline run of ci.yml and this advisory run must judge the same rules.
		foreach (string path in GovernanceWorkflows.AllWorkflowPaths())
		{
			foreach (KeyValuePair<string, YamlMappingNode> job in YamlDocument.Load(path).Jobs)
			{
				foreach (YamlMappingNode step in YamlDocument.Steps(job.Value))
				{
					if (YamlDocument.UsesAction(step, "zizmorcore/zizmor-action"))
					{
						Assert.True(string.Equals(YamlDocument.Scalar(YamlDocument.Child(step, "with"), "version"), ZizmorVersion,
								StringComparison.Ordinal),
							$"{path} job {job.Key} must pin zizmor {ZizmorVersion} like {GovernanceWorkflows.ZizmorOnline}.");
					}
				}
			}
		}
	}

	[Fact]
	public void Dependency_submission_runs_on_main_dispatch_and_same_repository_pull_requests_only()
	{
		YamlDocument workflow = YamlDocument.Load(GovernanceWorkflows.DependencySubmission);

		Assert.Equal(["push", "pull_request", "workflow_dispatch"], workflow.Triggers);
		Assert.Equal(["main"], YamlDocument.Scalars(workflow.Trigger("push"), "branches"));
		string condition = YamlDocument.NormalizeWhitespace(YamlDocument.Scalar(workflow.Job("detect"), "if") ?? "");
		Assert.Equal(
			"github.event_name != 'pull_request' || (!github.event.pull_request.draft && github.event.pull_request.head.repo.full_name == github.repository)",
			condition);
		// submit only follows a successful detect, so it inherits the fork and draft exclusion.
		Assert.Equal(["detect"], YamlDocument.Scalars(workflow.Job("submit"), "needs"));
		Assert.Null(YamlDocument.Child(workflow.Job("submit"), "if"));
	}

	[Fact]
	public void Only_the_dependency_submit_job_holds_contents_write()
	{
		List<string> writers = [];
		foreach (string path in GovernanceWorkflows.Existing())
		{
			foreach (KeyValuePair<string, YamlMappingNode> job in YamlDocument.Load(path).Jobs)
			{
				if (YamlDocument.Permissions(job.Value) is { } permissions
					&& permissions.TryGetValue("contents", out string? access)
					&& string.Equals(access, "write", StringComparison.Ordinal))
				{
					writers.Add($"{path}#{job.Key}");
				}
			}
		}

		Assert.Equal([GovernanceWorkflows.DependencySubmission + "#submit"], writers);
		Assert.Null(YamlDocument.Permissions(YamlDocument.Load(GovernanceWorkflows.DependencySubmission).Job("detect")));
	}

	[Fact]
	public void Dependency_submit_job_runs_no_third_party_code()
	{
		YamlDocument workflow = YamlDocument.Load(GovernanceWorkflows.DependencySubmission);
		IReadOnlyList<YamlMappingNode> steps = YamlDocument.Steps(workflow.Job("submit"));

		Assert.Equal(2, steps.Count);
		Assert.True(YamlDocument.UsesAction(steps[0], "actions/download-artifact"), "submit starts by downloading the snapshot.");
		Assert.Equal("dependency-snapshot", YamlDocument.Scalar(YamlDocument.Child(steps[0], "with"), "name"));
		Assert.Null(YamlDocument.Uses(steps[1]));
		string run = YamlDocument.Scalar(steps[1], "run") ?? "";
		Assert.Contains("gh api --method POST", run, StringComparison.Ordinal);

		// Detection and submission name the same commit, ref and correlator.
		YamlMappingNode detect = Assert.Single(YamlDocument.Steps(workflow.Job("detect")),
			static step => YamlDocument.Child(step, "env") is not null);
		foreach (string variable in (string[]) ["SNAPSHOT_SHA", "SNAPSHOT_REF", "SNAPSHOT_CORRELATOR"])
		{
			Assert.Equal(YamlDocument.Scalar(YamlDocument.Child(detect, "env"), variable),
				YamlDocument.Scalar(YamlDocument.Child(steps[1], "env"), variable));
		}

		// The official action fetches the latest Component Detection at run time next to the write token.
		Assert.DoesNotContain("component-detection-dependency-submission-action", workflow.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void Dependency_detection_uses_a_pinned_hash_verified_component_detection()
	{
		string script = RepositoryFile.ReadText("eng/ci/New-DependencySnapshot.ps1");

		Assert.Matches(new Regex(@"^\$DetectorVersion = '\d+\.\d+\.\d+'\r?$", RegexOptions.Multiline, TimeSpan.FromSeconds(1)), script);
		Assert.Matches(new Regex(@"^\$DetectorSha256 = '[0-9a-f]{64}'\r?$", RegexOptions.Multiline, TimeSpan.FromSeconds(1)), script);
		Assert.Contains("releases/download/v$DetectorVersion/$DetectorAsset", script, StringComparison.Ordinal);
		Assert.Contains("Get-FileHash -LiteralPath $detector -Algorithm SHA256", script, StringComparison.Ordinal);
		Assert.Contains("'--locked-mode'", script, StringComparison.Ordinal);
		Assert.DoesNotContain("releases/latest", script, StringComparison.Ordinal);
		Assert.DoesNotContain("dependency-graph/snapshots", script, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("valid", true)]
	[InlineData("other_commit", false)]
	[InlineData("other_ref", false)]
	[InlineData("other_correlator", false)]
	[InlineData("extra_property", false)]
	[InlineData("no_manifest", false)]
	public async Task Dependency_submit_step_submits_only_a_snapshot_of_this_run(string variant, bool submitted)
	{
		const string Sha = "0123456789abcdef0123456789abcdef01234567";
		const string Ref = "refs/heads/main";
		using TemporaryDirectory directory = new();
		Directory.CreateDirectory(directory.File("snapshot"));
		Dictionary<string, object> snapshot = new(StringComparer.Ordinal)
		{
			["version"] = 0,
			["sha"] = Is(variant, "other_commit") ? new string('f', 40) : Sha,
			["ref"] = Is(variant, "other_ref") ? "refs/heads/feature" : Ref,
			["job"] = new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["correlator"] = Is(variant, "other_correlator") ? "other" : "sdk-nuget",
				["id"] = "1"
			},
			["detector"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["name"] = "d", ["version"] = "1", ["url"] = "u" },
			["scanned"] = "2026-09-23T00:00:00Z",
			["manifests"] = Is(variant, "no_manifest")
				? new Dictionary<string, object>(StringComparer.Ordinal)
				: new Dictionary<string, object>(StringComparer.Ordinal) { ["src/A.csproj"] = new Dictionary<string, object>(StringComparer.Ordinal) }
		};
		if (Is(variant, "extra_property"))
		{
			snapshot["extra"] = 1;
		}

		await File.WriteAllTextAsync(directory.File("snapshot/snapshot.json"), JsonSerializer.Serialize(snapshot),
			TestContext.Current.CancellationToken);

		YamlMappingNode submit = YamlDocument.Steps(YamlDocument.Load(GovernanceWorkflows.DependencySubmission).Job("submit"))[1];
		string marker = directory.File("gh-called.txt");
		string script = $"Set-Location -LiteralPath {PwshScript.Quote(directory.Path)}" + Environment.NewLine +
						$"function gh {{ $args -join ' ' | Set-Content -LiteralPath {PwshScript.Quote(marker)}; $global:LASTEXITCODE = 0 }}" +
						Environment.NewLine + YamlDocument.Scalar(submit, "run");
		Dictionary<string, string> environment = new(StringComparer.Ordinal)
		{
			["REPOSITORY"] = "CheatEngineNet/CheatEngine.SDK",
			["SNAPSHOT_SHA"] = Sha,
			["SNAPSHOT_REF"] = Ref,
			["SNAPSHOT_CORRELATOR"] = "sdk-nuget",
			["GITHUB_STEP_SUMMARY"] = directory.File("summary.md")
		};

		PwshResult run = await PwshScript.RunTextAsync(script, environment);

		Assert.True(submitted == (run.ExitCode == 0), run.Transcript);
		Assert.Equal(submitted, File.Exists(marker));
		if (submitted)
		{
			Assert.StartsWith("api --method POST repos/CheatEngineNet/CheatEngine.SDK/dependency-graph/snapshots",
				await File.ReadAllTextAsync(marker, TestContext.Current.CancellationToken), StringComparison.Ordinal);
		}
		else
		{
			Assert.Contains("nothing was submitted", run.StandardError + run.StandardOutput, StringComparison.Ordinal);
		}
	}

	private static bool Is(string value, string expected)
	{
		return string.Equals(value, expected, StringComparison.Ordinal);
	}

	private static YamlNode? WithOf(YamlMappingNode job, string action)
	{
		YamlMappingNode step = Assert.Single(YamlDocument.Steps(job), step => YamlDocument.UsesAction(step, action)
																			|| string.Equals(YamlDocument.Uses(step), action, StringComparison.Ordinal));
		return YamlDocument.Child(step, "with");
	}

	private static int IndexOf(IReadOnlyList<YamlMappingNode> steps, string action)
	{
		for (int i = 0; i < steps.Count; i++)
		{
			if (YamlDocument.UsesAction(steps[i], action)
				|| string.Equals(YamlDocument.Uses(steps[i]), action, StringComparison.Ordinal))
			{
				return i;
			}
		}

		return -1;
	}

	[GeneratedRegex(@"^\s*(-\s+)?uses:\s+(?<reference>\S+)(?<comment>.*)$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
		matchTimeoutMilliseconds: 1000)]
	private static partial Regex UsesLine();

	[GeneratedRegex(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_./-]+@[0-9a-f]{40}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex PinnedReference();

	[GeneratedRegex(@"^\s+# v\d+\.\d+\.\d+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex VersionComment();

	/// <summary>A line that starts a native command or a repository script (same pattern as WorkflowContractTests).</summary>
	[GeneratedRegex(@"^\s*(?:dotnet|xmake|git|gh|tar)\s|^\s*\./|^\s*&\s", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex NativeInvocation();
}
