using System.Globalization;

using YamlDotNet.RepresentationModel;

namespace CheatEngine.SDK.Repository.Tests.Release;

/// <summary>
///     The release workflow is draft-first and compatible with immutable releases (shared contract 1.11, audit ch.21
///     "Le tuple a qualifier", Checkpoint F): <c>verify → ci → attest → draft-release → publish → verify-publication →
///     finalize-release</c>. Attestations and every asset exist before the draft is published, publication jobs run only
///     for version tags of this repository, the only write tokens live in the jobs that need them, NuGet trusted
///     publishing stays in the <c>publish</c> job with environment <c>nuget</c>, and nothing is ever uploaded to a
///     published release. These tests read the committed YAML only.
/// </summary>
public sealed class ReleaseWorkflowContractTests
{
	/// <summary>The literal guard of every publication job and attestation step (<c>TAG_GUARD</c> in the lot brief).</summary>
	private const string TagGuard =
		"github.event_name == 'push' && github.ref_type == 'tag' && github.repository == 'CheatEngineNet/CheatEngine.SDK'";

	private const string SpdxPredicate = "https://spdx.dev/Document/v2.2";

	private static readonly (string Id, string Name, string[] Needs)[] s_chain =
	[
		("verify", "Verify tag", []),
		("ci", "CI", ["verify"]),
		("attest", "Attest and assemble release assets", ["verify", "ci"]),
		("draft-release", "Create draft release", ["verify", "attest"]),
		("publish", "Publish to NuGet", ["verify", "draft-release"]),
		("verify-publication", "Verify nuget.org publication", ["verify", "publish"]),
		("finalize-release", "Publish GitHub release", ["verify", "attest", "verify-publication"])
	];

	private static readonly string[] s_publicationJobs = ["draft-release", "publish", "verify-publication", "finalize-release"];

	private static readonly HashSet<string> s_runnerLabels = new(StringComparer.Ordinal) { "windows-2025", "ubuntu-24.04" };

	[Fact]
	public void Release_runs_for_version_tags_and_manual_dry_runs_without_cancelling()
	{
		ReleaseWorkflow workflow = ReleaseWorkflow.Load();
		YamlMappingNode on = ReleaseWorkflow.Mapping(workflow.Root, "on")!;

		Assert.Equal(["push", "workflow_dispatch"], ReleaseWorkflow.Keys(on));
		YamlMappingNode push = ReleaseWorkflow.Mapping(on, "push")!;
		Assert.Equal(["tags"], ReleaseWorkflow.Keys(push));
		Assert.Equal(["v*.*.*"], ReleaseWorkflow.ScalarValues(ReleaseWorkflow.Sequence(push, "tags")!));

		// A second tag run of the same ref waits instead of cancelling a publication half-way.
		YamlMappingNode concurrency = ReleaseWorkflow.Mapping(workflow.Root, "concurrency")!;
		Assert.Equal("${{ github.workflow }}-${{ github.ref }}", ReleaseWorkflow.Scalar(concurrency, "group"));
		Assert.Equal("false", ReleaseWorkflow.Scalar(concurrency, "cancel-in-progress"));
		Assert.Equal("read", ReleaseWorkflow.Scalar(ReleaseWorkflow.Mapping(workflow.Root, "permissions")!, "contents"));
		Assert.Single(ReleaseWorkflow.Keys(ReleaseWorkflow.Mapping(workflow.Root, "permissions")!));
	}

	[Fact]
	public void Release_jobs_form_the_draft_first_chain()
	{
		ReleaseWorkflow workflow = ReleaseWorkflow.Load();

		Assert.Equal(Ids(), workflow.JobIds(), StringComparer.Ordinal);
		foreach ((string id, string name, string[] needs) in s_chain)
		{
			YamlMappingNode job = workflow.Job(id);
			Assert.Equal(name, ReleaseWorkflow.Scalar(job, "name"));
			Assert.Equal(needs, ReleaseWorkflow.Needs(job), StringComparer.Ordinal);
		}
	}

