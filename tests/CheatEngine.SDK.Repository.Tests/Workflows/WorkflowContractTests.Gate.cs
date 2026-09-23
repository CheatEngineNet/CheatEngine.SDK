using YamlDotNet.RepresentationModel;

namespace CheatEngine.SDK.Repository.Tests.Workflows;

/// <summary>
///     The required check "CI / Gate" (contract 1.2 and 1.7): the callers, the gate job and its inputs, the job table of
///     ci.yml, the Sonar expectation and the triggers.
/// </summary>
public sealed partial class WorkflowContractTests
{
	private const string PipelineUses = "./.github/workflows/ci.yml";

	[Fact]
	public void Callers_invoke_ci_through_job_ci_named_CI()
	{
		foreach (string caller in WorkflowContract.Callers)
		{
			WorkflowJob job = WorkflowFile.LoadWorkflow(caller).Job(WorkflowContract.CallerJobId);
			Assert.True(string.Equals(WorkflowContract.CallerJobName, job.Name, StringComparison.Ordinal),
				$"{job.Location} must be named '{WorkflowContract.CallerJobName}': the required check is 'CI / Gate'.");
			Assert.Equal(PipelineUses, job.Uses);
		}

		// No other job calls the pipeline: a second caller would publish the gate under another check name.
		foreach (WorkflowFile workflow in WorkflowFile.LoadWorkflows())
		{
			foreach (WorkflowJob job in workflow.Jobs())
			{
				if (string.Equals(job.Uses, PipelineUses, StringComparison.Ordinal))
				{
					Assert.True(string.Equals(job.Id, WorkflowContract.CallerJobId, StringComparison.Ordinal) &&
						string.Equals(job.Name, WorkflowContract.CallerJobName, StringComparison.Ordinal),
						$"{job.Location} calls ci.yml; only a job 'ci' named 'CI' may.");
				}
			}
		}
	}

	[Fact]
	public void Pull_request_and_main_callers_request_sonar_and_the_release_run_never_does()
	{
		foreach (string caller in new[] { "pull-request-ci.yml", "main-ci.yml" })
		{
			WorkflowJob job = WorkflowFile.LoadWorkflow(caller).Job(WorkflowContract.CallerJobId);
			Assert.Equal("true", WorkflowJob.With(job.Node, "sonar"));
			YamlMappingNode secrets = Assert.IsType<YamlMappingNode>(WorkflowFile.Mapping(job.Node, "secrets"));
			Assert.Equal("${{ secrets.SONAR_TOKEN }}", WorkflowFile.Scalar(secrets, "SONAR_TOKEN"));
		}

		// The tag run builds the release candidate; SONAR_EXPECTED is false there (contract 1.11).
		WorkflowJob release = WorkflowFile.LoadWorkflow("release.yml").Job(WorkflowContract.CallerJobId);
		Assert.Null(WorkflowJob.With(release.Node, "sonar"));
	}

