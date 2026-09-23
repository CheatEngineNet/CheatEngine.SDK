using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Release;

/// <summary>
///     <c>eng/release/Export-PackageSbom.ps1</c> hands the release exactly the SPDX 2.2 document embedded in the package:
///     byte for byte (it is attested as is), and never when the entry is missing or is another SPDX version.
/// </summary>
public sealed class PackageSbomScriptTests
{
	private const string Script = "eng/release/Export-PackageSbom.ps1";

	[Fact]
	public async Task Exported_sbom_is_byte_identical_to_the_package_entry()
	{
		using SyntheticRelease release = SyntheticRelease.Create(withBundles: false);
		string output = Path.Combine(release.AssetsDirectory, "exported.spdx.json");

		ProcessResult run = await PowerShellScript.RunFileAsync(Script, "-PackagePath", release.PackagePath, "-OutputPath",
			output);

		Assert.True(run.ExitCode == 0, run.CombinedOutput);
		Assert.Equal(release.SbomBytes, File.ReadAllBytes(output));
		Assert.Contains(SyntheticRelease.Sha256Hex(release.SbomBytes), run.StandardOutput, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Export_fails_when_the_package_has_no_spdx_2_2_manifest()
	{
		using SyntheticRelease release = SyntheticRelease.Create(withBundles: false);
		string output = Path.Combine(release.AssetsDirectory, "exported.spdx.json");

		release.WritePackage(includeSbom: false, spdxVersion: "SPDX-2.2");
		ProcessResult absent = await PowerShellScript.RunFileAsync(Script, "-PackagePath", release.PackagePath, "-OutputPath",
			output);
		release.WritePackage(includeSbom: true, spdxVersion: "SPDX-2.3");
		ProcessResult otherVersion = await PowerShellScript.RunFileAsync(Script, "-PackagePath", release.PackagePath,
			"-OutputPath", output);

		Assert.NotEqual(0, absent.ExitCode);
		Assert.Contains("embeds no SPDX 2.2 SBOM", absent.CombinedOutput, StringComparison.Ordinal);
		Assert.NotEqual(0, otherVersion.ExitCode);
		Assert.Contains("declares spdxVersion 'SPDX-2.3', expected SPDX-2.2", otherVersion.CombinedOutput,
			StringComparison.Ordinal);
		Assert.False(File.Exists(output));
	}
}
