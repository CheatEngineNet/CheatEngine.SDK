using System.Text.RegularExpressions;

using CheatEngine.SDK.Repository.Tests.Infrastructure;

using YamlDotNet.RepresentationModel;

namespace CheatEngine.SDK.Repository.Tests.Workflows;

/// <summary>Locked restores through the composite action, the pinned SDK, and no package cache (contract 1.5).</summary>
public sealed partial class WorkflowContractTests
{
	[Fact]
	public void Composite_setup_restores_in_locked_mode()
	{
		WorkflowFile action = SetupAction();

		YamlMappingNode inputs = Assert.IsType<YamlMappingNode>(WorkflowFile.Mapping(action.Root, "inputs"));
		Assert.Equal("false", WorkflowFile.Scalar(Assert.IsType<YamlMappingNode>(WorkflowFile.Mapping(inputs, "cache")), "default"));
		Assert.Equal("", WorkflowFile.Scalar(Assert.IsType<YamlMappingNode>(WorkflowFile.Mapping(inputs, "restore")), "default"));

		IReadOnlyList<YamlMappingNode> steps = action.ActionSteps();
		YamlMappingNode install = Assert.Single(steps,
			static step => (WorkflowFile.Scalar(step, "uses") ?? "").StartsWith("actions/setup-dotnet@", StringComparison.Ordinal));
		Assert.Equal("global.json", WorkflowJob.With(install, "global-json-file"));
		Assert.Equal("${{ inputs.cache }}", WorkflowJob.With(install, "cache"));
		Assert.Equal("**/packages.lock.json", WorkflowJob.With(install, "cache-dependency-path"));

		YamlMappingNode restore = Assert.Single(steps,
			static step => string.Equals(WorkflowFile.Scalar(step, "name"), "Restore", StringComparison.Ordinal));
		Assert.Equal("inputs.restore != ''", WorkflowFile.Scalar(restore, "if"));
		Assert.Equal("${{ inputs.restore }}", WorkflowJob.Env(restore, "RESTORE_TARGETS"));
		string run = WorkflowFile.Scalar(restore, "run") ?? "";
		// One locked restore per listed solution or project, each checked, with the fix in the message.
		Assert.Contains("-split \"`n\"", run, StringComparison.Ordinal);
		Assert.Contains("foreach ($target in $targets)", run, StringComparison.Ordinal);
		Assert.Contains("dotnet restore $target --locked-mode", run, StringComparison.Ordinal);
		Assert.Contains("./eng/Update-LockFiles.ps1", run, StringComparison.Ordinal);
		// NU1005: locked mode and force-evaluate cannot be combined.
		Assert.DoesNotContain("--force-evaluate", run, StringComparison.Ordinal);
	}

	[Fact]
	public void Every_restore_in_the_pipeline_is_locked()
	{
		List<string> unlocked = [];
		foreach (string fileName in new[] { WorkflowContract.Pipeline, WorkflowContract.Sonar })
		{
			foreach (WorkflowJob job in WorkflowFile.LoadWorkflow(fileName).Jobs())
			{
				foreach (string line in StripComments(job.RunText()).Split('\n'))
				{
					if (DotnetRestore().IsMatch(line) && !line.Contains("--locked-mode", StringComparison.Ordinal))
					{
						unlocked.Add($"{job.Location}: {line.Trim()}");
					}
				}
			}
		}

		Assert.True(unlocked.Count == 0,
			"Restore with --locked-mode, or through the composite action's restore input: " + string.Join("; ", unlocked));
	}

	[Fact]
	public void Release_reachable_workflows_never_enable_a_package_cache()
	{
		foreach (string fileName in WorkflowContract.CacheFreeWorkflows)
		{
			WorkflowFile? workflow = WorkflowFile.TryLoadWorkflow(fileName);
			if (workflow is null)
			{
				continue;
			}

			foreach (WorkflowJob job in workflow.Jobs())
			{
				foreach (YamlMappingNode step in job.Steps)
				{
					string uses = WorkflowFile.Scalar(step, "uses") ?? "";
					Assert.False(uses.StartsWith("actions/cache", StringComparison.Ordinal),
						$"{job.Location} uses {uses}: a cache written by a pull request run must never feed a release build.");
					string? cache = WorkflowJob.With(step, "cache");
					Assert.True(cache is null || string.Equals(cache, "false", StringComparison.Ordinal),
						$"{job.Location} passes cache: {cache}; every job of {fileName} is reachable from a release, Sonar or CodeQL run.");
				}
			}
		}
	}

