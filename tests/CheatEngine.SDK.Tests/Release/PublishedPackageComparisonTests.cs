using System.IO.Compression;

using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Release;

/// <summary>
///     The pure checks of <c>eng/release/Test-PublishedPackage.ps1</c>, on synthetic archives: nuget.org's repository-signed
///     copy must be exactly the attested package plus <c>.signature.p7s</c>, every common entry byte-identical; and the
///     content hash printed by <c>dotnet nuget verify</c> is recognized by its value, whatever the language of the label
///     around it (the French label was observed on 2026-09-23).
/// </summary>
public sealed class PublishedPackageComparisonTests : IDisposable
{
	private const string ContentHash = "n7nHqZ8vzo7Vf20jF0fkh/jUtR3yo1TwRGpXE7ERxZeJ4C5S/Nsft4lqOg7zGwfsD5Nh9tTVgdw4PrybJRF0gA==";

	private static readonly Dictionary<string, string> s_attestedEntries = new(StringComparer.Ordinal)
	{
		["CheatEngine.SDK.nuspec"] = "<package />",
		["lib/net10.0/CheatEngine.SDK.dll"] = "library",
		["build/native/cheatengine-sdk-lua-bridge.dll"] = "bridge",
		["_manifest/spdx_2.2/manifest.spdx.json"] = "{}"
	};

	private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("cheatengine-sdk-published-");

	/// <inheritdoc />
	public void Dispose()
	{
		_directory.Delete(true);
	}

	[Fact]
	public async Task Signed_copy_with_identical_entries_plus_signature_matches()
	{
		Dictionary<string, string> signed = new(s_attestedEntries, StringComparer.Ordinal)
		{
			[".signature.p7s"] = "sig"
		};

		JsonElement result = await CompareAsync(s_attestedEntries, signed);

		Assert.True(result.GetProperty("IsMatch").GetBoolean(), result.GetRawText());
		Assert.True(result.GetProperty("HasSignature").GetBoolean());
		Assert.Equal(s_attestedEntries.Count, result.GetProperty("Compared").GetInt32());
	}

	[Fact]
	public async Task Signed_copy_with_a_changed_entry_is_rejected()
	{
		Dictionary<string, string> signed = new(s_attestedEntries, StringComparer.Ordinal)
		{
			[".signature.p7s"] = "sig",
			["lib/net10.0/CheatEngine.SDK.dll"] = "another library"
		};

		JsonElement result = await CompareAsync(s_attestedEntries, signed);

		Assert.False(result.GetProperty("IsMatch").GetBoolean());
		Assert.Equal(["lib/net10.0/CheatEngine.SDK.dll"], Strings(result.GetProperty("Changed")));
	}

	[Fact]
	public async Task Signed_copy_missing_an_entry_is_rejected()
	{
		Dictionary<string, string> signed = new(s_attestedEntries, StringComparer.Ordinal)
		{
			[".signature.p7s"] = "sig"
		};
		signed.Remove("build/native/cheatengine-sdk-lua-bridge.dll");
		signed["build/native/other.dll"] = "extra";
		Dictionary<string, string> unsigned = new(s_attestedEntries, StringComparer.Ordinal);

		JsonElement missing = await CompareAsync(s_attestedEntries, signed);
		JsonElement noSignature = await CompareAsync(s_attestedEntries, unsigned);

		Assert.False(missing.GetProperty("IsMatch").GetBoolean());
		Assert.Equal(["build/native/cheatengine-sdk-lua-bridge.dll"], Strings(missing.GetProperty("Missing")));
		Assert.Equal(["build/native/other.dll"], Strings(missing.GetProperty("Unexpected")));
		Assert.False(noSignature.GetProperty("IsMatch").GetBoolean());
		Assert.False(noSignature.GetProperty("HasSignature").GetBoolean());
	}

	[Theory]
	[InlineData("Content hash: " + ContentHash, true)]
	[InlineData("Hachage du contenu : " + ContentHash, true)]
	[InlineData("  Inhaltshash: " + ContentHash + "\r\nSignature type: Repository", true)]
	[InlineData("Content hash: X" + ContentHash, false)]
	[InlineData("Content hash: " + "o7nHqZ8vzo7Vf20jF0fkh/jUtR3yo1TwRGpXE7ERxZeJ4C5S/Nsft4lqOg7zGwfsD5Nh9tTVgdw4PrybJRF0gA==", false)]
	[InlineData("Successfully verified package 'CheatEngine.SDK.1.0.0'.", false)]
	public async Task Content_hash_is_found_in_verify_output_whatever_the_ui_language(string output, bool expected)
	{
		ProcessResult run = await PowerShellScript.RunWithReleaseToolsAsync(
			$"Test-VerifyOutputContainsContentHash -Output {PowerShellScript.Literal(output)} -ContentHash {PowerShellScript.Literal(ContentHash)}");

		Assert.True(run.ExitCode == 0, run.CombinedOutput);
		Assert.Equal(expected ? "True" : "False", run.StandardOutput.Trim());
	}

	private async Task<JsonElement> CompareAsync(Dictionary<string, string> attested, Dictionary<string, string> signed)
	{
		string attestedPath = WriteZip("attested.nupkg", attested);
		string signedPath = WriteZip("signed.nupkg", signed);
		ProcessResult run = await PowerShellScript.RunWithReleaseToolsAsync(
			$"Compare-SignedPackageContent -AttestedPath {PowerShellScript.Literal(attestedPath)} " +
			$"-SignedPath {PowerShellScript.Literal(signedPath)} | ConvertTo-Json -Compress");
		Assert.True(run.ExitCode == 0, run.CombinedOutput);
		return JsonDocument.Parse(run.StandardOutput).RootElement.Clone();
	}

	private string WriteZip(string name, Dictionary<string, string> entries)
	{
		string path = Path.Combine(_directory.FullName, name);
		File.Delete(path);
		using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
		foreach ((string entryName, string content) in entries)
		{
			using StreamWriter writer = new(archive.CreateEntry(entryName).Open());
			writer.Write(content);
		}

		return path;
	}

	private static List<string> Strings(JsonElement array)
	{
		return [.. array.EnumerateArray().Select(static e => e.GetString()!)];
	}
}
