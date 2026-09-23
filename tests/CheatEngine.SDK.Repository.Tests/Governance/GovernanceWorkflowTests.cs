using System.Globalization;
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
