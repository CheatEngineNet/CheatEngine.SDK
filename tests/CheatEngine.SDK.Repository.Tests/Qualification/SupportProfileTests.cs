using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using CheatEngine.SDK.Repository.Tests.Qualification.Validation;

namespace CheatEngine.SDK.Repository.Tests.Qualification;

/// <summary>
///     <c>docs/qualification/support-profile.json</c> and its page: the documentary public-source profile can never be
///     qualified, the qualifiable CE 7.7 profile names its exact host, runtime and route, and every recorded hash equals
///     the repository file it describes. No test reads the Cheat Engine installation.
/// </summary>
public sealed class SupportProfileTests
{
	private const string CeluaSha256 = "aa1342b4a5d5d5c65b255fb3a8fd7b6bcbbac1cd138961669d9f37f43e0b9c00";
	private const string LuaFixture = "native/cheat-engine/lua53-64.dll";
	private const string BridgeDirectory = "native/cheatengine-sdk-lua-bridge";
	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

	private static JsonElement SupportProfile => QualificationDocuments.LoadJson(QualificationDocuments.SupportProfilePath);

	private static JsonElement Qualifiable =>
		SupportProfileRules.Find(SupportProfile, QualificationContract.QualifiableProfileId)
		?? throw new InvalidOperationException("The qualifiable profile is missing.");

	[Fact]
	public void Support_profile_matches_its_v0_schema()
	{
		IReadOnlyList<string> errors = SupportProfileRules.Validate(SupportProfile);

		Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
		Assert.True(SupportProfile.TryGetProperty("measurements", out JsonElement measurements) &&
					measurements.GetArrayLength() > 0, "The SDK profile keeps its dated measurement record.");
	}

	[Fact]
	public void Public_source_profile_is_documentary_and_never_qualifiable()
	{
		JsonElement documentary = SupportProfileRules.Find(SupportProfile, QualificationContract.DocumentaryProfileId)
								  ?? throw new InvalidOperationException("The documentary profile is missing.");

		Assert.Equal("Documentary", documentary.GetProperty("kind").GetString());
		Assert.False(documentary.GetProperty("qualifiable").GetBoolean());
		Assert.Equal("NotExecuted", documentary.GetProperty("qualificationStatus").GetString());
		Assert.Equal("ObservedSource", documentary.GetProperty("evidenceKind").GetString());
		Assert.Equal("ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37",
			documentary.GetProperty("source").GetProperty("commit").GetString());

		// Refusal proof: the schema and the rules reject any attempt to make it qualifiable.
		Assert.NotEmpty(SupportProfileRules.Validate(MutateProfile(QualificationContract.DocumentaryProfileId,
			static profile => profile["qualifiable"] = true)));
		Assert.NotEmpty(SupportProfileRules.Validate(MutateProfile(QualificationContract.DocumentaryProfileId,
			static profile => profile["qualificationStatus"] = "HostQualified")));
		Assert.NotEmpty(SupportProfileRules.Validate(MutateProfile(QualificationContract.DocumentaryProfileId,
			static profile => profile["kind"] = "Qualifiable")));
	}

