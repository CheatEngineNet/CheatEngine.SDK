using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using CheatEngine.SDK.Tests.Infrastructure;
using CheatEngine.SDK.Tests.Release;

namespace CheatEngine.SDK.Tests.Packaging;

/// <summary>
///     The release scripts run on the package under test, as the release workflow runs them on the attested file: export
///     its embedded SBOM, write <c>SHA256SUMS</c>, then write a PrePublish tuple from a build-info derived from that
///     package. In the CI Release leg this is the exact <c>nuget-package</c> file, so a package the tooling cannot describe
///     fails before any release starts.
/// </summary>
[Collection(PackagedUmbrellaSuite.Name)]
[Trait("Category", UmbrellaPackage.PackagingCategory)]
public sealed partial class ReleaseTupleTests(PackagedUmbrellaFixture fixture)
{
	private const string BridgeEntry = "build/native/cheatengine-sdk-lua-bridge.dll";
	private const string SbomEntry = "_manifest/spdx_2.2/manifest.spdx.json";

	[Fact]
	public async Task Pre_publish_tuple_of_the_package_under_test_is_valid()
	{
		DirectoryInfo scratch = Directory.CreateTempSubdirectory("cheatengine-sdk-release-tuple-");
		try
		{
			string packageFile = Path.GetFileName(fixture.PackagePath);
			string release = Path.Combine(scratch.FullName, "release");
			Directory.CreateDirectory(release);
			string package = Path.Combine(release, packageFile);
			File.Copy(fixture.PackagePath, package);
			string sbomFile = $"{UmbrellaPackage.Id}.{fixture.PackageVersion}.spdx.json";

			ProcessResult export = await PowerShellScript.RunFileAsync("eng/release/Export-PackageSbom.ps1", "-PackagePath",
				package, "-OutputPath", Path.Combine(release, sbomFile));
			Assert.True(export.ExitCode == 0, export.CombinedOutput);
			ProcessResult sums = await PowerShellScript.RunFileAsync("eng/release/New-Sha256Sums.ps1", "-Directory", release,
				"-Name", $"{packageFile},{sbomFile}", "-OutputPath", Path.Combine(release, "SHA256SUMS"));
			Assert.True(sums.ExitCode == 0, sums.CombinedOutput);

			string buildInfo = Path.Combine(scratch.FullName, "build-info.json");
			await File.WriteAllTextAsync(buildInfo, await BuildInfoAsync(packageFile), TestContext.Current.CancellationToken);
			string output = Path.Combine(release, $"{UmbrellaPackage.Id}.{fixture.PackageVersion}.tuple.json");
			ProcessResult tuple = await PowerShellScript.RunFileAsync("eng/release/New-ReleaseTuple.ps1", "-Stage",
				"PrePublish", "-PackagePath", package, "-BuildInfoPath", buildInfo, "-AssetsDirectory", release, "-OutputPath",
				output);
			Assert.True(tuple.ExitCode == 0, tuple.CombinedOutput);

			using JsonDocument document = JsonDocument.Parse(await File.ReadAllBytesAsync(output,
				TestContext.Current.CancellationToken));
			List<string> errors = ReleaseTupleValidator.Validate(document.RootElement);
			Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
			JsonElement described = document.RootElement.GetProperty("package");
			Assert.Equal(fixture.PackageVersion, described.GetProperty("version").GetString());
			Assert.Equal(fixture.PackageSha256, described.GetProperty("attestedAssetSha256").GetString());
			Assert.Equal(fixture.PackageSha512Base64, described.GetProperty("contentHashSha512").GetString());
			Assert.Equal(SyntheticRelease.Sha256Hex(NupkgInspector.ReadEntryBytes(fixture.PackagePath, SbomEntry)),
				document.RootElement.GetProperty("sbom").GetProperty("sha256").GetString());
		}
		finally
		{
			scratch.Delete(true);
		}
	}

	/// <summary>A v0 build-info of the CI run that would have packed this package.</summary>
	private async Task<string> BuildInfoAsync(string packageFile)
	{
		byte[] bridge = NupkgInspector.ReadEntryBytes(fixture.PackagePath, BridgeEntry);
		string fingerprint = Assert.Single(Fingerprint().Matches(System.Text.Encoding.Latin1.GetString(bridge))).Value;
		XNamespace ns = fixture.Nuspec.Root!.GetDefaultNamespace();
		string commit = (string?) fixture.Nuspec.Descendants(ns + "repository").Single().Attribute("commit") ?? "";
		ProcessResult tree = await ProcessRunner.RunAsync("git", "rev-parse HEAD^{tree}", RepositoryLayout.Root,
			TimeSpan.FromMinutes(1));
		Assert.True(tree.ExitCode == 0, tree.CombinedOutput);

		JsonObject info = new()
		{
			["schema"] = "cheatengine-build-info/v0",
			["repository"] = "CheatEngineNet/CheatEngine.SDK",
			["commit"] = commit,
			["treeHash"] = tree.StandardOutput.Trim(),
			["ref"] = "refs/heads/main",
			["event"] = "workflow_dispatch",
			["runId"] = 1,
			["runAttempt"] = 1,
			["runUrl"] = "https://github.com/CheatEngineNet/CheatEngine.SDK/actions/runs/1",
			["pullRequest"] = null,
			["dotnetSdk"] = "10.0.401",
			["globalJsonSha256"] = SyntheticRelease.Sha256Hex(File.ReadAllBytes(RepositoryLayout.PathOf("global.json"))),
			["runner"] = new JsonObject { ["label"] = "windows-2025", ["imageOs"] = "local", ["imageVersion"] = "local" },
			["toolchain"] = new JsonObject
			{
				["xmake"] = "3.0.9",
				["msvcToolset"] = "unknown",
				["msvcVersion"] = "unknown",
				["windowsSdk"] = "unknown"
			},
			["nativeBridge"] = new JsonObject
			{
				["sha256"] = SyntheticRelease.Sha256Hex(bridge),
				["checkedInSha256"] = SyntheticRelease.Sha256Hex(bridge),
				["sourceFingerprint"] = fingerprint,
				["driftFromCheckedIn"] = false
			},
			["packages"] = new JsonArray(new JsonObject
			{
				["id"] = UmbrellaPackage.Id,
				["version"] = fixture.PackageVersion,
				["file"] = packageFile,
				["sha256"] = fixture.PackageSha256,
				["sha512"] = fixture.PackageSha512Base64,
				["sbomEntry"] = SbomEntry
			}),
			["createdUtc"] = "2026-09-23T00:00:00Z"
		};
		return info.ToJsonString();
	}

	[GeneratedRegex("[0-9a-f]{64}:[0-9a-f]{64}", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex Fingerprint();
}