	[Fact]
	public void Publication_jobs_run_only_for_tags_of_this_repository()
	{
		ReleaseWorkflow workflow = ReleaseWorkflow.Load();

		foreach (string id in Ids())
		{
			string guard = ReleaseWorkflow.Normalize(ReleaseWorkflow.Scalar(workflow.Job(id), "if"));
			if (s_publicationJobs.Contains(id, StringComparer.Ordinal))
			{
				Assert.True(string.Equals(TagGuard, guard, StringComparison.Ordinal), $"Job '{id}' must run only when '{TagGuard}', not '{guard}'.");
			}
			else
			{
				Assert.True(guard.Length == 0, $"Job '{id}' runs on dry runs too and must not be conditional ('{guard}').");
			}
		}

		// The attest job also runs on dry runs, so each of its steps that signs or checks an attestation is guarded.
		YamlMappingNode attest = workflow.Job("attest");
		List<YamlMappingNode> attestations = ReleaseWorkflow.StepsUsing(attest, "actions/attest@");
		Assert.Equal(2, attestations.Count);
		foreach (YamlMappingNode step in ReleaseWorkflow.Steps(attest))
		{
			bool signsOrVerifies = attestations.Contains(step)
								   || (ReleaseWorkflow.Scalar(step, "run") ?? "").Contains("gh attestation verify", StringComparison.Ordinal);
			if (signsOrVerifies)
			{
				Assert.Equal(TagGuard, ReleaseWorkflow.Normalize(ReleaseWorkflow.Scalar(step, "if")));
			}
		}
	}

	[Fact]
	public void Attest_job_attests_the_package_provenance_and_its_spdx_2_2_sbom()
	{
		YamlMappingNode attest = ReleaseWorkflow.Load().Job("attest");
		List<YamlMappingNode> attestations = ReleaseWorkflow.StepsUsing(attest, "actions/attest@");

		// Provenance first (no sbom-path), then the SBOM predicate, both about the package staged from nuget-package.
		Assert.Equal("${{ steps.stage.outputs.package }}", ReleaseWorkflow.With(attestations[0], "subject-path"));
		Assert.Null(ReleaseWorkflow.With(attestations[0], "sbom-path"));
		Assert.Equal("${{ steps.stage.outputs.package }}", ReleaseWorkflow.With(attestations[1], "subject-path"));
		Assert.Equal("${{ steps.stage.outputs.sbom }}", ReleaseWorkflow.With(attestations[1], "sbom-path"));

		string run = ReleaseWorkflow.RunText(attest);
		Assert.Contains("./eng/release/Export-PackageSbom.ps1", run, StringComparison.Ordinal);
		Assert.Contains("--predicate-type', '" + SpdxPredicate, run, StringComparison.Ordinal);
		Assert.Contains("--deny-self-hosted-runners", run, StringComparison.Ordinal);
		Assert.Contains("./eng/release/New-Sha256Sums.ps1", run, StringComparison.Ordinal);
		Assert.Contains("./eng/release/New-ReleaseTuple.ps1", run, StringComparison.Ordinal);
		Assert.Contains("'PrePublish'", run, StringComparison.Ordinal);
	}

