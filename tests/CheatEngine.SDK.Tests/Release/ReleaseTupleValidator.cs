using System.Text.RegularExpressions;

namespace CheatEngine.SDK.Tests.Release;

/// <summary>
///     Validates a release tuple (<c>cheatengine-release-tuple/v0</c>) with System.Text.Json only: the object shapes of
///     <c>eng/release/release-tuple.v0.schema.json</c>, the value patterns, the stage rules and the encoding rules (no
///     absolute local path). <see cref="ReleaseTupleSchemaTests" /> asserts that the required, enum and const lists below
///     equal the schema's, so the schema and this validator cannot drift apart.
/// </summary>
internal static partial class ReleaseTupleValidator
{
	/// <summary>The <c>schema</c> value of every tuple.</summary>
	public const string SchemaValue = "cheatengine-release-tuple/v0";

	/// <summary>The only package a tuple describes.</summary>
	public const string PackageId = "CheatEngine.SDK";

	/// <summary>
	///     Required property names of every object, in schema order. Keys are paths: <c>""</c> is the root, <c>a.b</c> a
	///     nested object, <c>a[]</c> the items of array <c>a</c>.
	/// </summary>
	public static readonly IReadOnlyDictionary<string, string[]> RequiredProperties =
		new Dictionary<string, string[]>(StringComparer.Ordinal)
		{
			[""] =
			[
				"schema", "stage", "package", "source", "build", "nativeBridge", "ceProfile", "qualification", "sbom",
				"attestations", "assets", "createdUtc"
			],
			["package"] =
			[
				"id", "version", "attestedAssetSha256", "contentHashSha512", "nugetOrgSignedSha256", "nugetOrgSignedSha512",
				"repositorySignatureVerified"
			],
			["source"] = ["repository", "tag", "commit", "treeHash", "pullRequest", "releaseRunUrl", "ciRunUrl"],
			["source.pullRequest"] = ["number", "headSha"],
			["build"] = ["dotnetSdk", "runner", "toolchain", "roslynFloor", "analysisLevel"],
			["build.runner"] = ["label", "imageOs", "imageVersion"],
			["build.toolchain"] = ["xmake", "msvcToolset", "msvcVersion", "windowsSdk"],
			["nativeBridge"] = ["packagePath", "sha256", "sourceFingerprint"],
			["ceProfile"] = ["profileId", "supportProfileSha256"],
			["qualification"] = ["matrixSha256", "receipts"],
			["qualification.receipts[]"] = ["receiptId", "qualificationId", "level", "status", "sha256"],
			["sbom"] = ["entry", "sha256", "spdxVersion", "attested"],
			["attestations"] = ["provenanceBundle", "sbomBundle"],
			["assets[]"] = ["name", "sha256"]
		};

	/// <summary>Enumerated string values, by path.</summary>
	public static readonly IReadOnlyDictionary<string, string[]> EnumValues =
		new Dictionary<string, string[]>(StringComparer.Ordinal)
		{
			["stage"] = ["PrePublish", "Published"],
			["qualification.receipts[].level"] = ["C3", "C4"],
			["qualification.receipts[].status"] = ["Passed", "Failed", "NotApplicable"]
		};

