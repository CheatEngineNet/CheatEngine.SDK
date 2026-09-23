using System.Text.RegularExpressions;

using YamlDotNet.RepresentationModel;

namespace CheatEngine.SDK.Repository.Tests.Workflows;

/// <summary>The lint and format jobs: actionlint, zizmor and PSScriptAnalyzer pinned and verified, whitespace verified.</summary>
public sealed partial class WorkflowContractTests
{
	private const string ZizmorConfig = ".github/zizmor.yml";

	[Fact]
	public void Lint_job_checks_out_the_repository_and_runs_every_linter()
	{
		WorkflowJob lint = Pipeline().Job("lint");

		// tests/native-abi-fixture/build.ps1 and other repository scripts live outside .github: the whole tree is
		// checked out, not a sparse .github.
		YamlMappingNode checkout = Assert.Single(lint.StepsUsing("actions/checkout@"));
		Assert.Null(WorkflowJob.With(checkout, "sparse-checkout"));
		Assert.Empty(lint.Needs());
		foreach (string step in new[] { "Run actionlint", "Run zizmor" })
		{
			Assert.True(lint.StepIndex(step) >= 0, $"The lint job has no '{step}' step.");
		}
	}

	[Fact]
	public void Zizmor_and_actionlint_are_pinned_by_version_and_checksum()
	{
		WorkflowJob lint = Pipeline().Job("lint");
		YamlMappingNode env = Assert.IsType<YamlMappingNode>(WorkflowFile.Mapping(lint.Node, "env"));
		Assert.Matches(ExactVersion(), WorkflowFile.Scalar(env, "ACTIONLINT_VERSION") ?? "");
		Assert.Matches(Sha256(), WorkflowFile.Scalar(env, "ACTIONLINT_SHA256") ?? "");
		string actionlint = WorkflowFile.Scalar(lint.Step("Run actionlint"), "run") ?? "";
		Assert.Contains("(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()", actionlint,
			StringComparison.Ordinal);
		Assert.Contains("if ($actual -ne $env:ACTIONLINT_SHA256)", actionlint, StringComparison.Ordinal);

		// The action commit and the zizmor version are both pinned, and the gate never depends on the GitHub API.
		YamlMappingNode zizmor = Assert.Single(lint.StepsUsing("zizmorcore/zizmor-action@"));
		Assert.Matches(ExactVersion(), WorkflowJob.With(zizmor, "version") ?? "");
		Assert.Equal("false", WorkflowJob.With(zizmor, "online-audits"));
		Assert.Equal("false", WorkflowJob.With(zizmor, "advanced-security"));
		Assert.Equal(ZizmorConfig, WorkflowJob.With(zizmor, "config"));
		Assert.Null(WorkflowJob.With(zizmor, "persona"));
		Assert.Null(WorkflowJob.With(zizmor, "min-severity"));
		Assert.NotNull(ReadRepositoryText(ZizmorConfig));
	}

	[Fact]
	public void Every_zizmor_exception_carries_a_justification_comment()
	{
		string[] lines = ReadRepositoryText(ZizmorConfig).Split('\n');
		YamlStream stream = [];
		stream.Load(new StringReader(string.Join('\n', lines)));
		YamlMappingNode rules = Assert.IsType<YamlMappingNode>(
			WorkflowFile.Mapping(Assert.IsType<YamlMappingNode>(stream.Documents[0].RootNode), "rules"));
		foreach (KeyValuePair<YamlNode, YamlNode> rule in rules.Children)
		{
			YamlMappingNode body = Assert.IsType<YamlMappingNode>(rule.Value);
			Assert.True(WorkflowFile.Has(body, "ignore") || WorkflowFile.Has(body, "disable"),
				$"{ZizmorConfig} rule '{rule.Key}' neither ignores nor disables anything.");
			int line = (int) rule.Key.Start.Line - 2;
			while (line >= 0 && string.IsNullOrWhiteSpace(lines[line]))
			{
				line--;
			}

			Assert.True(line >= 0 && lines[line].TrimStart().StartsWith('#'),
				$"{ZizmorConfig} rule '{rule.Key}' needs a comment directly above it giving the reason.");
		}

		// Inline suppressions (the only kind a composite action supports) carry their reason on the same comment.
		foreach (WorkflowFile file in AllActionFiles())
		{
			foreach (Match ignore in InlineZizmorIgnore().Matches(file.Text))
			{
				Assert.True(ignore.Groups["reason"].Value.Trim().Length >= 20,
					$"{file.RelativePath}: '{ignore.Value.Trim()}' must explain why the finding is acceptable.");
			}
		}
	}

	[Fact]
	public void Format_job_verifies_whitespace_without_restore()
	{
		WorkflowJob format = Pipeline().Job("format");
		Assert.Empty(format.Needs());

		// The pinned SDK comes from the composite action; --folder needs no restore and no MSBuild workspace.
		YamlMappingNode setup = Assert.Single(format.StepsUsing(WorkflowContract.SetupAction));
		Assert.Null(WorkflowJob.With(setup, "restore"));
		string run = format.RunText();
		Assert.Contains("dotnet format whitespace . --folder --verify-no-changes --exclude artifacts", run,
			StringComparison.Ordinal);
		Assert.DoesNotContain("dotnet restore", run, StringComparison.Ordinal);
		Assert.DoesNotContain("dotnet build", run, StringComparison.Ordinal);
		// Style rules are enforced by the build (EnforceCodeStyleInBuild, warnings as errors), not by this job.
		Assert.DoesNotContain("format style", run, StringComparison.Ordinal);

		// Valid on Linux only because the checkout is CRLF and .editorconfig says so.
		Assert.Contains("end_of_line = crlf", ReadRepositoryText(".editorconfig"), StringComparison.Ordinal);
	}

	[GeneratedRegex(@"#\s*zizmor:\s*ignore\[[^\]]+\](?<reason>[^\n]*)", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex InlineZizmorIgnore();

	[GeneratedRegex(@"^\d+\.\d+\.\d+$", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex ExactVersion();

	[GeneratedRegex(@"^[0-9a-f]{64}$", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex Sha256();
}