	[Fact]
	public void Only_the_publish_job_uses_the_nuget_environment_and_nuget_login()
	{
		ReleaseWorkflow workflow = ReleaseWorkflow.Load();

		foreach (string id in Ids())
		{
			YamlMappingNode job = workflow.Job(id);
			bool isPublish = string.Equals(id, "publish", StringComparison.Ordinal);
			YamlNode? environment = job.Children.TryGetValue(new YamlScalarNode("environment"), out YamlNode? value) ? value : null;
			List<YamlMappingNode> logins = ReleaseWorkflow.StepsUsing(job, "NuGet/login@");
			bool pushes = ReleaseWorkflow.RunText(job).Contains("dotnet nuget push", StringComparison.Ordinal);
			if (!isPublish)
			{
				Assert.True(environment is null, $"Job '{id}' uses an environment; only 'publish' may use 'nuget'.");
				Assert.True(logins.Count == 0 && !pushes, $"Job '{id}' logs in to or pushes to NuGet; only 'publish' may.");
				continue;
			}

			// Trusted publishing is bound to release.yml + environment nuget, and the temporary key lives one hour, so the
			// login is the step right before the push.
			Assert.Equal("nuget", ReleaseWorkflow.Scalar((YamlMappingNode) environment!, "name"));
			YamlMappingNode login = Assert.Single(logins);
			Assert.Equal("${{ secrets.NUGET_USER }}", ReleaseWorkflow.With(login, "user"));
			List<YamlMappingNode> steps = ReleaseWorkflow.Steps(job);
			int loginIndex = steps.IndexOf(login);
			Assert.True(loginIndex + 1 < steps.Count, "The NuGet login must be followed by the push.");
			Assert.Contains("dotnet nuget push", ReleaseWorkflow.Scalar(steps[loginIndex + 1], "run") ?? "", StringComparison.Ordinal);
			Assert.Equal("${{ steps.login.outputs.NUGET_API_KEY }}",
				ReleaseWorkflow.Scalar(ReleaseWorkflow.Mapping(steps[loginIndex + 1], "env")!, "NUGET_API_KEY"));
		}
	}

	[Fact]
	public void Id_token_write_is_limited_to_attest_publish_and_finalize()
	{
		AssertWriteScope("id-token", ["attest", "publish", "finalize-release"]);
		AssertWriteScope("attestations", ["attest", "finalize-release"]);
	}

	[Fact]
	public void Contents_write_is_limited_to_draft_release_and_finalize_release()
	{
		AssertWriteScope("contents", ["draft-release", "finalize-release"]);

		// No job holds a write scope beyond the ones the chain needs.
		ReleaseWorkflow workflow = ReleaseWorkflow.Load();
		foreach (string id in Ids())
		{
			foreach ((string scope, string value) in ReleaseWorkflow.Permissions(workflow.Job(id)))
			{
				Assert.True(!string.Equals(value, "write", StringComparison.Ordinal) || scope is "contents" or "id-token" or "attestations",
					$"Job '{id}' grants '{scope}: write'.");
			}
		}
	}

	[Fact]
	public void Release_is_created_as_a_draft_and_published_only_by_finalize()
	{
		ReleaseWorkflow workflow = ReleaseWorkflow.Load();

		foreach (string id in Ids())
		{
			string run = ReleaseWorkflow.RunText(workflow.Job(id));
			bool creates = run.Contains("gh release create", StringComparison.Ordinal);
			bool edits = run.Contains("gh release edit", StringComparison.Ordinal);
			bool clobbers = run.Contains("--clobber", StringComparison.Ordinal) && run.Contains("gh release upload", StringComparison.Ordinal);
			bool publishes = run.Contains("--draft=false", StringComparison.Ordinal);
			Assert.True(!creates || id is "draft-release", $"Job '{id}' creates a release; only 'draft-release' may.");
			Assert.True(!edits || id is "finalize-release", $"Job '{id}' edits a release; only 'finalize-release' may.");
			Assert.True(!publishes || id is "finalize-release", $"Job '{id}' publishes the release; only 'finalize-release' may.");
			Assert.True(!clobbers || id is "draft-release" or "finalize-release", $"Job '{id}' replaces a release asset.");
			Assert.DoesNotContain("gh release delete", run, StringComparison.Ordinal);
		}

		string draft = ReleaseWorkflow.RunText(workflow.Job("draft-release"));
		Assert.Contains("gh release create $env:TAG @options @files", draft, StringComparison.Ordinal);
		Assert.Contains("@('--draft', '--verify-tag'", draft, StringComparison.Ordinal);
		Assert.Contains("Get-ReleaseAssetPlan", draft, StringComparison.Ordinal);

		string finalize = ReleaseWorkflow.RunText(workflow.Job("finalize-release"));
		Assert.Contains("gh release edit $env:TAG --draft=false", finalize, StringComparison.Ordinal);
		Assert.Contains("gh release verify $env:TAG", finalize, StringComparison.Ordinal);
		Assert.Contains("gh release verify-asset $env:TAG", finalize, StringComparison.Ordinal);
		Assert.Contains("'Published'", finalize, StringComparison.Ordinal);

		// The replaced assets are the tuple and its bundle, on a draft: the step runs only while the release is a draft.
		YamlMappingNode publishStep = Assert.Single(ReleaseWorkflow.Steps(workflow.Job("finalize-release")),
			static step => (ReleaseWorkflow.Scalar(step, "run") ?? "").Contains("--draft=false", StringComparison.Ordinal));
		Assert.Equal("steps.state.outputs.draft == 'true'", ReleaseWorkflow.Normalize(ReleaseWorkflow.Scalar(publishStep, "if")));
		Assert.Contains("gh release upload $env:TAG $env:TUPLE $bundle --clobber", ReleaseWorkflow.Scalar(publishStep, "run"),
			StringComparison.Ordinal);
	}