	[Fact]
	public void Qualifiable_profile_names_the_managed_hostfxr_route_and_the_local_runtimeconfig_modification()
	{
		JsonElement profile = Qualifiable;
		JsonElement runtime = profile.GetProperty("runtime");
		JsonElement runtimeconfig = runtime.GetProperty("runtimeconfig");

		Assert.Equal("Qualifiable", profile.GetProperty("kind").GetString());
		Assert.Equal("managed-hostfxr", runtime.GetProperty("loadProfile").GetString());
		Assert.Equal("LocalModified", runtimeconfig.GetProperty("classification").GetString());
		Assert.Equal("68f5d81c0a17cc5bdac40bb3d5d88a624f4d31b414f7195ad847d57b0126ac2b",
			runtimeconfig.GetProperty("sha256").GetString());
		Assert.Equal("net10.0", runtimeconfig.GetProperty("tfm").GetString());
		Assert.Equal(
			["Microsoft.NETCore.App 10.0.0 LatestMinor", "Microsoft.WindowsDesktop.App 10.0.0 LatestMinor",
				"Microsoft.AspNetCore.App 10.0.0 LatestMinor"],
			runtimeconfig.GetProperty("frameworks").EnumerateArray().Select(static framework =>
				framework.GetProperty("name").GetString() + " " + framework.GetProperty("version").GetString() + " " +
				framework.GetProperty("rollForward").GetString()), StringComparer.Ordinal);
		Assert.Equal(["LocalProcess"],
			profile.GetProperty("qualifiedBackends").EnumerateArray().Select(static backend => backend.GetString()), StringComparer.Ordinal);
		Assert.Equal(SdkPluginContractVersion(), profile.GetProperty("sdkPluginContractVersion").GetInt32());
		Assert.Equal("AMD64", profile.GetProperty("host").GetProperty("machine").GetString());
		Assert.Equal("HKCU\\Software\\Cheat Engine", profile.GetProperty("registry").GetProperty("key").GetString());
		Assert.Contains(profile.GetProperty("host").GetProperty("excludedVariants").EnumerateArray(),
			static variant => string.Equals(variant.GetProperty("exeName").GetString(), "cheatengine-x86_64-SSE4-AVX2.exe", StringComparison.Ordinal));
	}

	[Fact]
	public void Profile_lua_hash_equals_the_committed_fixture_dll()
	{
		string fixture = QualificationDocuments.RawSha256(LuaFixture);

		Assert.Equal(fixture, Qualifiable.GetProperty("lua").GetProperty("sha256").GetString());
		Assert.Contains(fixture.ToUpperInvariant(), QualificationDocuments.ReadNormalizedText("native/cheat-engine/README.md"),
			StringComparison.Ordinal);
	}

	[Fact]
	public void Profile_host_hash_and_version_equal_the_LiveProbe_authorization_constants()
	{
		string source = QualificationDocuments.ReadNormalizedText(
			"tests/CheatEngine.SDK.LiveProbe/LiveProbeAuthorization.cs");
		JsonElement host = Qualifiable.GetProperty("host");

		Assert.Equal(host.GetProperty("exeSha256").GetString(),
			Constant(source, "ExactCheatEngineSha256").ToLowerInvariant());
		Assert.Equal(host.GetProperty("version").GetString(), Constant(source, "ExactCheatEngineFileVersion"));
		Assert.Equal("cheatengine-x86_64.exe", host.GetProperty("exeName").GetString());
	}

