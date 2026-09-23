using System.Text.Json.Nodes;

using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Release;

/// <summary>
///     <c>eng/release/New-ReleaseTuple.ps1</c> on synthetic inputs (<see cref="SyntheticRelease" />): the tuple mirrors the
///     package, the bridge it carries and the build-info of the run that packed it; it refuses a build-info that describes
///     another package or another bridge; it reads qualification evidence from committed files only and tolerates their
///     absence; a Published tuple needs the nuget.org identities and both attestation bundles. No trait: both CI legs run it.
/// </summary>
public sealed class ReleaseTupleScriptTests
{
	[Fact]
	public async Task Pre_publish_tuple_is_valid_and_mirrors_the_package_and_build_info()
	{
		using SyntheticRelease release = SyntheticRelease.Create();
		release.AddQualificationDocuments();

		ProcessResult run = await release.RunTupleScriptAsync("PrePublish", "-Tag", SyntheticRelease.Tag,
			"-ProvenanceBundle", SyntheticRelease.ProvenanceBundle, "-SbomBundle", SyntheticRelease.SbomBundle,
			"-PullRequestNumber", "142", "-PullRequestHeadSha", SyntheticRelease.TreeHash);

		Assert.True(run.ExitCode == 0, run.CombinedOutput);
		byte[] written = File.ReadAllBytes(release.OutputPath);
		Assert.False(written.AsSpan().StartsWith((ReadOnlySpan<byte>) [0xEF, 0xBB, 0xBF]), "The tuple starts with a BOM.");
		Assert.DoesNotContain((byte) '\r', written);
		Assert.Equal((byte) '\n', written[^1]);

		using JsonDocument document = release.ReadTuple();
		JsonElement tuple = document.RootElement;
		AssertValid(tuple);
		Assert.Equal("PrePublish", tuple.GetProperty("stage").GetString());
		JsonElement package = tuple.GetProperty("package");
		Assert.Equal(SyntheticRelease.Version, package.GetProperty("version").GetString());
		Assert.Equal(release.PackageSha256, package.GetProperty("attestedAssetSha256").GetString());
		Assert.Equal(release.PackageSha512, package.GetProperty("contentHashSha512").GetString());
		Assert.Equal(JsonValueKind.Null, package.GetProperty("nugetOrgSignedSha256").ValueKind);
		Assert.Equal(JsonValueKind.Null, package.GetProperty("repositorySignatureVerified").ValueKind);

		JsonElement source = tuple.GetProperty("source");
		Assert.Equal(SyntheticRelease.Tag, source.GetProperty("tag").GetString());
		Assert.Equal(SyntheticRelease.Commit, source.GetProperty("commit").GetString());
		Assert.Equal(SyntheticRelease.TreeHash, source.GetProperty("treeHash").GetString());
		Assert.Equal(142, source.GetProperty("pullRequest").GetProperty("number").GetInt32());
		Assert.Equal(SyntheticRelease.RunUrl, source.GetProperty("ciRunUrl").GetString());

		JsonElement build = tuple.GetProperty("build");
		Assert.Equal("windows-2025", build.GetProperty("runner").GetProperty("label").GetString());
		Assert.Equal("14.44.35207", build.GetProperty("toolchain").GetProperty("msvcVersion").GetString());
		Assert.Equal("5.9.0", build.GetProperty("roslynFloor").GetString());
		Assert.Equal("10.0-recommended", build.GetProperty("analysisLevel").GetString());

		Assert.Equal(release.BridgeSha256, tuple.GetProperty("nativeBridge").GetProperty("sha256").GetString());
		Assert.Equal(release.BridgeFingerprint, tuple.GetProperty("nativeBridge").GetProperty("sourceFingerprint").GetString());
		Assert.Equal(SyntheticRelease.Sha256Hex(release.SbomBytes), tuple.GetProperty("sbom").GetProperty("sha256").GetString());
		Assert.True(tuple.GetProperty("sbom").GetProperty("attested").GetBoolean());
		Assert.Equal(SyntheticRelease.QualifiableProfile, tuple.GetProperty("ceProfile").GetProperty("profileId").GetString());

		string sums = File.ReadAllText(Path.Combine(release.AssetsDirectory, "SHA256SUMS"));
		List<string> expectedAssets = [.. sums.TrimEnd('\n').Split('\n').Select(static line => line[66..] + "=" + line[..64])];
		List<string> actualAssets =
		[
			.. tuple.GetProperty("assets").EnumerateArray()
				.Select(static a => a.GetProperty("name").GetString() + "=" + a.GetProperty("sha256").GetString())
		];
		Assert.Equal(expectedAssets, actualAssets);
	}

	[Fact]
	public async Task Tuple_generation_fails_when_build_info_names_another_package()
	{
		using SyntheticRelease release = SyntheticRelease.Create();
		release.WriteBuildInfo(static info => info["packages"]![0]!["sha256"] = new string('0', 64));

		ProcessResult run = await release.RunTupleScriptAsync("PrePublish");

		Assert.NotEqual(0, run.ExitCode);
		Assert.Contains("build-info names another package", run.CombinedOutput, StringComparison.Ordinal);
		Assert.False(File.Exists(release.OutputPath));
	}

