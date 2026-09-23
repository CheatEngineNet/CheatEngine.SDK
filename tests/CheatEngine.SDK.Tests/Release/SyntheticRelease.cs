using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using CheatEngine.SDK.Tests.Infrastructure;

namespace CheatEngine.SDK.Tests.Release;

/// <summary>
///     A throwaway release workspace for the <c>eng/release</c> script tests, with no pack and no network: a small
///     CheatEngine.SDK nupkg (nuspec with a repository commit, the working-tree native bridge under <c>build/native</c>, an
///     SPDX 2.2 SBOM with its checksum sidecar, one library), its exported SBOM, a <c>build-info.json</c> that follows the
///     v0 contract, the release asset folder with its <c>SHA256SUMS</c>, and a minimal repository root holding the files the
///     tuple generator reads (Roslyn floor, analysis level, bridge audit manifest, optional qualification documents).
/// </summary>
internal sealed partial class SyntheticRelease : IDisposable
{
	public const string Version = "2.0.0-alpha.0.18";
	public const string Tag = "v" + Version;
	public const string Commit = "0123456789abcdef0123456789abcdef01234567";
	public const string TreeHash = "89abcdef0123456789abcdef0123456789abcdef";
	public const string RunUrl = "https://github.com/CheatEngineNet/CheatEngine.SDK/actions/runs/123456789";
	public const string ProvenanceBundle = "CheatEngine.SDK." + Version + ".provenance.sigstore.json";
	public const string SbomBundle = "CheatEngine.SDK." + Version + ".sbom.sigstore.json";
	public const string QualifiableProfile = "ce-7.7.0.10621-x64-managed-hostfxr";

	private const string BridgeEntry = "build/native/cheatengine-sdk-lua-bridge.dll";
	private const string SbomEntry = "_manifest/spdx_2.2/manifest.spdx.json";

	private readonly DirectoryInfo _root;

	private SyntheticRelease(DirectoryInfo root)
	{
		_root = root;
		RepositoryRoot = Path.Combine(root.FullName, "repo");
		AssetsDirectory = Path.Combine(root.FullName, "release");
		PackageFile = $"{ReleaseTupleValidator.PackageId}.{Version}.nupkg";
		PackagePath = Path.Combine(AssetsDirectory, PackageFile);
		SbomFile = $"{ReleaseTupleValidator.PackageId}.{Version}.spdx.json";
		BuildInfoPath = Path.Combine(root.FullName, "build-info", "build-info.json");
		OutputPath = Path.Combine(AssetsDirectory, $"{ReleaseTupleValidator.PackageId}.{Version}.tuple.json");
		BridgeBytes = File.ReadAllBytes(
			RepositoryLayout.PathOf("native/cheatengine-sdk-lua-bridge/runtimes/win-x64/native/cheatengine-sdk-lua-bridge.dll"));
		BridgeSha256 = Sha256Hex(BridgeBytes);
		BridgeFingerprint = FindFingerprint(BridgeBytes);
	}

	public string RepositoryRoot
	{
		get;
	}

	public string AssetsDirectory
	{
		get;
	}

	public string PackageFile
	{
		get;
	}

	public string PackagePath
	{
		get;
	}

	public string SbomFile
	{
		get;
	}

	public string BuildInfoPath
	{
		get;
	}

	public string OutputPath
	{
		get;
	}

	public byte[] BridgeBytes
	{
		get;
	}

	public string BridgeSha256
	{
		get;
	}

	public string BridgeFingerprint
	{
		get;
	}

	public byte[] SbomBytes { get; private set; } = [];

	public string PackageSha256 { get; private set; } = "";

	public string PackageSha512 { get; private set; } = "";

	/// <summary>Creates the workspace; <paramref name="withBundles" /> adds the two attestation bundles of a tag run.</summary>
	public static SyntheticRelease Create(bool withBundles = true)
	{
		SyntheticRelease release = new(Directory.CreateTempSubdirectory("cheatengine-sdk-release-"));
		Directory.CreateDirectory(release.AssetsDirectory);
		release.WriteRepositoryRoot();
		release.WritePackage(includeSbom: true, spdxVersion: "SPDX-2.2");
		File.WriteAllBytes(Path.Combine(release.AssetsDirectory, release.SbomFile), release.SbomBytes);
		if (withBundles)
		{
			File.WriteAllText(Path.Combine(release.AssetsDirectory, ProvenanceBundle), """{"mediaType":"provenance"}""");
			File.WriteAllText(Path.Combine(release.AssetsDirectory, SbomBundle), """{"mediaType":"sbom"}""");
		}

		release.WriteSha256Sums();
		release.WriteBuildInfo(static _ => { });
		return release;
	}