	[Fact]
	public void Gate_job_is_named_Gate_runs_always_and_has_no_permissions()
	{
		WorkflowJob gate = Pipeline().Job(WorkflowContract.GateJobId);

		Assert.Equal(WorkflowContract.GateJobName, gate.Name);
		Assert.Equal("always()", gate.Condition);
		Assert.True(gate.Node.Children.TryGetValue(new YamlScalarNode("permissions"), out YamlNode? permissions),
			"The gate must declare 'permissions: {}'.");
		Assert.Empty(Assert.IsType<YamlMappingNode>(permissions).Children);
		Assert.Empty(gate.StepsUsing("actions/checkout@"));

		YamlMappingNode step = Assert.Single(gate.Steps);
		Assert.Equal("${{ toJSON(needs) }}", WorkflowJob.Env(step, "NEEDS"));
		Assert.Equal("${{ github.event_name }}", WorkflowJob.Env(step, "EVENT"));
		string run = WorkflowFile.Scalar(step, "run") ?? "";
		// Data-driven: the verdict comes from a required result per job, never from chained -and/-or conditions.
		Assert.DoesNotContain(" -and ", run, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain(" -or ", run, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("$required = 'success'", run, StringComparison.Ordinal);
		Assert.Contains("$required = 'skipped'", run, StringComparison.Ordinal);
		Assert.Contains("if ($result -ne $required)", run, StringComparison.Ordinal);
		Assert.Contains("| Job | Result | Required | Reason |", run, StringComparison.Ordinal);
		Assert.Contains("Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8", run, StringComparison.Ordinal);
		Assert.Contains("exit 1", run, StringComparison.Ordinal);
	}

	[Fact]
	public void Gate_needs_every_other_ci_job_except_the_advisory_allowlist()
	{
		WorkflowFile pipeline = Pipeline();
		HashSet<string> needs = new(pipeline.Job(WorkflowContract.GateJobId).Needs(), StringComparer.Ordinal);
		HashSet<string> expected = new(StringComparer.Ordinal);
		foreach (WorkflowJob job in pipeline.Jobs())
		{
			if (string.Equals(job.Id, WorkflowContract.GateJobId, StringComparison.Ordinal))
			{
				continue;
			}

			if (WorkflowContract.AdvisoryJobs.Contains(job.Id))
			{
				Assert.Equal("true", WorkflowFile.Scalar(job.Node, "continue-on-error"));
				Assert.False(needs.Contains(job.Id), $"The advisory job '{job.Id}' must stay out of gate.needs.");
				continue;
			}

			expected.Add(job.Id);
		}

		List<string> missing = [.. expected.Except(needs, StringComparer.Ordinal)];
		List<string> extra = [.. needs.Except(expected, StringComparer.Ordinal)];
		Assert.True(missing.Count == 0 && extra.Count == 0,
			$"gate.needs must list every other ci.yml job. Missing: {string.Join(", ", missing)}. Unknown: {string.Join(", ", extra)}.");
	}

	[Fact]
	public void Ci_jobs_match_the_frozen_contract_ids_and_names()
	{
		Dictionary<string, WorkflowJob> jobs = new(StringComparer.Ordinal);
		foreach (WorkflowJob job in Pipeline().Jobs())
		{
			jobs.Add(job.Id, job);
		}

		foreach (PipelineJob expected in WorkflowContract.Jobs)
		{
			Assert.True(jobs.Remove(expected.Id, out WorkflowJob? job), $"ci.yml has no job '{expected.Id}'.");
			Assert.Equal(expected.Name, job.Name);
			Assert.Equal(expected.RunsOn, job.RunsOn);
			Assert.Equal(expected.TimeoutMinutes?.ToString(System.Globalization.CultureInfo.InvariantCulture),
				WorkflowFile.Scalar(job.Node, "timeout-minutes"));
		}

		// Anything else must be a job id reserved for a later wave, with its reserved name.
		foreach (WorkflowJob job in jobs.Values)
		{
			Assert.True(WorkflowContract.ReservedJobs.TryGetValue(job.Id, out string? name),
				$"ci.yml job '{job.Id}' is not in the contract (shared contract 1.6).");
			Assert.Equal(name, job.Name);
		}
	}

	[Fact]
	public void Ci_declares_exactly_the_contract_inputs_and_secret()
	{
		YamlMappingNode call = Assert.IsType<YamlMappingNode>(Pipeline().Trigger("workflow_call"));
		Assert.Equal(["workflow_call"], Pipeline().Triggers());

		YamlMappingNode inputs = Assert.IsType<YamlMappingNode>(WorkflowFile.Mapping(call, "inputs"));
		Assert.Equal(["sonar", "package-version", "package-retention-days"], WorkflowFile.Keys(inputs));
		AssertInput(inputs, "sonar", "boolean", "false");
		AssertInput(inputs, "package-version", "string", "");
		AssertInput(inputs, "package-retention-days", "number", "7");

		YamlMappingNode secrets = Assert.IsType<YamlMappingNode>(WorkflowFile.Mapping(call, "secrets"));
		Assert.Equal(["SONAR_TOKEN"], WorkflowFile.Keys(secrets));
		Assert.Equal("false", WorkflowFile.Scalar(Assert.IsType<YamlMappingNode>(WorkflowFile.Mapping(secrets, "SONAR_TOKEN")), "required"));
		Assert.False(WorkflowFile.Has(call, "outputs"), "ci.yml has no workflow_call outputs in v1 (contract 1.4).");

		// No repository variable steers the pipeline (contract 1.4).
		Assert.DoesNotContain("vars.", Pipeline().Text, StringComparison.Ordinal);
	}

	[Fact]
	public void Sonar_condition_equals_the_gate_sonar_expected_expression()
	{
		WorkflowFile pipeline = Pipeline();
		string condition = WorkflowFile.NormalizeWhitespace(pipeline.Job("sonar").Condition ?? "");
		YamlMappingNode gateStep = Assert.Single(pipeline.Job(WorkflowContract.GateJobId).Steps);
		string expected = WorkflowFile.NormalizeWhitespace(WorkflowJob.Env(gateStep, "SONAR_EXPECTED") ?? "");

		Assert.Equal(WorkflowContract.SonarExpected, condition);
		Assert.Equal(condition, expected);
	}

	[Fact]
	public void Sonar_waits_for_the_quality_gate_outside_push_events()
	{
		WorkflowJob sonar = Pipeline().Job("sonar");
		Assert.Equal("./.github/workflows/sonar.yml", sonar.Uses);
		Assert.Equal(["build-test"], sonar.Needs());
		Assert.Equal("${{ github.event_name != 'push' }}", WorkflowJob.With(sonar.Node, "wait-quality-gate"));
		Assert.Equal("${{ secrets.SONAR_TOKEN }}",
			WorkflowFile.Scalar(Assert.IsType<YamlMappingNode>(WorkflowFile.Mapping(sonar.Node, "secrets")), "SONAR_TOKEN"));

		WorkflowJob analyze = WorkflowFile.LoadWorkflow(WorkflowContract.Sonar).Job("analyze");
		YamlMappingNode env = Assert.IsType<YamlMappingNode>(WorkflowFile.Mapping(analyze.Node, "env"));
		Assert.Equal("${{ inputs.wait-quality-gate }}", WorkflowFile.Scalar(env, "SONAR_WAIT_QUALITY_GATE"));
		Assert.Contains("/d:sonar.qualitygate.wait=$env:SONAR_WAIT_QUALITY_GATE",
			WorkflowFile.Scalar(analyze.Step("Begin analysis"), "run"), StringComparison.Ordinal);

		// A failed quality gate stays one click away: the summary links the analysis even when End analysis fails.
		YamlMappingNode link = analyze.Step("Link the analysis");
		Assert.True(analyze.StepIndex("End analysis") < analyze.StepIndex("Link the analysis"));
		Assert.Equal("${{ !cancelled() }}", WorkflowFile.Scalar(link, "if"));
		Assert.Contains("Out-File -FilePath $env:GITHUB_STEP_SUMMARY", WorkflowFile.Scalar(link, "run"), StringComparison.Ordinal);
	}

	[Fact]
	public void Sonar_excludes_non_product_trees_from_analysis_and_coverage()
	{
		string begin = WorkflowFile.Scalar(
			WorkflowFile.LoadWorkflow(WorkflowContract.Sonar).Job("analyze").Step("Begin analysis"), "run") ?? "";

		HashSet<string> exclusions = new(SonarProperty(begin, "sonar.exclusions").Split(','), StringComparer.Ordinal);
		foreach (string tree in new[]
				 {
					 "artifacts/**", "tests/CheatEngine.SDK.QualificationTarget/**", "tests/native-host-emulator/**", "eng/tools/**",
					 "docs/**"
				 })
		{
			Assert.True(exclusions.Contains(tree), $"sonar.exclusions must list {tree}.");
		}

		// Tests stay analysed (their issues are triaged by rule), but they never count as product coverage.
		Assert.False(exclusions.Contains("tests/**"), "Tests stay analysed; only their coverage is excluded.");
		HashSet<string> coverage = new(SonarProperty(begin, "sonar.coverage.exclusions").Split(','), StringComparer.Ordinal);
		foreach (string tree in new[] { "tests/**", "eng/**", "docs/**" })
		{
			Assert.True(coverage.Contains(tree), $"sonar.coverage.exclusions must list {tree}.");
		}
	}

	[Fact]
	public void No_workflow_uses_pull_request_target_or_a_merge_group_trigger()
	{
		foreach (WorkflowFile workflow in WorkflowFile.LoadWorkflows())
		{
			IReadOnlyList<string> triggers = workflow.Triggers();
			Assert.False(triggers.Contains("pull_request_target", StringComparer.Ordinal),
				$"{workflow.FileName} must not use pull_request_target: it runs pull-request code with the base repository's secrets.");
			Assert.False(triggers.Contains("merge_group", StringComparer.Ordinal),
				$"{workflow.FileName} must not listen to merge_group: there is no merge queue, and an untested event path must not feed the required check.");
		}
	}

	[Fact]
	public void Pull_request_and_policy_workflows_have_no_path_filters()
	{
		foreach (string fileName in new[] { "pull-request-ci.yml", "pr-policy.yml" })
		{
			WorkflowFile? workflow = WorkflowFile.TryLoadWorkflow(fileName);
			if (workflow is null)
			{
				// pr-policy.yml no longer exists (the PR title/changelog policy engine was removed); pull-request-ci.yml
				// must always exist.
				Assert.False(string.Equals(fileName, "pull-request-ci.yml", StringComparison.Ordinal), $"{fileName} is missing.");
				continue;
			}

			foreach (string trigger in workflow.Triggers())
			{
				YamlMappingNode? configuration = workflow.Trigger(trigger);
				if (configuration is null)
				{
					continue;
				}

				Assert.False(WorkflowFile.Has(configuration, "paths") || WorkflowFile.Has(configuration, "paths-ignore"),
					$"{fileName} '{trigger}' must not filter paths: its required check must report on every pull request.");
			}
		}
	}

	[Fact]
	public void Main_ci_runs_every_push_to_main_without_a_concurrency_group()
	{
		WorkflowFile main = WorkflowFile.LoadWorkflow("main-ci.yml");

		Assert.Equal(["push", "workflow_dispatch"], main.Triggers());
		YamlMappingNode push = Assert.IsType<YamlMappingNode>(main.Trigger("push"));
		Assert.Equal(["main"], WorkflowFile.ScalarValues(Assert.IsType<YamlSequenceNode>(WorkflowFile.Sequence(push, "branches"))));
		Assert.False(WorkflowFile.Has(main.Root, "concurrency"),
			"main-ci.yml keeps every main commit's run: a concurrency group would cancel or drop intermediate runs.");
	}

	[Fact]
	public void Pull_request_ci_skips_drafts_and_cancels_superseded_runs()
	{
		WorkflowFile pullRequest = WorkflowFile.LoadWorkflow("pull-request-ci.yml");

		Assert.Equal(["pull_request"], pullRequest.Triggers());
		YamlMappingNode trigger = Assert.IsType<YamlMappingNode>(pullRequest.Trigger("pull_request"));
		Assert.Equal(["opened", "synchronize", "reopened", "ready_for_review"],
			WorkflowFile.ScalarValues(Assert.IsType<YamlSequenceNode>(WorkflowFile.Sequence(trigger, "types"))));
		Assert.Equal("${{ !github.event.pull_request.draft }}", pullRequest.Job(WorkflowContract.CallerJobId).Condition);

		YamlMappingNode concurrency = Assert.IsType<YamlMappingNode>(WorkflowFile.Mapping(pullRequest.Root, "concurrency"));
		Assert.Equal("${{ github.workflow }}-${{ github.event.pull_request.number }}", WorkflowFile.Scalar(concurrency, "group"));
		Assert.Equal("true", WorkflowFile.Scalar(concurrency, "cancel-in-progress"));
	}

	private static void AssertInput(YamlMappingNode inputs, string name, string type, string defaultValue)
	{
		YamlMappingNode input = Assert.IsType<YamlMappingNode>(WorkflowFile.Mapping(inputs, name));
		Assert.Equal(type, WorkflowFile.Scalar(input, "type"));
		Assert.Equal(defaultValue, WorkflowFile.Scalar(input, "default"));
	}

	private static string SonarProperty(string beginScript, string property)
	{
		string marker = $"/d:{property}=";
		int start = beginScript.IndexOf(marker, StringComparison.Ordinal);
		Assert.True(start >= 0, $"The Begin analysis step does not set {property}.");
		start += marker.Length;
		int end = beginScript.IndexOf('\'', start);
		return beginScript[start..end];
	}
}