	[Fact]
	public void Publication_is_verified_on_nuget_org_before_the_release_is_published()
	{
		ReleaseWorkflow workflow = ReleaseWorkflow.Load();

		string verification = ReleaseWorkflow.RunText(workflow.Job("verify-publication"));
		Assert.Contains("./eng/release/Test-PublishedPackage.ps1", verification, StringComparison.Ordinal);

		// The published tuple takes the nuget.org identities from that job's outputs, never from a workflow input.
		YamlMappingNode finalize = workflow.Job("finalize-release");
		YamlMappingNode tuple = Assert.Single(ReleaseWorkflow.Steps(finalize),
			static step => (ReleaseWorkflow.Scalar(step, "run") ?? "").Contains("New-ReleaseTuple.ps1", StringComparison.Ordinal));
		YamlMappingNode env = ReleaseWorkflow.Mapping(tuple, "env")!;
		Assert.Equal("${{ needs.verify-publication.outputs.signed-sha256 }}", ReleaseWorkflow.Scalar(env, "SIGNED_SHA256"));
		Assert.Equal("${{ needs.verify-publication.outputs.signed-sha512 }}", ReleaseWorkflow.Scalar(env, "SIGNED_SHA512"));

		// The publish job pushes only the file SHA256SUMS of the draft lists.
		string publish = ReleaseWorkflow.RunText(workflow.Job("publish"));
		Assert.True(publish.IndexOf("SHA256SUMS", StringComparison.Ordinal) < publish.IndexOf("dotnet nuget push", StringComparison.Ordinal),
			"The publish job must check the package against SHA256SUMS before pushing it.");
	}

	[Fact]
	public void Release_calls_ci_with_the_tag_version_ninety_day_retention_and_no_sonar()
	{
		YamlMappingNode ci = ReleaseWorkflow.Load().Job("ci");

		Assert.Equal("./.github/workflows/ci.yml", ReleaseWorkflow.Scalar(ci, "uses"));
		YamlMappingNode with = ReleaseWorkflow.Mapping(ci, "with")!;
		Assert.Equal(["package-version", "package-retention-days"], ReleaseWorkflow.Keys(with));
		Assert.Equal("${{ needs.verify.outputs.version }}", ReleaseWorkflow.Scalar(with, "package-version"));
		Assert.Equal("90", ReleaseWorkflow.Scalar(with, "package-retention-days"));
		Assert.Null(ReleaseWorkflow.Mapping(ci, "secrets"));
		Assert.Null(ReleaseWorkflow.Scalar(ci, "secrets"));
	}