	[Fact]
	public async Task Tuple_generation_fails_when_the_packed_bridge_fingerprint_differs_from_build_info()
	{
		using SyntheticRelease release = SyntheticRelease.Create();
		string otherFingerprint = new string('1', 64) + ":" + new string('2', 64);
		release.WriteBuildInfo(info => info["nativeBridge"]!["sourceFingerprint"] = otherFingerprint);

		ProcessResult run = await release.RunTupleScriptAsync("PrePublish");

		Assert.NotEqual(0, run.ExitCode);
		Assert.Contains($"embeds fingerprint {release.BridgeFingerprint}", run.CombinedOutput, StringComparison.Ordinal);
		Assert.Contains(otherFingerprint, run.CombinedOutput, StringComparison.Ordinal);
		Assert.False(File.Exists(release.OutputPath));
	}

	[Fact]
	public async Task Tuple_generation_reports_a_packed_bridge_that_differs_from_the_audited_one_without_failing()
	{
		using SyntheticRelease release = SyntheticRelease.Create(withBundles: false);
		string manifestPath = Path.Combine(release.RepositoryRoot, "native", "cheatengine-sdk-lua-bridge",
			"bridge-audit-manifest.json");
		JsonNode manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
		manifest["nativeAsset"]!["sha256"] = new string('e', 64);
		File.WriteAllText(manifestPath, manifest.ToJsonString());

		ProcessResult run = await release.RunTupleScriptAsync("PrePublish");

		Assert.True(run.ExitCode == 0, run.CombinedOutput);
		Assert.Contains($"::notice title=Native bridge drift::packed bridge {release.BridgeSha256} differs from the " +
						$"committed, audited bridge {new string('e', 64)}", run.CombinedOutput, StringComparison.Ordinal);
		using JsonDocument document = release.ReadTuple();
		AssertValid(document.RootElement);
	}

	[Fact]
	public async Task Tuple_without_qualification_files_records_null_hashes_and_no_receipts()
	{
		using SyntheticRelease release = SyntheticRelease.Create(withBundles: false);

		ProcessResult run = await release.RunTupleScriptAsync("PrePublish");

		Assert.True(run.ExitCode == 0, run.CombinedOutput);
		Assert.Contains("::warning::docs/qualification/support-profile.json is absent", run.CombinedOutput,
			StringComparison.Ordinal);
		using JsonDocument document = release.ReadTuple();
		JsonElement tuple = document.RootElement;
		AssertValid(tuple);
		Assert.Equal(SyntheticRelease.QualifiableProfile, tuple.GetProperty("ceProfile").GetProperty("profileId").GetString());
		Assert.Equal(JsonValueKind.Null, tuple.GetProperty("ceProfile").GetProperty("supportProfileSha256").ValueKind);
		Assert.Equal(JsonValueKind.Null, tuple.GetProperty("qualification").GetProperty("matrixSha256").ValueKind);
		Assert.Equal(0, tuple.GetProperty("qualification").GetProperty("receipts").GetArrayLength());
		Assert.Equal(JsonValueKind.Null, tuple.GetProperty("source").GetProperty("tag").ValueKind);
		Assert.Equal(JsonValueKind.Null, tuple.GetProperty("attestations").GetProperty("sbomBundle").ValueKind);
		Assert.False(tuple.GetProperty("sbom").GetProperty("attested").GetBoolean());
	}

	[Fact]
	public async Task Tuple_lists_committed_receipts_and_ignores_event_logs()
	{
		using SyntheticRelease release = SyntheticRelease.Create();
		release.AddQualificationDocuments();
		string c4 = release.AddReceipt("Q30.a", "R-20260923T101500Z-Q30.a-0123abcd", "C4", "NotApplicable");
		string c3 = release.AddReceipt("Q05", "R-20260923T091500Z-Q05-89abcdef", "C3", "Passed");

		ProcessResult run = await release.RunTupleScriptAsync("PrePublish", "-Tag", SyntheticRelease.Tag,
			"-ProvenanceBundle", SyntheticRelease.ProvenanceBundle, "-SbomBundle", SyntheticRelease.SbomBundle);

		Assert.True(run.ExitCode == 0, run.CombinedOutput);
		using JsonDocument document = release.ReadTuple();
		JsonElement tuple = document.RootElement;
		AssertValid(tuple);
		JsonElement qualification = tuple.GetProperty("qualification");
		string qualificationRoot = Path.Combine(release.RepositoryRoot, "docs", "qualification");
		Assert.Equal(SyntheticRelease.NormalizedSha256(Path.Combine(qualificationRoot, "matrix.json")),
			qualification.GetProperty("matrixSha256").GetString());
		Assert.Equal(SyntheticRelease.NormalizedSha256(Path.Combine(qualificationRoot, "support-profile.json")),
			tuple.GetProperty("ceProfile").GetProperty("supportProfileSha256").GetString());

		JsonElement[] receipts = [.. qualification.GetProperty("receipts").EnumerateArray()];
		Assert.Equal(2, receipts.Length);
		Assert.Equal("R-20260923T091500Z-Q05-89abcdef", receipts[0].GetProperty("receiptId").GetString());
		Assert.Equal("Q05", receipts[0].GetProperty("qualificationId").GetString());
		Assert.Equal("C3", receipts[0].GetProperty("level").GetString());
		Assert.Equal("Passed", receipts[0].GetProperty("status").GetString());
		Assert.Equal(SyntheticRelease.NormalizedSha256(c3), receipts[0].GetProperty("sha256").GetString());
		Assert.Equal("R-20260923T101500Z-Q30.a-0123abcd", receipts[1].GetProperty("receiptId").GetString());
		Assert.Equal("NotApplicable", receipts[1].GetProperty("status").GetString());
		Assert.Equal(SyntheticRelease.NormalizedSha256(c4), receipts[1].GetProperty("sha256").GetString());
	}