	[Fact]
	public void Measurement_locators_name_repository_lines_that_declare_the_measured_hash()
	{
		List<string> problems = [];
		int locators = 0;
		foreach (JsonElement measurement in SupportProfile.GetProperty("measurements").EnumerateArray())
		{
			string sha256 = measurement.GetProperty("sha256").GetString()!;
			foreach (JsonElement declared in measurement.GetProperty("declaredIn").EnumerateArray())
			{
				locators++;
				string locator = declared.GetString()!;
				int colon = locator.LastIndexOf(':');
				string file = locator[..colon];
				int line = int.Parse(locator[(colon + 1)..], System.Globalization.CultureInfo.InvariantCulture);
				if (!QualificationDocuments.Exists(file))
				{
					problems.Add($"{locator}: {file} does not exist.");
					continue;
				}

				string[] lines = QualificationDocuments.ReadNormalizedText(file).Split('\n');
				if (line >= 1 && line <= lines.Length &&
					lines[line - 1].Contains(sha256, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				int actual = Array.FindIndex(lines, text => text.Contains(sha256, StringComparison.OrdinalIgnoreCase));
				problems.Add(actual < 0
					? $"{locator}: {file} no longer declares {sha256}."
					: $"{locator}: the hash is now declared at {file}:{actual + 1}; update declaredIn.");
			}
		}

		Assert.True(locators >= 4, "The measurement record lost its repository locators.");
		Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
	}

	[Fact]
	public void Profile_celua_hash_is_the_audit_reference()
	{
		foreach (JsonElement profile in SupportProfile.GetProperty("profiles").EnumerateArray())
		{
			Assert.Equal(CeluaSha256, profile.GetProperty("celua").GetProperty("sha256").GetString());
		}
	}

	[Fact]
	public void Markdown_tuple_section_names_the_checked_in_bridge_hash_and_fingerprint()
	{
		// CI replaces the working-tree DLL with the bridge it just built, so its bytes are not the committed ones. The
		// committed bytes are pinned by bridge-audit-manifest.json, which BridgeAuditManifestTests checks against the
		// committed blob (git cat-file); this test only proves that the support profile names that same hash.
		string bridge = QualificationDocuments.LoadJson(BridgeDirectory + "/bridge-audit-manifest.json")
			.GetProperty("nativeAsset").GetProperty("sha256").GetString()!;
		string fingerprint = QualificationDocuments.RawSha256(BridgeDirectory + "/cheatengine_sdk_lua_bridge.c") + ":" +
							 QualificationDocuments.RawSha256(BridgeDirectory + "/xmake.lua");
		string tuples = Section(QualificationDocuments.ReadNormalizedText(QualificationDocuments.SupportProfileMarkdownPath),
			"## Package tuples");

		Assert.Contains(bridge, tuples, StringComparison.Ordinal);
		Assert.Contains(fingerprint, tuples, StringComparison.Ordinal);
	}

	[Fact]
	public void Checkpoint_A_decisions_appear_in_json_and_markdown()
	{
		string section = Section(QualificationDocuments.ReadNormalizedText(QualificationDocuments.SupportProfileMarkdownPath),
			"## Checkpoint A decisions");
		List<string> ids = [];
		foreach (JsonElement decision in SupportProfile.GetProperty("decisions").EnumerateArray())
		{
			string id = decision.GetProperty("id").GetString()!;
			ids.Add(id);
			Assert.Equal("ProposedDecision", decision.GetProperty("evidenceKind").GetString());
			Assert.Contains("| " + id + " |", section, StringComparison.Ordinal);
		}

		Assert.Equal(["CPA-1", "CPA-2", "CPA-3"], ids);
		Assert.Equal(ids.Count, Regex.Count(section, @"^\| CPA-\d+ \|", RegexOptions.Multiline, RegexTimeout));
	}

	[Fact]
	public void Unsupported_routes_include_nativeaot_x86_host_and_sse4_variant()
	{
		List<string> routes =
			[.. SupportProfile.GetProperty("unsupportedRoutes").EnumerateArray().Select(static route => route.GetProperty("id").GetString()!)];
		string section = Section(QualificationDocuments.ReadNormalizedText(QualificationDocuments.SupportProfileMarkdownPath),
			"## Unsupported routes");

		Assert.Equal(["historical-clr-loader", "nativeaot-plugin", "x86-host", "sse4-avx2-host-variant"], routes);
		foreach (string route in routes)
		{
			Assert.Contains("`" + route + "`", section, StringComparison.Ordinal);
		}
	}

	[Fact]
	public void Not_executed_section_equals_the_matrix()
	{
		QualificationMatrix matrix =
			QualificationMatrix.Read(QualificationDocuments.LoadJson(QualificationDocuments.MatrixPath));
		string expected = QualificationMarkdown.NotExecuted(matrix);

		string? actual = QualificationMarkdown.GeneratedBlock(
			QualificationDocuments.ReadNormalizedText(QualificationDocuments.SupportProfileMarkdownPath),
			QualificationMarkdown.NotExecutedMarker);

		Assert.True(string.Equals(expected, actual, StringComparison.Ordinal),
			$"Replace the {QualificationMarkdown.NotExecutedMarker} block of {QualificationDocuments.SupportProfileMarkdownPath} with:{Environment.NewLine}{expected}");
	}

	[Fact]
	public void Qualification_documents_contain_no_absolute_local_path_or_global_percentage()
	{
		List<string> problems = [];
		foreach (string file in QualificationFiles())
		{
			string text = QualificationDocuments.ReadNormalizedText(file);
			if (TextRules.ContainsAbsoluteLocalPath(text))
			{
				problems.Add(file + ": contains an absolute local path.");
			}

			if (TextRules.ContainsPercentage(text))
			{
				problems.Add(file + ": contains a percentage; qualification documents publish no global score.");
			}

			foreach (string claim in (string[]) ["fully supported", "complete coverage", "full coverage"])
			{
				if (text.Contains(claim, StringComparison.OrdinalIgnoreCase))
				{
					problems.Add($"{file}: claims '{claim}'.");
				}
			}
		}

		Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
	}

	[Fact]
	public void Json_documents_are_in_canonical_form()
	{
		List<string> documents = [QualificationDocuments.SupportProfilePath];
		if (QualificationDocuments.Exists(QualificationDocuments.MatrixPath))
		{
			documents.Add(QualificationDocuments.MatrixPath);
		}

		foreach (string schema in QualificationContract.SchemaFiles)
		{
			documents.Add(QualificationDocuments.SchemaDirectory + "/" + schema);
		}

		foreach (string document in documents)
		{
			string committed = QualificationDocuments.ReadNormalizedText(document);
			string canonical = QualificationDocuments.Canonical(QualificationDocuments.ParseJson(committed));
			Assert.True(string.Equals(committed, canonical, StringComparison.Ordinal),
				$"{document} is not in canonical form (2-space indent, LF, unescaped non-ASCII, one final newline). Expected:{Environment.NewLine}{canonical}");
		}
	}

	internal static string Section(string markdown, string heading)
	{
		int start = markdown.IndexOf("\n" + heading + "\n", StringComparison.Ordinal);
		Assert.True(start >= 0, $"The heading '{heading}' is missing.");
		int end = markdown.IndexOf("\n## ", start + heading.Length + 1, StringComparison.Ordinal);
		return end < 0 ? markdown[start..] : markdown[start..end];
	}

	private static IEnumerable<string> QualificationFiles()
	{
		foreach (string file in Directory.EnumerateFiles(
					 QualificationDocuments.Absolute(QualificationDocuments.QualificationDirectory), "*.*",
					 SearchOption.AllDirectories))
		{
			if (file.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
				file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
			{
				yield return Infrastructure.RepositoryRoot.ToRelative(file);
			}
		}
	}

	private static int SdkPluginContractVersion()
	{
		string source = QualificationDocuments.ReadNormalizedText("libs/CheatEngine.SDK.Abi/AbiConstants.cs");
		Match match = Regex.Match(source, @"\bSdkVersion\s*=\s*(?<value>\d+)\s*;", RegexOptions.None, RegexTimeout);
		Assert.True(match.Success, "AbiConstants.SdkVersion was not found.");
		return int.Parse(match.Groups["value"].Value, System.Globalization.CultureInfo.InvariantCulture);
	}

	private static string Constant(string source, string name)
	{
		Match match = Regex.Match(source, "\\b" + name + "\\s*=\\s*\"(?<value>[^\"]+)\"", RegexOptions.None,
			RegexTimeout);
		Assert.True(match.Success, $"LiveProbeAuthorization.{name} was not found.");
		return match.Groups["value"].Value;
	}

	private static JsonElement MutateProfile(string id, Action<JsonObject> change)
	{
		JsonNode document = JsonNode.Parse(SupportProfile.GetRawText())!;
		foreach (JsonNode? profile in document["profiles"]!.AsArray())
		{
			if (string.Equals((string?) profile!["id"], id, StringComparison.Ordinal))
			{
				change(profile.AsObject());
			}
		}

		return QualificationDocuments.ParseJson(document.ToJsonString());
	}
}