	[Fact]
	public void Release_jobs_have_a_timeout_and_a_pinned_runner()
	{
		ReleaseWorkflow workflow = ReleaseWorkflow.Load();

		foreach (string id in Ids())
		{
			YamlMappingNode job = workflow.Job(id);
			if (ReleaseWorkflow.Scalar(job, "uses") is not null)
			{
				continue; // GitHub rejects runs-on and timeout-minutes on a reusable-workflow call.
			}

			string? runsOn = ReleaseWorkflow.Scalar(job, "runs-on");
			Assert.True(runsOn is not null && s_runnerLabels.Contains(runsOn), $"Job '{id}' runs on '{runsOn}'.");
			Assert.True(int.TryParse(ReleaseWorkflow.Scalar(job, "timeout-minutes"), NumberStyles.None, CultureInfo.InvariantCulture, out int minutes)
						&& minutes is > 0 and <= 60,
				$"Job '{id}' needs a timeout-minutes between 1 and 60.");
		}
	}

	[Fact]
	public void Release_uploads_only_reserved_artifact_names()
	{
		ReleaseWorkflow workflow = ReleaseWorkflow.Load();
		List<string> uploaded = [];

		foreach (string id in Ids())
		{
			foreach (YamlMappingNode step in ReleaseWorkflow.StepsUsing(workflow.Job(id), "actions/upload-artifact@"))
			{
				uploaded.Add(ReleaseWorkflow.With(step, "name")!);
				Assert.Equal("90", ReleaseWorkflow.With(step, "retention-days"));
				Assert.Equal("error", ReleaseWorkflow.With(step, "if-no-files-found"));
			}
		}

		// The nupkg travels only in ci.yml's nuget-package artifact: attestation-bundles never carries a second copy.
		Assert.Equal(["release-notes", "attestation-bundles"], uploaded);
		YamlMappingNode bundles = Assert.Single(ReleaseWorkflow.StepsUsing(workflow.Job("attest"), "actions/upload-artifact@"));
		Assert.Contains("!artifacts/release/*.nupkg", ReleaseWorkflow.With(bundles, "path"), StringComparison.Ordinal);
	}

	[Fact]
	public void Release_jobs_that_run_dotnet_install_the_pinned_sdk_and_never_cache_packages()
	{
		ReleaseWorkflow workflow = ReleaseWorkflow.Load();

		foreach (string id in Ids())
		{
			YamlMappingNode job = workflow.Job(id);
			Assert.Empty(ReleaseWorkflow.StepsUsing(job, "actions/cache"));
			foreach (YamlMappingNode setup in ReleaseWorkflow.StepsUsing(job, "./.github/actions/setup-dotnet"))
			{
				Assert.True(ReleaseWorkflow.With(setup, "cache") is null or "false", $"Job '{id}' enables a package cache.");
			}

			bool needsDotnet = id is "publish" or "verify-publication";
			Assert.Equal(needsDotnet, ReleaseWorkflow.StepsUsing(job, "./.github/actions/setup-dotnet").Count == 1);
		}
	}

	private static string[] Ids()
	{
		string[] ids = new string[s_chain.Length];
		for (int index = 0; index < s_chain.Length; index++)
		{
			ids[index] = s_chain[index].Id;
		}

		return ids;
	}

	private static void AssertWriteScope(string scope, string[] allowed)
	{
		ReleaseWorkflow workflow = ReleaseWorkflow.Load();
		List<string> granted = [];
		foreach (string id in Ids())
		{
			if (ReleaseWorkflow.Permissions(workflow.Job(id)).TryGetValue(scope, out string? value)
				&& string.Equals(value, "write", StringComparison.Ordinal))
			{
				granted.Add(id);
			}
		}

		Assert.Equal(allowed, granted, StringComparer.Ordinal);
	}
}