	[Fact]
	public async Task Published_tuple_requires_the_nuget_org_identities_and_both_bundles()
	{
		using SyntheticRelease release = SyntheticRelease.Create();
		string signedSha256 = new('3', 64);
		const string signedSha512 = "1a2B/E6reX5e636hfdb+Zdj3kT6817DuNES1RWvprhRyuyztE/56Zk2iHOMQIKpGH+O2Va8rYJxXXXTVq5aN9Q==";

		ProcessResult incomplete = await release.RunTupleScriptAsync("Published", "-Tag", SyntheticRelease.Tag,
			"-ProvenanceBundle", SyntheticRelease.ProvenanceBundle, "-SbomBundle", SyntheticRelease.SbomBundle);
		ProcessResult unbundled = await release.RunTupleScriptAsync("Published", "-Tag", SyntheticRelease.Tag,
			"-NuGetOrgSignedSha256", signedSha256, "-NuGetOrgSignedSha512", signedSha512, "-RepositorySignatureVerified");

		Assert.NotEqual(0, incomplete.ExitCode);
		Assert.Contains("A Published tuple requires -NuGetOrgSignedSha256", incomplete.CombinedOutput, StringComparison.Ordinal);
		Assert.Contains("-RepositorySignatureVerified", incomplete.CombinedOutput, StringComparison.Ordinal);
		Assert.NotEqual(0, unbundled.ExitCode);
		Assert.Contains("-ProvenanceBundle and -SbomBundle", unbundled.CombinedOutput, StringComparison.Ordinal);
		Assert.False(File.Exists(release.OutputPath));

		ProcessResult complete = await release.RunTupleScriptAsync("Published", "-Tag", SyntheticRelease.Tag,
			"-ProvenanceBundle", SyntheticRelease.ProvenanceBundle, "-SbomBundle", SyntheticRelease.SbomBundle,
			"-NuGetOrgSignedSha256", signedSha256, "-NuGetOrgSignedSha512", signedSha512, "-RepositorySignatureVerified");

		Assert.True(complete.ExitCode == 0, complete.CombinedOutput);
		using JsonDocument document = release.ReadTuple();
		JsonElement tuple = document.RootElement;
		AssertValid(tuple);
		Assert.Equal("Published", tuple.GetProperty("stage").GetString());
		Assert.Equal(signedSha256, tuple.GetProperty("package").GetProperty("nugetOrgSignedSha256").GetString());
		Assert.Equal(signedSha512, tuple.GetProperty("package").GetProperty("nugetOrgSignedSha512").GetString());
		Assert.True(tuple.GetProperty("package").GetProperty("repositorySignatureVerified").GetBoolean());
		Assert.Equal(SyntheticRelease.ProvenanceBundle,
			tuple.GetProperty("attestations").GetProperty("provenanceBundle").GetString());
	}

	[Fact]
	public async Task Tuple_contains_no_absolute_local_path()
	{
		using SyntheticRelease release = SyntheticRelease.Create();
		release.AddQualificationDocuments();

		ProcessResult run = await release.RunTupleScriptAsync("PrePublish", "-Tag", SyntheticRelease.Tag,
			"-ProvenanceBundle", SyntheticRelease.ProvenanceBundle, "-SbomBundle", SyntheticRelease.SbomBundle);

		Assert.True(run.ExitCode == 0, run.CombinedOutput);
		string text = File.ReadAllText(release.OutputPath);
		Assert.DoesNotContain(release.AssetsDirectory, text, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain(release.RepositoryRoot, text, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), text, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain(":\\", text, StringComparison.Ordinal);
		Assert.DoesNotContain("file://", text, StringComparison.Ordinal);
	}

	private static void AssertValid(JsonElement tuple)
	{
		List<string> errors = ReleaseTupleValidator.Validate(tuple);
		Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
	}
}