	/// <summary>Rewrites the package, for example without an SBOM or with another SPDX version.</summary>
	public void WritePackage(bool includeSbom, string spdxVersion)
	{
		SbomBytes = Encoding.UTF8.GetBytes($$"""
		                                     {
		                                       "spdxVersion": "{{spdxVersion}}",
		                                       "SPDXID": "SPDXRef-DOCUMENT",
		                                       "name": "CheatEngine.SDK {{Version}}",
		                                       "documentNamespace": "https://github.com/CheatEngineNet/CheatEngine.SDK/synthetic/{{Version}}",
		                                       "documentDescribes": ["SPDXRef-RootPackage"],
		                                       "files": []
		                                     }
		                                     """);
		if (File.Exists(PackagePath))
		{
			File.Delete(PackagePath);
		}

		using (ZipArchive archive = ZipFile.Open(PackagePath, ZipArchiveMode.Create))
		{
			AddEntry(archive, "CheatEngine.SDK.nuspec", Encoding.UTF8.GetBytes($"""
				<?xml version="1.0" encoding="utf-8"?>
				<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
				  <metadata>
				    <id>{ReleaseTupleValidator.PackageId}</id>
				    <version>{Version}</version>
				    <authors>CheatEngineNet</authors>
				    <description>Synthetic package of the release script tests.</description>
				    <repository type="git" url="https://github.com/CheatEngineNet/CheatEngine.SDK" commit="{Commit}" />
				  </metadata>
				</package>
				"""));
			AddEntry(archive, "lib/net10.0/CheatEngine.SDK.dll", [0x4D, 0x5A, 0x90, 0x00]);
			AddEntry(archive, BridgeEntry, BridgeBytes);
			if (includeSbom)
			{
				AddEntry(archive, SbomEntry, SbomBytes);
				AddEntry(archive, SbomEntry + ".sha256", Encoding.ASCII.GetBytes(Sha256Hex(SbomBytes)));
			}
		}

		byte[] package = File.ReadAllBytes(PackagePath);
		PackageSha256 = Sha256Hex(package);
		PackageSha512 = Convert.ToBase64String(SHA512.HashData(package));
	}

	/// <summary>Writes a v0 build-info.json describing this package and bridge, then lets the test change it.</summary>
	public void WriteBuildInfo(Action<JsonObject> change)
	{
		JsonObject buildInfo = new()
		{
			["schema"] = "cheatengine-build-info/v0",
			["repository"] = "CheatEngineNet/CheatEngine.SDK",
			["commit"] = Commit,
			["treeHash"] = TreeHash,
			["ref"] = "refs/tags/" + Tag,
			["event"] = "push",
			["runId"] = 123456789,
			["runAttempt"] = 1,
			["runUrl"] = RunUrl,
			["pullRequest"] = null,
			["dotnetSdk"] = "10.0.401",
			["globalJsonSha256"] = new string('a', 64),
			["runner"] = new JsonObject
			{
				["label"] = "windows-2025",
				["imageOs"] = "win25",
				["imageVersion"] = "20260915.1.0"
			},
			["toolchain"] = new JsonObject
			{
				["xmake"] = "3.0.9",
				["msvcToolset"] = "14.44",
				["msvcVersion"] = "14.44.35207",
				["windowsSdk"] = "10.0.26100.0"
			},
			["nativeBridge"] = new JsonObject
			{
				["sha256"] = BridgeSha256,
				["checkedInSha256"] = BridgeSha256,
				["sourceFingerprint"] = BridgeFingerprint,
				["driftFromCheckedIn"] = false
			},
			["packages"] = new JsonArray(new JsonObject
			{
				["id"] = ReleaseTupleValidator.PackageId,
				["version"] = Version,
				["file"] = PackageFile,
				["sha256"] = PackageSha256,
				["sha512"] = PackageSha512,
				["sbomEntry"] = SbomEntry
			}),
			["createdUtc"] = "2026-09-23T00:00:00Z"
		};
		change(buildInfo);
		Directory.CreateDirectory(Path.GetDirectoryName(BuildInfoPath)!);
		File.WriteAllText(BuildInfoPath, buildInfo.ToJsonString());
	}

	/// <summary>Writes <c>SHA256SUMS</c> over every file of the asset folder except the tuple itself.</summary>
	public void WriteSha256Sums()
	{
		SortedDictionary<string, string> sums = new(StringComparer.Ordinal);
		foreach (string file in Directory.GetFiles(AssetsDirectory))
		{
			string name = Path.GetFileName(file);
			if (name is not "SHA256SUMS" && !string.Equals(file, OutputPath, StringComparison.Ordinal))
			{
				sums[name] = Sha256Hex(File.ReadAllBytes(file));
			}
		}

		StringBuilder text = new();
		foreach ((string name, string hash) in sums)
		{
			text.Append(hash).Append("  ").Append(name).Append('\n');
		}

		File.WriteAllText(Path.Combine(AssetsDirectory, "SHA256SUMS"), text.ToString());
	}

