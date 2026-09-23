using YamlDotNet.RepresentationModel;

namespace CheatEngine.SDK.Repository.Tests.Workflows;

/// <summary>Supply-chain jobs of ci.yml: the dependency review that never skips and the lock-file drift check.</summary>
public sealed partial class WorkflowContractTests
{
	private const string DependencyReviewConfig = ".github/dependency-review-config.yml";

	[Fact]
	public void Dependency_review_job_always_runs_and_reviews_only_pull_requests()
	{
		WorkflowJob job = Pipeline().Job("dependency-review");

		// The gate requires success: the job itself never skips, only its steps choose by event (contract 1.11).
		Assert.Null(job.Condition);
		Assert.Empty(job.Needs());
		Assert.False(WorkflowFile.Has(job.Node, "permissions"),
			"dependency-review keeps the read-only top-level permissions.");

		YamlMappingNode review = Assert.Single(job.StepsUsing("actions/dependency-review-action@"));
		Assert.Equal("github.event_name == 'pull_request'", WorkflowFile.Scalar(review, "if"));
		Assert.Equal($"./{DependencyReviewConfig}", WorkflowJob.With(review, "config-file"));
		Assert.Equal("never", WorkflowJob.With(review, "comment-summary-in-pr"));
		Assert.Equal("true", WorkflowJob.With(review, "retry-on-snapshot-warnings"));

		YamlMappingNode notice = job.Step("Nothing to review");
		Assert.Equal("github.event_name != 'pull_request'", WorkflowFile.Scalar(notice, "if"));
		Assert.Contains("::notice", WorkflowFile.Scalar(notice, "run"), StringComparison.Ordinal);

		// Every other step serves the review and runs on pull requests only.
		foreach (YamlMappingNode step in job.Steps)
		{
			string? condition = WorkflowFile.Scalar(step, "if");
			Assert.True(condition is "github.event_name == 'pull_request'" or "github.event_name != 'pull_request'",
				$"{job.Location} step '{WorkflowFile.Scalar(step, "name")}' must choose by event, never skip the job.");
		}
	}

	[Fact]
	public void Dependency_review_configuration_blocks_advisories_and_unreviewed_licenses()
	{
		YamlStream stream = [];
		stream.Load(new StringReader(ReadRepositoryText(DependencyReviewConfig)));
		YamlMappingNode config = Assert.IsType<YamlMappingNode>(stream.Documents[0].RootNode);

		Assert.Equal("moderate", WorkflowFile.Scalar(config, "fail-on-severity"));
		Assert.Equal(["runtime", "development"],
			WorkflowFile.ScalarValues(
				Assert.IsType<YamlSequenceNode>(WorkflowFile.Sequence(config, "fail-on-scopes"))));
		Assert.Contains("MIT",
			WorkflowFile.ScalarValues(Assert.IsType<YamlSequenceNode>(WorkflowFile.Sequence(config, "allow-licenses"))),
			StringComparer.Ordinal);
		foreach (string purl in WorkflowFile.ScalarValues(
			         Assert.IsType<YamlSequenceNode>(WorkflowFile.Sequence(config, "allow-dependencies-licenses"))))
		{
			Assert.StartsWith("pkg:nuget/", purl, StringComparison.Ordinal);
		}

		// The workflow sets these inline (contract 1.6); a second value here could silently disagree.
		foreach (string inline in new[] { "comment-summary-in-pr", "retry-on-snapshot-warnings", "config-file" })
		{
			Assert.False(WorkflowFile.Has(config, inline),
				$"{DependencyReviewConfig} must not repeat '{inline}', which ci.yml sets.");
		}
	}

	[Fact]
	public void Lock_file_job_restores_the_solution_and_every_out_of_solution_project_locked_on_windows()
	{
		WorkflowJob job = Pipeline().Job("lock-files");

		// Native AOT lock sections record the host-RID ILCompiler packages: only a Windows restore reproduces them.
		Assert.Equal("windows-2025", job.RunsOn);
		Assert.Empty(job.Needs());

		// The composite action's own --locked-mode restore IS the verification (NU1004 the moment a committed
		// packages.lock.json no longer matches a fresh restore): no separate script needed. AotProbe is the only
		// project outside the solution (Solution/SolutionInventoryTests.cs).
		YamlMappingNode setup = Assert.Single(job.StepsUsing(WorkflowContract.SetupAction));
		List<string> targets = RestoreTargets(setup);
		Assert.Contains("CheatEngine.SDK.slnx", targets, StringComparer.Ordinal);
		Assert.Contains("tests/CheatEngine.SDK.AotProbe/CheatEngine.SDK.AotProbe.csproj", targets,
			StringComparer.Ordinal);
	}
}
