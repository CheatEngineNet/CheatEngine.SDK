using System.Text.Json.Nodes;

namespace CheatEngine.SDK.Tests.Release;

/// <summary>
///     <see cref="ReleaseTupleValidator" /> is the oracle of every tuple test, so it is shown to reject each class of
///     defect on an otherwise valid in-memory tuple: an unknown property, a mixed stage, a local path, an asset list that
///     does not name the attested package, a tag of another version.
/// </summary>
public sealed class ReleaseTupleValidatorTests
{
	private const string Sha256 = "889dc4c231d182f9b7baa9e29880555aad949f42023dde232fe327542c3c5387";
	private const string Sha512 = "n7nHqZ8vzo7Vf20jF0fkh/jUtR3yo1TwRGpXE7ERxZeJ4C5S/Nsft4lqOg7zGwfsD5Nh9tTVgdw4PrybJRF0gA==";

	[Fact]
	public void A_complete_pre_publish_tuple_is_valid()
	{
		Assert.Empty(Validate(ValidTuple()));
	}

	[Fact]
	public void An_unknown_or_missing_property_is_rejected()
	{
		JsonObject extra = ValidTuple();
		extra["package"]!["downloadUrl"] = "https://example.invalid/p.nupkg";
		JsonObject missing = ValidTuple();
		missing["source"]!.AsObject().Remove("treeHash");

		Assert.Contains("'package' has the unknown property 'downloadUrl'.", Validate(extra), StringComparer.Ordinal);
		Assert.Contains("'source' has no 'treeHash'.", Validate(missing), StringComparer.Ordinal);
	}

	[Fact]
	public void Stage_rules_reject_publication_fields_before_publication_and_their_absence_after()
	{
		JsonObject early = ValidTuple();
		early["package"]!["nugetOrgSignedSha256"] = Sha256;
		JsonObject late = ValidTuple();
		late["stage"] = "Published";

		Assert.Contains("A PrePublish tuple has no package.nugetOrgSignedSha256.", Validate(early), StringComparer.Ordinal);
		List<string> lateErrors = Validate(late);
		Assert.Contains("A Published tuple needs package.nugetOrgSignedSha512.", lateErrors, StringComparer.Ordinal);
		Assert.Contains("A Published tuple needs package.repositorySignatureVerified = true.", lateErrors, StringComparer.Ordinal);
	}

	[Fact]
	public void An_absolute_local_path_anywhere_is_rejected()
	{
		JsonObject tuple = ValidTuple();
		tuple["build"]!["runner"]!["imageVersion"] = @"C:\Users\runneradmin\image";

		Assert.Contains(Validate(tuple), static e => e.StartsWith("'build.runner.imageVersion' contains an absolute local path",
			StringComparison.Ordinal));
	}

	[Fact]
	public void Assets_must_name_the_attested_package_and_the_tag_its_version()
	{
		JsonObject tuple = ValidTuple();
		tuple["assets"]![0]!["sha256"] = new string('0', 64);
		tuple["source"]!["tag"] = "v9.9.9";

		List<string> errors = Validate(tuple);
		Assert.Contains("assets must list CheatEngine.SDK.2.0.0.nupkg with package.attestedAssetSha256.", errors, StringComparer.Ordinal);
		Assert.Contains("source.tag v9.9.9 does not name version 2.0.0.", errors, StringComparer.Ordinal);
	}

	private static List<string> Validate(JsonObject tuple)
	{
		using JsonDocument document = JsonDocument.Parse(tuple.ToJsonString());
		return ReleaseTupleValidator.Validate(document.RootElement);
	}

	private static JsonObject ValidTuple()
	{
		return JsonNode.Parse($$"""
		                        {
		                          "schema": "cheatengine-release-tuple/v0",
		                          "stage": "PrePublish",
		                          "package": {
		                            "id": "CheatEngine.SDK", "version": "2.0.0", "attestedAssetSha256": "{{Sha256}}",
		                            "contentHashSha512": "{{Sha512}}", "nugetOrgSignedSha256": null,
		                            "nugetOrgSignedSha512": null, "repositorySignatureVerified": null
		                          },
		                          "source": {
		                            "repository": "CheatEngineNet/CheatEngine.SDK", "tag": "v2.0.0",
		                            "commit": "a6fefb93e9c6f85a1bcedb68bf97e6741175b227",
		                            "treeHash": "41678f939547b2215e106ee3bbc8c2878815652e", "pullRequest": null,
		                            "releaseRunUrl": "https://github.com/CheatEngineNet/CheatEngine.SDK/actions/runs/1",
		                            "ciRunUrl": "https://github.com/CheatEngineNet/CheatEngine.SDK/actions/runs/1"
		                          },
		                          "build": {
		                            "dotnetSdk": "10.0.401",
		                            "runner": { "label": "windows-2025", "imageOs": "win25", "imageVersion": "20260915.1.0" },
		                            "toolchain": { "xmake": "3.0.9", "msvcToolset": "14.44", "msvcVersion": "14.44.35207", "windowsSdk": "10.0.26100.0" },
		                            "roslynFloor": "5.9.0", "analysisLevel": "10.0-recommended"
		                          },
		                          "nativeBridge": {
		                            "packagePath": "build/native/cheatengine-sdk-lua-bridge.dll", "sha256": "{{Sha256}}",
		                            "sourceFingerprint": "{{Sha256}}:{{Sha256}}"
		                          },
		                          "ceProfile": { "profileId": "ce-7.7.0.10621-x64-managed-hostfxr", "supportProfileSha256": null },
		                          "qualification": { "matrixSha256": null, "receipts": [] },
		                          "sbom": { "entry": "_manifest/spdx_2.2/manifest.spdx.json", "sha256": "{{Sha256}}", "spdxVersion": "SPDX-2.2", "attested": false },
		                          "attestations": { "provenanceBundle": null, "sbomBundle": null },
		                          "assets": [
		                            { "name": "CheatEngine.SDK.2.0.0.nupkg", "sha256": "{{Sha256}}" },
		                            { "name": "CheatEngine.SDK.2.0.0.spdx.json", "sha256": "{{Sha256}}" }
		                          ],
		                          "createdUtc": "2026-09-23T00:00:00Z"
		                        }
		                        """)!.AsObject();
	}
}