	/// <summary>Adds the support profile and matrix documents to the repository root.</summary>
	public void AddQualificationDocuments()
	{
		string root = Path.Combine(RepositoryRoot, "docs", "qualification");
		Directory.CreateDirectory(root);
		File.WriteAllText(Path.Combine(root, "support-profile.json"), $$"""
		                                                                 {
		                                                                   "schema": "cheatengine-support-profile/v0",
		                                                                   "profiles": [
		                                                                     { "id": "ce-public-src-ec45d5f", "kind": "Documentary" },
		                                                                     { "id": "{{QualifiableProfile}}", "kind": "Qualifiable" }
		                                                                   ]
		                                                                 }
		                                                                 """.ReplaceLineEndings("\r\n"));
		File.WriteAllText(Path.Combine(root, "matrix.json"),
			"""{ "schema": "cheatengine-qualification-matrix/v0", "rows": [] }""" + "\r\n");
	}

	/// <summary>Adds a committed receipt and its event log (which the tuple must not list).</summary>
	public string AddReceipt(string qualificationId, string receiptId, string level, string status)
	{
		string folder = Path.Combine(RepositoryRoot, "docs", "qualification", "receipts", qualificationId);
		Directory.CreateDirectory(folder);
		string path = Path.Combine(folder, receiptId + ".json");
		File.WriteAllText(path, $$"""
		                          {
		                            "schema": "cheatengine-qualification-receipt/v0",
		                            "receiptId": "{{receiptId}}",
		                            "qualificationId": "{{qualificationId}}",
		                            "level": "{{level}}",
		                            "status": "{{status}}"
		                          }
		                          """.ReplaceLineEndings("\r\n"));
		File.WriteAllText(Path.Combine(folder, receiptId + ".events.json"), """{ "receiptId": "event log" }""");
		return path;
	}

	/// <summary>Runs <c>New-ReleaseTuple.ps1</c> on this workspace with the given extra arguments.</summary>
	public Task<ProcessResult> RunTupleScriptAsync(string stage, params string[] extraArguments)
	{
		List<string> arguments =
		[
			"-Stage", stage, "-PackagePath", PackagePath, "-BuildInfoPath", BuildInfoPath, "-AssetsDirectory",
			AssetsDirectory, "-OutputPath", OutputPath, "-RepositoryRoot", RepositoryRoot
		];
		arguments.AddRange(extraArguments);
		return PowerShellScript.RunFileAsync("eng/release/New-ReleaseTuple.ps1", [.. arguments]);
	}

	/// <summary>Parses the written tuple.</summary>
	public JsonDocument ReadTuple()
	{
		return JsonDocument.Parse(File.ReadAllBytes(OutputPath));
	}

	/// <summary>SHA-256 of a text file after CRLF to LF, as the tuple records committed JSON documents.</summary>
	public static string NormalizedSha256(string path)
	{
		string text = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
		return Sha256Hex(Encoding.UTF8.GetBytes(text));
	}

	public static string Sha256Hex(byte[] content)
	{
		return Convert.ToHexStringLower(SHA256.HashData(content));
	}

	/// <inheritdoc />
	public void Dispose()
	{
		_root.Delete(true);
	}

	private void WriteRepositoryRoot()
	{
		string[] copied =
		[
			"eng/RoslynComponent.props", "Directory.Build.props", "native/cheatengine-sdk-lua-bridge/bridge-audit-manifest.json"
		];
		foreach (string relative in copied)
		{
			string target = Path.Combine(RepositoryRoot, relative.Replace('/', Path.DirectorySeparatorChar));
			Directory.CreateDirectory(Path.GetDirectoryName(target)!);
			File.Copy(RepositoryLayout.PathOf(relative), target);
		}
	}

	private static void AddEntry(ZipArchive archive, string name, byte[] content)
	{
		ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
		using Stream stream = entry.Open();
		stream.Write(content);
	}

	private static string FindFingerprint(byte[] bridge)
	{
		MatchCollection matches = FingerprintPattern().Matches(Encoding.Latin1.GetString(bridge));
		return matches.Count == 1
			? matches[0].Value
			: throw new InvalidDataException($"The working-tree bridge embeds {matches.Count} source fingerprints, expected one.");
	}

	[GeneratedRegex("[0-9a-f]{64}:[0-9a-f]{64}", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex FingerprintPattern();
}
