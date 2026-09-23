using System.Globalization;
using System.Text.RegularExpressions;

using YamlDotNet.RepresentationModel;

namespace CheatEngine.SDK.Repository.Tests.Workflows;

/// <summary>Rules for every workflow and composite action: runners, timeouts, permissions, pins and run scripts.</summary>
public sealed partial class WorkflowContractTests
{
	[Fact]
	public void Every_job_has_a_timeout_and_a_pinned_runner_label()
	{
		List<Violation> violations = [];
		foreach (WorkflowFile workflow in WorkflowFile.LoadWorkflows())
		{
			foreach (WorkflowJob job in workflow.Jobs())
			{
				// GitHub rejects runs-on and timeout-minutes on a reusable-workflow call; the callee's jobs are checked.
				if (job.CallsReusableWorkflow)
				{
					continue;
				}

				if (job.RunsOn is null || !WorkflowContract.RunnerLabels.Contains(job.RunsOn))
				{
					violations.Add(new Violation(workflow.FileName, job.Id,
						$"{job.Location} runs on '{job.RunsOn}': use a literal windows-2025 or ubuntu-24.04 label, never -latest or an expression."));
				}

				string? timeout = WorkflowFile.Scalar(job.Node, "timeout-minutes");
				if (!int.TryParse(timeout, NumberStyles.None, CultureInfo.InvariantCulture, out int minutes) || minutes <= 0)
				{
					violations.Add(new Violation(workflow.FileName, job.Id,
						$"{job.Location} has no positive timeout-minutes ('{timeout}')."));
				}
			}
		}

		WorkflowContract.AssertNoViolations(WorkflowContract.Rules.Runner, violations);
	}

	[Fact]
	public void Workflows_grant_only_read_permissions_at_the_top_level()
	{
		foreach (WorkflowFile workflow in WorkflowFile.LoadWorkflows())
		{
			Assert.True(workflow.Root.Children.TryGetValue(new YamlScalarNode("permissions"), out YamlNode? permissions),
				$"{workflow.FileName} must declare top-level permissions (contents: read) and elevate per job only.");
			if (permissions is YamlScalarNode scalar)
			{
				Assert.True(string.Equals(scalar.Value, "read-all", StringComparison.Ordinal),
					$"{workflow.FileName} grants '{scalar.Value}' to every job; elevate in the job that needs it.");
				continue;
			}

			YamlMappingNode scopes = Assert.IsType<YamlMappingNode>(permissions);
			foreach (KeyValuePair<YamlNode, YamlNode> scope in scopes.Children)
			{
				string value = ((YamlScalarNode) scope.Value).Value ?? "";
				Assert.True(value is "read" or "none",
					$"{workflow.FileName} grants '{scope.Key}: {value}' to every job; elevate in the job that needs it.");
			}

			if (Array.IndexOf(WorkflowContract.PipelineWorkflows, workflow.FileName) >= 0)
			{
				Assert.Equal(["contents"], WorkflowFile.Keys(scopes));
				Assert.Equal("read", WorkflowFile.Scalar(scopes, "contents"));
			}
		}
	}

	[Fact]
	public void Pipeline_jobs_never_elevate_permissions()
	{
		// ci.yml and sonar.yml run pull-request code: no job there may hold a write scope (contract 1.6).
		foreach (string fileName in new[] { WorkflowContract.Pipeline, WorkflowContract.Sonar })
		{
			foreach (WorkflowJob job in WorkflowFile.LoadWorkflow(fileName).Jobs())
			{
				if (!job.Node.Children.TryGetValue(new YamlScalarNode("permissions"), out YamlNode? permissions))
				{
					continue;
				}

				YamlMappingNode scopes = Assert.IsType<YamlMappingNode>(permissions);
				foreach (KeyValuePair<YamlNode, YamlNode> scope in scopes.Children)
				{
					string value = ((YamlScalarNode) scope.Value).Value ?? "";
					Assert.True(value is "read" or "none", $"{job.Location} grants '{scope.Key}: {value}'.");
				}
			}
		}
	}

	[Fact]
	public void Every_remote_action_is_pinned_to_a_full_sha_with_a_version_comment()
	{
		Dictionary<string, string> pins = new(StringComparer.Ordinal);
		foreach (WorkflowFile file in AllActionFiles())
		{
			foreach (string line in file.Text.Split('\n'))
			{
				Match uses = UsesLine().Match(line);
				if (!uses.Success || uses.Groups["reference"].Value.StartsWith("./", StringComparison.Ordinal))
				{
					continue;
				}

				Match pinned = PinnedReference().Match(uses.Groups["reference"].Value + uses.Groups["rest"].Value);
				Assert.True(pinned.Success,
					$"{file.RelativePath}: '{line.Trim()}' must be owner/repo@<40-hex commit> # vX.Y.Z (the tag of that commit).");

				// One action, one pin: the same action at two commits would be two supply-chain inputs to review.
				string action = pinned.Groups["action"].Value;
				string sha = pinned.Groups["sha"].Value;
				if (pins.TryGetValue(action, out string? other))
				{
					Assert.True(string.Equals(other, sha, StringComparison.Ordinal),
						$"{action} is pinned to both {other} and {sha}; use one commit everywhere.");
				}
				else
				{
					pins.Add(action, sha);
				}
			}
		}

		Assert.NotEmpty(pins);
	}