	[Fact]
	public void Every_dotnet_job_uses_the_composite_setup_action()
	{
		List<Violation> violations = [];
		foreach (WorkflowFile workflow in WorkflowFile.LoadWorkflows())
		{
			foreach (WorkflowJob job in workflow.Jobs())
			{
				int firstDotnetStep = -1;
				int setupStep = -1;
				IReadOnlyList<YamlMappingNode> steps = job.Steps;
				for (int index = 0; index < steps.Count; index++)
				{
					if (setupStep < 0 &&
						string.Equals(WorkflowFile.Scalar(steps[index], "uses"), WorkflowContract.SetupAction, StringComparison.Ordinal))
					{
						setupStep = index;
					}

					if (firstDotnetStep < 0 && WorkflowFile.Scalar(steps[index], "run") is { } run && RunsDotnet(run))
					{
						firstDotnetStep = index;
					}
				}

				if (firstDotnetStep >= 0 && (setupStep < 0 || setupStep > firstDotnetStep))
				{
					violations.Add(new Violation(workflow.FileName, job.Id,
						$"{job.Location} runs dotnet without first using {WorkflowContract.SetupAction}: global.json pins the SDK with rollForward: disable and runner images do not ship it."));
				}
			}
		}

		WorkflowContract.AssertNoViolations(WorkflowContract.Rules.DotnetSetup, violations);
	}

	[Fact]
	public void Sonar_restores_locked_from_nuget_org_before_the_scanner_begins()
	{
		WorkflowJob analyze = WorkflowFile.LoadWorkflow(WorkflowContract.Sonar).Job("analyze");

		int restore = analyze.StepIndex("Restore from NuGet.org");
		int begin = analyze.StepIndex("Begin analysis");
		int build = analyze.StepIndex("Build");
		Assert.True(restore >= 0 && restore < begin && begin < build,
			"sonar.yml must restore before the scanner begins, then build without restoring: no pull-request-controlled source may run while scanner credentials are configured.");
		string restoreRun = WorkflowFile.Scalar(analyze.Steps[restore], "run") ?? "";
		Assert.Contains("--configfile $env:NUGET_CONFIG", restoreRun, StringComparison.Ordinal);
		Assert.Contains("--locked-mode", restoreRun, StringComparison.Ordinal);
		Assert.Contains("--no-restore", WorkflowFile.Scalar(analyze.Steps[build], "run"), StringComparison.Ordinal);

		// The composite action installs the SDK only: the checked-out nuget.config never resolves the scanner packages.
		YamlMappingNode setup = Assert.Single(analyze.StepsUsing(WorkflowContract.SetupAction));
		Assert.Null(WorkflowJob.With(setup, "restore"));
	}

	/// <summary>Whether a run script, or a repository script it invokes, runs the dotnet CLI.</summary>
	private static bool RunsDotnet(string run)
	{
		if (DotnetInvocation().IsMatch(StripComments(run)))
		{
			return true;
		}

		foreach (Match script in ScriptReference().Matches(run))
		{
			string path = Path.Combine(RepositoryRoot.Path, script.Groups["path"].Value);
			if (File.Exists(path) && DotnetInvocation().IsMatch(StripComments(File.ReadAllText(path))))
			{
				return true;
			}
		}

		return false;
	}

	[GeneratedRegex(@"(?m)(?:^|[\s;(|{&])dotnet\s", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex DotnetInvocation();

	[GeneratedRegex(@"\bdotnet\s+restore\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex DotnetRestore();

	[GeneratedRegex(@"(?:^|[\s(])\.?/?(?<path>(?:eng|tests)/[\w./-]+\.ps1)\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex ScriptReference();
}
