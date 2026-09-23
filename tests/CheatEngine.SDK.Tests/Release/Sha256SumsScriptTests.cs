using System.Security.Cryptography;
using System.Text;

using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Release;

/// <summary>
///     <c>eng/release/New-Sha256Sums.ps1</c> writes the <c>SHA256SUMS</c> release asset in the exact form that
///     <c>sha256sum -c</c> reads: one lowercase hash, two spaces and the name per asset, ordinal order, LF, final newline,
///     no BOM; and it refuses an asset named twice or outside its folder.
/// </summary>
public sealed class Sha256SumsScriptTests : IDisposable
{
	private const string Script = "eng/release/New-Sha256Sums.ps1";
	private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("cheatengine-sdk-sums-");

	/// <inheritdoc />
	public void Dispose()
	{
		_directory.Delete(true);
	}

	[Fact]
	public async Task Sums_file_lists_each_asset_once_sorted_with_lowercase_hashes_and_lf_endings()
	{
		Dictionary<string, byte[]> assets = new(StringComparer.Ordinal)
		{
			["c.tuple.json"] = "{}\n"u8.ToArray(),
			["A.nupkg"] = [0x50, 0x4B, 0x03, 0x04],
			["b.spdx.json"] = "{\"spdxVersion\":\"SPDX-2.2\"}"u8.ToArray()
		};
		foreach ((string name, byte[] content) in assets)
		{
			await File.WriteAllBytesAsync(Path.Combine(_directory.FullName, name), content,
				TestContext.Current.CancellationToken);
		}

		string output = Path.Combine(_directory.FullName, "SHA256SUMS");
		ProcessResult run = await PowerShellScript.RunFileAsync(Script, "-Directory", _directory.FullName, "-Name",
			"c.tuple.json,A.nupkg, b.spdx.json", "-OutputPath", output);

		Assert.True(run.ExitCode == 0, run.CombinedOutput);
		byte[] written = await File.ReadAllBytesAsync(output, TestContext.Current.CancellationToken);
		string expected = string.Concat(
			Line(assets, "A.nupkg"), Line(assets, "b.spdx.json"), Line(assets, "c.tuple.json"));
		Assert.Equal(expected, Encoding.UTF8.GetString(written));
		Assert.False(written.AsSpan().StartsWith((ReadOnlySpan<byte>) [0xEF, 0xBB, 0xBF]), "SHA256SUMS starts with a BOM.");
		Assert.DoesNotContain((byte) '\r', written);
	}

	[Fact]
	public async Task An_asset_named_twice_or_outside_the_folder_is_refused()
	{
		await File.WriteAllTextAsync(Path.Combine(_directory.FullName, "a.nupkg"), "a", TestContext.Current.CancellationToken);
		string output = Path.Combine(_directory.FullName, "SHA256SUMS");

		ProcessResult twice = await PowerShellScript.RunFileAsync(Script, "-Directory", _directory.FullName, "-Name",
			"a.nupkg,a.nupkg", "-OutputPath", output);
		ProcessResult outside = await PowerShellScript.RunFileAsync(Script, "-Directory", _directory.FullName, "-Name",
			"../a.nupkg", "-OutputPath", output);

		Assert.NotEqual(0, twice.ExitCode);
		Assert.Contains("'a.nupkg' is named twice", twice.CombinedOutput, StringComparison.Ordinal);
		Assert.NotEqual(0, outside.ExitCode);
		Assert.Contains("is not a plain file name", outside.CombinedOutput, StringComparison.Ordinal);
		Assert.False(File.Exists(output));
	}

	private static string Line(Dictionary<string, byte[]> assets, string name)
	{
		return $"{Convert.ToHexStringLower(SHA256.HashData(assets[name]))}  {name}\n";
	}
}