	[Fact]
	public void Every_checkout_disables_credential_persistence()
	{
		int checkouts = 0;
		foreach (WorkflowFile file in AllActionFiles())
		{
			foreach (YamlMappingNode step in AllSteps(file))
			{
				string? uses = WorkflowFile.Scalar(step, "uses");
				if (uses is null || !uses.StartsWith("actions/checkout@", StringComparison.Ordinal))
				{
					continue;
				}

				checkouts++;
				Assert.True(string.Equals(WorkflowJob.With(step, "persist-credentials"), "false", StringComparison.Ordinal),
					$"{file.RelativePath} line {step.Start.Line}: actions/checkout must set persist-credentials: false.");
			}
		}

		Assert.True(checkouts > 0, "No actions/checkout step was found.");
	}

	[Fact]
	public void Every_native_command_in_a_workflow_script_checks_its_exit_code()
	{
		List<Violation> violations = [];
		foreach (WorkflowFile file in AllActionFiles())
		{
			foreach ((string subject, YamlMappingNode step) in AllStepsWithSubject(file))
			{
				if (WorkflowFile.Scalar(step, "run") is not { } run)
				{
					continue;
				}

				string[] lines = run.Split('\n');
				for (int index = 0; index < lines.Length; index++)
				{
					if (!NativeInvocation().IsMatch(lines[index]))
					{
						continue;
					}

					// A command continued with a trailing backtick ends on its last continued line.
					int last = index;
					while (last + 1 < lines.Length && lines[last].TrimEnd().EndsWith('`'))
					{
						last++;
					}

					int next = last + 1;
					while (next < lines.Length && string.IsNullOrWhiteSpace(lines[next]))
					{
						next++;
					}

					if (next >= lines.Length ||
						!lines[next].TrimStart().StartsWith("if ($LASTEXITCODE -ne 0)", StringComparison.Ordinal))
					{
						violations.Add(new Violation(file.FileName, subject,
							$"{file.RelativePath} ({subject}): '{lines[index].Trim()}' must be followed by if ($LASTEXITCODE -ne 0) {{ throw ... }}."));
					}
				}
			}
		}

		WorkflowContract.AssertNoViolations(WorkflowContract.Rules.ExitCode, violations);
	}

	[Fact]
	public void No_run_script_interpolates_an_expression()
	{
		foreach (WorkflowFile file in AllActionFiles())
		{
			foreach (YamlMappingNode step in AllSteps(file))
			{
				string run = WorkflowFile.Scalar(step, "run") ?? "";
				Assert.False(run.Contains("${{", StringComparison.Ordinal),
					$"{file.RelativePath} line {step.Start.Line}: pass expressions to run scripts through env:, never inline (template injection).");
			}
		}
	}

	[Fact]
	public void Pipeline_workflows_and_composite_actions_run_scripts_in_pwsh()
	{
		foreach (string fileName in WorkflowContract.PipelineWorkflows)
		{
			WorkflowFile workflow = WorkflowFile.LoadWorkflow(fileName);
			YamlMappingNode? defaults = WorkflowFile.Mapping(workflow.Root, "defaults");
			YamlMappingNode? run = defaults is null ? null : WorkflowFile.Mapping(defaults, "run");
			Assert.True(run is not null && string.Equals(WorkflowFile.Scalar(run, "shell"), "pwsh", StringComparison.Ordinal),
				$"{fileName} must declare defaults.run.shell: pwsh.");
		}

		foreach (WorkflowFile action in WorkflowFile.LoadActions())
		{
			foreach (YamlMappingNode step in action.ActionSteps())
			{
				if (WorkflowFile.Has(step, "run"))
				{
					Assert.True(string.Equals(WorkflowFile.Scalar(step, "shell"), "pwsh", StringComparison.Ordinal),
						$"{action.RelativePath} line {step.Start.Line}: a composite run step must declare shell: pwsh.");
				}
			}
		}
	}

	[Fact]
	public void No_workflow_references_the_local_qualification_runner()
	{
		foreach (WorkflowFile file in AllActionFiles())
		{
			// The exact-host runner starts Cheat Engine; CI never does (levels C0-C2 only, audit ch.20).
			Assert.False(file.Text.Contains("eng/qualification", StringComparison.OrdinalIgnoreCase),
				$"{file.RelativePath} references eng/qualification: the local qualification runner never runs in CI.");
		}
	}

	[Fact]
	public void No_workflow_passes_ApiCompatGenerateSuppressionFile()
	{
		foreach (WorkflowFile file in AllActionFiles())
		{
			// Suppressions are regenerated by the integrator only; CI must fail on an undeclared break instead.
			Assert.False(file.Text.Contains("ApiCompatGenerateSuppressionFile", StringComparison.OrdinalIgnoreCase) ||
				file.Text.Contains("GenerateCompatibilitySuppressionFile", StringComparison.OrdinalIgnoreCase),
				$"{file.RelativePath} generates an ApiCompat suppression file; CI must fail on an undeclared break instead.");
		}
	}

	[GeneratedRegex(@"^\s*(?:-\s+)?uses:\s*(?<reference>\S+)(?<rest>.*)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex UsesLine();

	[GeneratedRegex(@"^(?<action>[A-Za-z0-9-]+/[A-Za-z0-9._/-]+)@(?<sha>[0-9a-f]{40}) # v\d+\.\d+\.\d+\s*$",
		RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex PinnedReference();

	[GeneratedRegex(@"^\s*(?:dotnet|xmake|git|gh|tar)\s|^\s*\./|^\s*&\s", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex NativeInvocation();
}