	/// <summary>Constant string values, by path.</summary>
	public static readonly IReadOnlyDictionary<string, string> ConstValues =
		new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["schema"] = SchemaValue,
			["package.id"] = PackageId,
			["nativeBridge.packagePath"] = "build/native/cheatengine-sdk-lua-bridge.dll",
			["sbom.entry"] = "_manifest/spdx_2.2/manifest.spdx.json",
			["sbom.spdxVersion"] = "SPDX-2.2"
		};

	/// <summary>Paths whose value may be <see langword="null" /> (dry runs, absent qualification files, before publication).</summary>
	private static readonly HashSet<string> s_nullable = new(StringComparer.Ordinal)
	{
		"package.nugetOrgSignedSha256", "package.nugetOrgSignedSha512", "package.repositorySignatureVerified", "source.tag",
		"source.pullRequest", "build.roslynFloor", "build.analysisLevel", "ceProfile.supportProfileSha256",
		"qualification.matrixSha256", "attestations.provenanceBundle", "attestations.sbomBundle"
	};

	private static readonly (string Path, Regex Pattern)[] s_patterns =
	[
		("package.version", Version()), ("package.attestedAssetSha256", Sha256()), ("package.contentHashSha512", Sha512()),
		("package.nugetOrgSignedSha256", Sha256()), ("package.nugetOrgSignedSha512", Sha512()),
		("source.repository", Repository()), ("source.tag", Tag()), ("source.commit", GitObject()),
		("source.treeHash", GitObject()), ("source.pullRequest.headSha", GitObject()), ("source.releaseRunUrl", HttpsUrl()),
		("source.ciRunUrl", HttpsUrl()), ("build.dotnetSdk", NonEmpty()), ("build.runner.label", NonEmpty()),
		("build.runner.imageOs", NonEmpty()), ("build.runner.imageVersion", NonEmpty()), ("build.toolchain.xmake", NonEmpty()),
		("build.toolchain.msvcToolset", NonEmpty()), ("build.toolchain.msvcVersion", NonEmpty()),
		("build.toolchain.windowsSdk", NonEmpty()), ("build.roslynFloor", NonEmpty()), ("build.analysisLevel", NonEmpty()),
		("nativeBridge.sha256", Sha256()), ("nativeBridge.sourceFingerprint", Fingerprint()),
		("ceProfile.profileId", ProfileId()), ("ceProfile.supportProfileSha256", Sha256()),
		("qualification.matrixSha256", Sha256()), ("qualification.receipts[].receiptId", ReceiptId()),
		("qualification.receipts[].qualificationId", QualificationId()), ("qualification.receipts[].sha256", Sha256()),
		("sbom.sha256", Sha256()), ("attestations.provenanceBundle", BundleName()), ("attestations.sbomBundle", BundleName()),
		("assets[].name", AssetName()), ("assets[].sha256", Sha256()), ("createdUtc", UtcTime())
	];

	/// <summary>Every rule the tuple breaks, one message each; empty when it is valid.</summary>
	public static List<string> Validate(JsonElement tuple)
	{
		List<string> errors = [];
		CheckShape(tuple, "", errors);
		if (errors.Count > 0)
		{
			return errors;
		}

		foreach ((string path, string expected) in ConstValues)
		{
			foreach (JsonElement value in Select(tuple, path))
			{
				if (value.ValueKind != JsonValueKind.String || !string.Equals(value.GetString(), expected, StringComparison.Ordinal))
				{
					errors.Add($"{path} must be '{expected}'.");
				}
			}
		}

		foreach ((string path, string[] allowed) in EnumValues)
		{
			foreach (JsonElement value in Select(tuple, path))
			{
				if (value.ValueKind != JsonValueKind.String || Array.IndexOf(allowed, value.GetString()) < 0)
				{
					errors.Add($"{path} must be one of {string.Join(", ", allowed)}.");
				}
			}
		}

		foreach ((string path, Regex pattern) in s_patterns)
		{
			foreach (JsonElement value in Select(tuple, path))
			{
				bool isNull = value.ValueKind == JsonValueKind.Null;
				if (isNull ? !s_nullable.Contains(path) : value.ValueKind != JsonValueKind.String || !pattern.IsMatch(value.GetString()!))
				{
					errors.Add($"{path} = {value.GetRawText()} does not match {pattern}.");
				}
			}
		}

		CheckTypes(tuple, errors);
		CheckStageRules(tuple, errors);
		CheckCrossReferences(tuple, errors);
		CheckNoAbsoluteLocalPath(tuple, "", errors);
		return errors;
	}

	private static void CheckShape(JsonElement element, string path, List<string> errors)
	{
		if (RequiredProperties.TryGetValue(path, out string[]? required))
		{
			if (element.ValueKind == JsonValueKind.Null && s_nullable.Contains(path))
			{
				return;
			}

			if (element.ValueKind != JsonValueKind.Object)
			{
				errors.Add($"'{Display(path)}' must be an object.");
				return;
			}

			HashSet<string> present = new(StringComparer.Ordinal);
			foreach (JsonProperty property in element.EnumerateObject())
			{
				present.Add(property.Name);
				if (Array.IndexOf(required, property.Name) < 0)
				{
					errors.Add($"'{Display(path)}' has the unknown property '{property.Name}'.");
				}
			}

			foreach (string name in required)
			{
				if (!present.Contains(name))
				{
					errors.Add($"'{Display(path)}' has no '{name}'.");
				}
			}
		}

		if (element.ValueKind == JsonValueKind.Object)
		{
			foreach (JsonProperty property in element.EnumerateObject())
			{
				string child = path.Length == 0 ? property.Name : $"{path}.{property.Name}";
				if (property.Value.ValueKind == JsonValueKind.Array)
				{
					foreach (JsonElement item in property.Value.EnumerateArray())
					{
						CheckShape(item, child + "[]", errors);
					}
				}
				else
				{
					CheckShape(property.Value, child, errors);
				}
			}
		}
	}

	private static void CheckTypes(JsonElement tuple, List<string> errors)
	{
		JsonValueKind verified = tuple.GetProperty("package").GetProperty("repositorySignatureVerified").ValueKind;
		if (verified is not (JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null))
		{
			errors.Add("package.repositorySignatureVerified must be a boolean or null.");
		}

		if (tuple.GetProperty("sbom").GetProperty("attested").ValueKind is not (JsonValueKind.True or JsonValueKind.False))
		{
			errors.Add("sbom.attested must be a boolean.");
		}

		JsonElement pullRequest = tuple.GetProperty("source").GetProperty("pullRequest");
		if (pullRequest.ValueKind == JsonValueKind.Object
			&& (!pullRequest.GetProperty("number").TryGetInt32(out int number) || number < 1))
		{
			errors.Add("source.pullRequest.number must be a positive integer.");
		}

		if (tuple.GetProperty("qualification").GetProperty("receipts").ValueKind != JsonValueKind.Array)
		{
			errors.Add("qualification.receipts must be an array.");
		}

		JsonElement assets = tuple.GetProperty("assets");
		if (assets.ValueKind != JsonValueKind.Array || assets.GetArrayLength() < 2)
		{
			errors.Add("assets must list at least the package and its SBOM.");
		}
	}

	private static void CheckStageRules(JsonElement tuple, List<string> errors)
	{
		JsonElement package = tuple.GetProperty("package");
		string[] publicationFields = ["nugetOrgSignedSha256", "nugetOrgSignedSha512", "repositorySignatureVerified"];
		if (string.Equals(tuple.GetProperty("stage").GetString(), "Published", StringComparison.Ordinal))
		{
			foreach (string field in publicationFields)
			{
				if (package.GetProperty(field).ValueKind == JsonValueKind.Null)
				{
					errors.Add($"A Published tuple needs package.{field}.");
				}
			}

			if (package.GetProperty("repositorySignatureVerified").ValueKind != JsonValueKind.True)
			{
				errors.Add("A Published tuple needs package.repositorySignatureVerified = true.");
			}

			if (tuple.GetProperty("source").GetProperty("tag").ValueKind == JsonValueKind.Null)
			{
				errors.Add("A Published tuple needs source.tag.");
			}

			foreach (JsonProperty bundle in tuple.GetProperty("attestations").EnumerateObject())
			{
				if (bundle.Value.ValueKind == JsonValueKind.Null)
				{
					errors.Add($"A Published tuple needs attestations.{bundle.Name}.");
				}
			}

			if (tuple.GetProperty("sbom").GetProperty("attested").ValueKind != JsonValueKind.True)
			{
				errors.Add("A Published tuple needs sbom.attested = true.");
			}
		}
		else
		{
			foreach (string field in publicationFields)
			{
				if (package.GetProperty(field).ValueKind != JsonValueKind.Null)
				{
					errors.Add($"A PrePublish tuple has no package.{field}.");
				}
			}
		}
	}

	private static void CheckCrossReferences(JsonElement tuple, List<string> errors)
	{
		JsonElement package = tuple.GetProperty("package");
		string version = package.GetProperty("version").GetString() ?? "";
		JsonElement tag = tuple.GetProperty("source").GetProperty("tag");
		if (tag.ValueKind == JsonValueKind.String && !string.Equals(tag.GetString(), "v" + version, StringComparison.Ordinal))
		{
			errors.Add($"source.tag {tag.GetString()} does not name version {version}.");
		}

		Dictionary<string, string> assets = new(StringComparer.Ordinal);
		foreach (JsonElement asset in tuple.GetProperty("assets").EnumerateArray())
		{
			string name = asset.GetProperty("name").GetString() ?? "";
			if (!assets.TryAdd(name, asset.GetProperty("sha256").GetString() ?? ""))
			{
				errors.Add($"assets lists '{name}' twice.");
			}
		}

		string packageFile = $"{PackageId}.{version}.nupkg";
		if (!assets.TryGetValue(packageFile, out string? packageSha256)
			|| !string.Equals(packageSha256, package.GetProperty("attestedAssetSha256").GetString(), StringComparison.Ordinal))
		{
			errors.Add($"assets must list {packageFile} with package.attestedAssetSha256.");
		}

		string sbomFile = $"{PackageId}.{version}.spdx.json";
		if (!assets.TryGetValue(sbomFile, out string? sbomSha256)
			|| !string.Equals(sbomSha256, tuple.GetProperty("sbom").GetProperty("sha256").GetString(), StringComparison.Ordinal))
		{
			errors.Add($"assets must list {sbomFile} with sbom.sha256.");
		}

		JsonElement provenance = tuple.GetProperty("attestations").GetProperty("provenanceBundle");
		JsonElement sbomBundle = tuple.GetProperty("attestations").GetProperty("sbomBundle");
		if ((provenance.ValueKind == JsonValueKind.Null) != (sbomBundle.ValueKind == JsonValueKind.Null))
		{
			errors.Add("attestations names both bundles or neither.");
		}

		JsonElement[] bundles = [provenance, sbomBundle];
		foreach (JsonElement bundle in bundles)
		{
			if (bundle.ValueKind == JsonValueKind.String && !assets.ContainsKey(bundle.GetString()!))
			{
				errors.Add($"assets must list the attestation bundle {bundle.GetString()}.");
			}
		}

		bool attested = tuple.GetProperty("sbom").GetProperty("attested").ValueKind == JsonValueKind.True;
		if (attested != (sbomBundle.ValueKind == JsonValueKind.String))
		{
			errors.Add("sbom.attested is true exactly when attestations.sbomBundle names a bundle.");
		}
	}

	private static void CheckNoAbsoluteLocalPath(JsonElement element, string path, List<string> errors)
	{
		switch (element.ValueKind)
		{
			case JsonValueKind.String when AbsoluteLocalPath().IsMatch(element.GetString()!):
				errors.Add($"'{Display(path)}' contains an absolute local path: {element.GetString()}");
				break;
			case JsonValueKind.Object:
				foreach (JsonProperty property in element.EnumerateObject())
				{
					CheckNoAbsoluteLocalPath(property.Value, path.Length == 0 ? property.Name : $"{path}.{property.Name}", errors);
				}

				break;
			case JsonValueKind.Array:
				foreach (JsonElement item in element.EnumerateArray())
				{
					CheckNoAbsoluteLocalPath(item, path + "[]", errors);
				}

				break;
		}
	}

	/// <summary>Every value at <paramref name="path" />; <c>[]</c> segments fan out over array items. Absent values yield nothing.</summary>
	private static List<JsonElement> Select(JsonElement root, string path)
	{
		List<JsonElement> current = [root];
		foreach (string segment in path.Split('.'))
		{
			bool items = segment.EndsWith("[]", StringComparison.Ordinal);
			string name = items ? segment[..^2] : segment;
			List<JsonElement> next = [];
			foreach (JsonElement element in current)
			{
				if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out JsonElement value))
				{
					continue;
				}

				if (!items)
				{
					next.Add(value);
				}
				else if (value.ValueKind == JsonValueKind.Array)
				{
					next.AddRange(value.EnumerateArray());
				}
			}

			current = next;
		}

		return current;
	}

	private static string Display(string path)
	{
		return path.Length == 0 ? "(root)" : path;
	}

	[GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex Sha256();

	[GeneratedRegex("^[A-Za-z0-9+/]{86}==$", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex Sha512();

	[GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex GitObject();

	[GeneratedRegex("^https://", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex HttpsUrl();

	[GeneratedRegex("^.+$", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex NonEmpty();

	[GeneratedRegex("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex Repository();

	[GeneratedRegex("^v[0-9]+\\.[0-9]+\\.[0-9]+(-[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, 1000)]
	private static partial Regex Tag();

	[GeneratedRegex("^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\\.[0-9A-Za-z-]+)*)?$",
		RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, 1000)]
	private static partial Regex Version();

	[GeneratedRegex("^[0-9a-f]{64}:[0-9a-f]{64}$", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex Fingerprint();

	[GeneratedRegex("^[a-z0-9][a-z0-9.-]*$", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex ProfileId();

	[GeneratedRegex("^R-[0-9]{8}T[0-9]{6}Z-Q[0-9]{2}(\\.[a-z])?-[0-9a-f]{8}$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, 1000)]
	private static partial Regex ReceiptId();

	[GeneratedRegex("^Q(0[1-9]|[1-3][0-9]|4[0-8])(\\.[a-z])?$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, 1000)]
	private static partial Regex QualificationId();

	[GeneratedRegex("^CheatEngine\\.SDK\\.[0-9A-Za-z.-]+\\.sigstore\\.json$", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex BundleName();

	[GeneratedRegex("^[^\\\\/\\s]+$", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex AssetName();

	[GeneratedRegex("^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex UtcTime();

	[GeneratedRegex("[A-Za-z]:\\\\|\\\\Users\\\\|/home/|/Users/|file://", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex AbsoluteLocalPath();
}
