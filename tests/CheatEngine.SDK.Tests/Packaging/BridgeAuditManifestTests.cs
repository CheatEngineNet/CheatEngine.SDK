using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Packaging;

/// <summary>
///     <c>native/cheatengine-sdk-lua-bridge/bridge-audit-manifest.json</c> records the audited bridge: the SHA-256 of its two
///     LF-pinned build inputs, the fingerprint embedded in the DLL, the DLL SHA-256, its PE facts and the pinned xmake
///     version. The DLL facts are read from the <b>committed</b> blob (<c>git cat-file</c>), never from the working tree:
///     CI overwrites the working-tree DLL with the one it builds, and a byte difference between the two is drift that CI
///     reports, never a failure. No collection and no trait, so both CI legs run it. On a mismatch the message prints the
///     complete expected manifest: replacing the file with it is the regeneration procedure.
/// </summary>
public sealed partial class BridgeAuditManifestTests
{
	private const string BridgeFolder = "native/cheatengine-sdk-lua-bridge";
	private const string ManifestPath = BridgeFolder + "/bridge-audit-manifest.json";
	private const string SourceFile = "cheatengine_sdk_lua_bridge.c";
	private const string BuildFile = "xmake.lua";
	private const string FingerprintExport = "cheatengine_sdk_lua_bridge_source_fingerprint";

