using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Release;

/// <summary>
///     The release tuple reads the bridge fingerprint from the packed DLL bytes on a Linux runner, without loading or
///     parsing the PE image (<c>Get-BridgeSourceFingerprint</c> in <c>eng/release/ReleaseTools.psm1</c>). That byte scan must
///     find exactly the value the DLL exports, on the committed bridge, and refuse bytes without a single fingerprint.
/// </summary>
public sealed class BridgeFingerprintExtractionTests
{
	private const string BridgePath = "native/cheatengine-sdk-lua-bridge/runtimes/win-x64/native/cheatengine-sdk-lua-bridge.dll";

	[Fact]
	public async Task Script_reads_the_same_fingerprint_as_the_pe_export()
	{
		byte[] committed = await CommittedFile.ReadBytesAsync(BridgePath);
		DirectoryInfo scratch = Directory.CreateTempSubdirectory("cheatengine-sdk-fingerprint-");
		try
		{
			string path = Path.Combine(scratch.FullName, "cheatengine-sdk-lua-bridge.dll");
			await File.WriteAllBytesAsync(path, committed, TestContext.Current.CancellationToken);
			string exported = PortableExecutableInspector.Read(path)
				.ReadExportedAsciiZ("cheatengine_sdk_lua_bridge_source_fingerprint");

			ProcessResult run = await PowerShellScript.RunWithReleaseToolsAsync(
				$"Get-BridgeSourceFingerprint -Bytes ([IO.File]::ReadAllBytes({PowerShellScript.Literal(path)}))");

			Assert.True(run.ExitCode == 0, run.CombinedOutput);
			Assert.Equal(exported, run.StandardOutput.Trim());
			Assert.Matches("^[0-9a-f]{64}:[0-9a-f]{64}$", exported);
		}
		finally
		{
			scratch.Delete(true);
		}
	}

	[Fact]
	public async Task Bytes_without_exactly_one_fingerprint_are_refused()
	{
		string fingerprint = new string('a', 64) + ":" + new string('b', 64);

		ProcessResult none = await PowerShellScript.RunWithReleaseToolsAsync(
			"Get-BridgeSourceFingerprint -Bytes ([Text.Encoding]::ASCII.GetBytes('MZ no fingerprint'))");
		ProcessResult two = await PowerShellScript.RunWithReleaseToolsAsync(
			$"Get-BridgeSourceFingerprint -Bytes ([Text.Encoding]::ASCII.GetBytes('{fingerprint} {fingerprint}'))");

		Assert.NotEqual(0, none.ExitCode);
		Assert.Contains("found 0", none.CombinedOutput, StringComparison.Ordinal);
		Assert.NotEqual(0, two.ExitCode);
		Assert.Contains("found 2", two.CombinedOutput, StringComparison.Ordinal);
	}
}