	private static readonly JsonSerializerOptions s_manifestFormat = new()
	{
		WriteIndented = true,
		IndentSize = 2,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	[Fact]
	public async Task Bridge_audit_manifest_records_the_current_source_hashes_and_fingerprint()
	{
		JsonNode manifest = ReadManifest();
		string sourceSha256 = Sha256(File.ReadAllBytes(BridgeInput(SourceFile)));
		string buildSha256 = Sha256(File.ReadAllBytes(BridgeInput(BuildFile)));
		string message = await StaleMessageAsync().ConfigureAwait(true);

		Assert.True(string.Equals(sourceSha256, (string?) manifest["source"]?["hashes"]?[SourceFile], StringComparison.Ordinal),
			message);
		Assert.True(string.Equals(buildSha256, (string?) manifest["source"]?["hashes"]?[BuildFile], StringComparison.Ordinal),
			message);
		Assert.True(string.Equals($"{sourceSha256}:{buildSha256}", (string?) manifest["source"]?["fingerprint"],
			StringComparison.Ordinal), message);
	}

	[Fact]
	public async Task Bridge_audit_manifest_matches_the_committed_bridge_blob()
	{
		JsonNode manifest = ReadManifest();
		BridgeFacts committed = await ReadCommittedBridgeAsync().ConfigureAwait(true);
		string message = await StaleMessageAsync().ConfigureAwait(true);
		JsonNode? asset = manifest["nativeAsset"];

		Assert.True(string.Equals(committed.Sha256, (string?) asset?["sha256"], StringComparison.Ordinal), message);
		Assert.True(string.Equals(committed.Format, (string?) asset?["pe"]?["format"], StringComparison.Ordinal), message);
		Assert.True(string.Equals(committed.Machine, (string?) asset?["pe"]?["machine"], StringComparison.Ordinal), message);
		Assert.True(committed.IsDll && (bool?) asset?["pe"]?["isDll"] == true, message);
		Assert.True(SetEquals(committed.Exports, Strings(asset?["exports"])), message);
		Assert.True(SetEquals(committed.Imports, Strings(asset?["imports"])), message);
		Assert.True(!committed.HasDelayImports && Strings(asset?["delayImports"]).Count == 0, message);
		Assert.True(string.Equals(committed.Fingerprint, (string?) manifest["source"]?["fingerprint"],
			StringComparison.Ordinal), message);
	}

	[Fact]
	public async Task Bridge_audit_manifest_names_the_pinned_xmake_version()
	{
		JsonNode manifest = ReadManifest();
		string configuration = (string?) manifest["toolchain"]?["buildSystem"]?["configuration"] ?? "";
		string message = await StaleMessageAsync().ConfigureAwait(true);

		Assert.True(File.Exists(RepositoryLayout.PathOf(configuration)),
			$"toolchain.buildSystem.configuration names '{configuration}', which does not exist. {message}");
		string? pinned = PinnedXmakeVersion(configuration);
		Assert.True(pinned is not null, $"'{configuration}' holds no single xmake-version pin. {message}");
		Assert.True(string.Equals(pinned, (string?) manifest["toolchain"]?["buildSystem"]?["version"],
			StringComparison.Ordinal), message);
	}

	[Fact]
	public void Bridge_audit_manifest_asset_path_exists_with_exact_case()
	{
		string asset = (string?) ReadManifest()["asset"] ?? "";
		string current = RepositoryLayout.PathOf(BridgeFolder);

		Assert.False(string.IsNullOrEmpty(asset), "The manifest names no asset.");
		foreach (string segment in asset.Split('/'))
		{
			string? match = null;
			foreach (string entry in Directory.GetFileSystemEntries(current))
			{
				if (string.Equals(Path.GetFileName(entry), segment, StringComparison.Ordinal))
				{
					match = entry;
				}
			}

			Assert.True(match is not null, $"'{segment}' of asset '{asset}' does not exist with that exact case in '{current}'.");
			current = match!;
		}

		Assert.True(File.Exists(current), $"Asset '{asset}' is not a file.");
	}

	/// <summary>
	///     The manifest the repository should hold, derived from the working-tree build inputs, the committed DLL blob and
	///     the pinned xmake version, with every descriptive field kept from the committed manifest.
	/// </summary>
	private static async Task<string> ExpectedManifestJsonAsync()
	{
		JsonNode expected = ReadManifest();
		BridgeFacts committed = await ReadCommittedBridgeAsync().ConfigureAwait(false);
		string sourceSha256 = Sha256(File.ReadAllBytes(BridgeInput(SourceFile)));
		string buildSha256 = Sha256(File.ReadAllBytes(BridgeInput(BuildFile)));

		JsonObject source = Object(expected, "source");
		JsonObject hashes = Object(source, "hashes");
		hashes[SourceFile] = sourceSha256;
		hashes[BuildFile] = buildSha256;
		source["fingerprint"] = $"{sourceSha256}:{buildSha256}";

		JsonObject asset = Object(expected, "nativeAsset");
		asset["sha256"] = committed.Sha256;
		JsonObject pe = Object(asset, "pe");
		pe["format"] = committed.Format;
		pe["machine"] = committed.Machine;
		pe["isDll"] = committed.IsDll;
		asset["exports"] = JsonStrings(committed.Exports);
		asset["delayImports"] = JsonStrings(committed.HasDelayImports ? ["(delay-import directory present)"] : []);
		asset["imports"] = JsonStrings(committed.Imports);

		JsonObject buildSystem = Object(Object(expected, "toolchain"), "buildSystem");
		string? pinned = PinnedXmakeVersion((string?) buildSystem["configuration"] ?? "");
		if (pinned is not null)
		{
			buildSystem["version"] = pinned;
		}

		return expected.ToJsonString(s_manifestFormat) + Environment.NewLine;
	}

	private static async Task<string> StaleMessageAsync()
	{
		return $"{ManifestPath} does not describe the committed bridge. Replace it with:{Environment.NewLine}" +
			   await ExpectedManifestJsonAsync().ConfigureAwait(false);
	}

	private static async Task<BridgeFacts> ReadCommittedBridgeAsync()
	{
		string asset = (string?) ReadManifest()["asset"] ?? "";
		byte[] blob = await CommittedFile.ReadBytesAsync($"{BridgeFolder}/{asset}").ConfigureAwait(false);
		DirectoryInfo scratch = Directory.CreateTempSubdirectory("cheatengine-sdk-bridge-blob-");
		try
		{
			string path = Path.Combine(scratch.FullName, Path.GetFileName(asset));
			await File.WriteAllBytesAsync(path, blob).ConfigureAwait(false);
			PortableExecutableInspector inspector = PortableExecutableInspector.Read(path);

			List<string> exports = [];
			foreach (PortableExecutableExport export in inspector.GetExports())
			{
				exports.Add(export.Name);
			}

			List<string> imports = [];
			foreach (PortableExecutableImport import in inspector.GetImports())
			{
				imports.Add(import.ModuleName);
			}

			exports.Sort(StringComparer.Ordinal);
			imports.Sort(StringComparer.Ordinal);
			return new BridgeFacts(
				Sha256(blob),
				inspector.Magic == PEMagic.PE32Plus ? "PE32+" : inspector.Magic.ToString(),
				inspector.Machine == Machine.Amd64 ? "AMD64" : inspector.Machine.ToString(),
				inspector.IsDll,
				exports,
				imports,
				inspector.HasDelayImports,
				inspector.ReadExportedAsciiZ(FingerprintExport));
		}
		finally
		{
			scratch.Delete(true);
		}
	}

	/// <summary>The single <c>xmake-version</c> value pinned in <paramref name="configuration" />, or <see langword="null" />.</summary>
	private static string? PinnedXmakeVersion(string configuration)
	{
		string path = RepositoryLayout.PathOf(configuration);
		if (configuration.Length == 0 || !File.Exists(path))
		{
			return null;
		}

		HashSet<string> versions = new(StringComparer.Ordinal);
		foreach (Match match in XmakeVersionPin().Matches(File.ReadAllText(path)))
		{
			versions.Add(match.Groups["version"].Value);
		}

		return versions.Count == 1 ? versions.First() : null;
	}

	private static JsonNode ReadManifest()
	{
		return JsonNode.Parse(File.ReadAllText(RepositoryLayout.PathOf(ManifestPath)))
			   ?? throw new InvalidDataException($"{ManifestPath} is empty.");
	}

	private static string BridgeInput(string fileName)
	{
		return RepositoryLayout.PathOf($"{BridgeFolder}/{fileName}");
	}

	private static JsonObject Object(JsonNode parent, string name)
	{
		return parent[name] as JsonObject
			   ?? throw new InvalidDataException($"{ManifestPath} has no '{name}' object.");
	}

	private static JsonArray JsonStrings(IReadOnlyList<string> values)
	{
		JsonArray array = [];
		foreach (string value in values)
		{
			array.Add(value);
		}

		return array;
	}

	private static List<string> Strings(JsonNode? node)
	{
		List<string> values = [];
		if (node is JsonArray array)
		{
			foreach (JsonNode? item in array)
			{
				values.Add((string?) item ?? "");
			}
		}

		return values;
	}

	private static bool SetEquals(IReadOnlyList<string> expected, List<string> actual)
	{
		return expected.Count == actual.Count
			   && new HashSet<string>(expected, StringComparer.Ordinal).SetEquals(actual);
	}

	private static string Sha256(byte[] content)
	{
		return Convert.ToHexStringLower(SHA256.HashData(content));
	}

	[GeneratedRegex("xmake-version:\\s*['\"]?(?<version>[0-9][0-9A-Za-z.+-]*)['\"]?",
		RegexOptions.CultureInvariant, 1000)]
	private static partial Regex XmakeVersionPin();

	private sealed record BridgeFacts(
		string Sha256,
		string Format,
		string Machine,
		bool IsDll,
		IReadOnlyList<string> Exports,
		IReadOnlyList<string> Imports,
		bool HasDelayImports,
		string Fingerprint);
}
