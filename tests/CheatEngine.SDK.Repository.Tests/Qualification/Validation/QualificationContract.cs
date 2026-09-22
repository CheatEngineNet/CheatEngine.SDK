namespace CheatEngine.SDK.Repository.Tests.Qualification.Validation;

/// <summary>
///     The frozen v0 vocabulary of the qualification documents (shared-contracts sections 2.0 to 2.3 with the ratified
///     A-SQUAL amendments). The semantic rules use these constants, and
///     <c>QualificationSchemaTests.Schema_required_and_enum_lists_equal_the_validator_constants</c> proves that every
///     <c>required</c> and <c>enum</c> list of the committed schemas equals them, so the schemas and the validator cannot
///     drift apart.
/// </summary>
internal static class QualificationContract
{
	internal const string SupportProfileSchema = "cheatengine-support-profile/v0";
	internal const string MatrixSchema = "cheatengine-qualification-matrix/v0";
	internal const string ReceiptSchema = "cheatengine-qualification-receipt/v0";
	internal const string EventsSchema = "cheatengine-qualification-events/v0";

	internal const string SupportProfileSchemaFile = "support-profile.v0.schema.json";
	internal const string MatrixSchemaFile = "qualification-matrix.v0.schema.json";
	internal const string ReceiptSchemaFile = "qualification-receipt.v0.schema.json";
	internal const string EventsSchemaFile = "qualification-events.v0.schema.json";

	internal const string DocumentaryProfileId = "ce-public-src-ec45d5f";
	internal const string QualifiableProfileId = "ce-7.7.0.10621-x64-managed-hostfxr";
	internal const string Repository = "CheatEngineNet/CheatEngine.SDK";
	internal const string RunnerScript = "eng/qualification/Invoke-LocalQualification.ps1";

	/// <summary>SHA-256 of the audit dossier's <c>MANIFESTE.md</c>: its identity, never a local path.</summary>
	internal const string AuditManifestSha256 = "7179b0691d27cba0589b3f5fa00945ddbb7c04b362500df3de30726550f4ff6e";

	internal const string SchemaIdPrefix =
		"https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/docs/qualification/schemas/";

	internal static readonly string[] SchemaFiles =
		[SupportProfileSchemaFile, MatrixSchemaFile, ReceiptSchemaFile, EventsSchemaFile];

	internal static readonly string[] Statuses = ["NotExecuted", "Passed", "Failed", "NotApplicable"];
	internal static readonly string[] ReceiptStatuses = ["Passed", "Failed", "NotApplicable"];
	internal static readonly string[] PassKinds = ["Functional", "RefusalVerified"];
	internal static readonly string[] Levels = ["C0", "C1", "C2", "C3", "C4"];
	internal static readonly string[] HostLevels = ["C3", "C4"];

	internal static readonly string[] EvidenceKinds =
	[
		"ObservedSource", "DeclaredRepo", "Deduced", "ToQualify", "ProposedDecision", "ObservedHost", "ExactBinary",
		"ExactInstalledFile", "PinnedUpstream", "ObservedLive", "Inferred", "Unknown"
	];

	internal static readonly string[] ExpectedCategories = ["Effect", "Partial", "Refused", "Unknown"];
	internal static readonly string[] Owners = ["SDK", "Client", "Both"];
	internal static readonly string[] Repositories = ["CheatEngineNet/CheatEngine.SDK", "CheatEngineNet/CheatEngine.Client"];
	internal static readonly string[] ProfileKinds = ["Documentary", "Qualifiable"];
	internal static readonly string[] ProfileQualificationStatuses = ["NotExecuted", "FixtureQualified", "HostQualified"];
	internal static readonly string[] Backends = ["LocalProcess", "FileAsProcess", "CEServer"];
	internal static readonly string[] Machines = ["AMD64", "I386", "ARM64"];
	internal static readonly string[] RuntimeconfigClassifications = ["LocalModified", "Installer", "Unknown"];

	internal static readonly string[] RollForwardPolicies =
		["Disable", "LatestPatch", "Minor", "LatestMinor", "Major", "LatestMajor"];

	internal static readonly string[] DotnetArchitectures = ["x64", "x86", "arm64"];
	internal static readonly string[] RegistryNameKinds = ["Subkey", "Value"];
	internal static readonly string[] AuthorizedTargetKinds = ["QualificationTarget", "GtutorialI386"];
	internal static readonly string[] TargetKinds = ["QualificationTarget", "GtutorialI386", "None"];
	internal static readonly string[] TargetArchitectures = ["x64", "x86"];
	internal static readonly string[] LoadRoutes = ["LuaLoadPlugin", "SettingsPluginsUi", "None"];
	internal static readonly string[] PackageSources = ["CiArtifact", "NuGetOrg"];

	internal static readonly string[] BundleNames =
		["LiveProbe", "LiveProbeNonAscii", "LivePlugin", "CoexistenceA", "CoexistenceB", "CoexistenceShared"];

	internal static readonly string[] EventSources = ["Runner", "Driver", "Plugin", "Operator"];

	/// <summary>
	///     Every <c>required</c> and <c>enum</c> list of the four schemas outside <c>if</c> conditions, keyed by
	///     <c>file|JSON pointer|keyword</c>.
	/// </summary>
	internal static readonly IReadOnlyDictionary<string, string[]> SchemaLists =
		new Dictionary<string, string[]>(StringComparer.Ordinal)
		{
			// support-profile.v0.schema.json
			[Key(SupportProfileSchemaFile, "/", "required")] = ["schema", "profiles", "decisions", "unsupportedRoutes"],
			[Key(SupportProfileSchemaFile, "/$defs/evidenceKind", "enum")] = EvidenceKinds,
			[Key(SupportProfileSchemaFile, "/$defs/profile", "required")] =
				["id", "kind", "qualifiable", "qualificationStatus", "description", "celua", "evidenceKind"],
			[Key(SupportProfileSchemaFile, "/$defs/profile/properties/kind", "enum")] = ProfileKinds,
			[Key(SupportProfileSchemaFile, "/$defs/profile/properties/qualificationStatus", "enum")] =
				ProfileQualificationStatuses,
			[Key(SupportProfileSchemaFile, "/$defs/profile/properties/qualifiedBackends/items", "enum")] = Backends,
			[Key(SupportProfileSchemaFile, "/$defs/profile/allOf/0/then", "required")] = ["source"],
			[Key(SupportProfileSchemaFile, "/$defs/profile/allOf/1/then", "required")] =
			[
				"host", "lua", "runtime", "registry", "qualifiedBackends", "authorizedTargets",
				"sdkPluginContractVersion"
			],
			[Key(SupportProfileSchemaFile, "/$defs/source", "required")] = ["repository", "commit"],
			[Key(SupportProfileSchemaFile, "/$defs/host", "required")] =
				["product", "version", "exeName", "exeSha256", "machine", "excludedVariants"],
			[Key(SupportProfileSchemaFile, "/$defs/host/properties/machine", "enum")] = Machines,
			[Key(SupportProfileSchemaFile, "/$defs/excludedVariant", "required")] = ["exeName", "sha256", "reason"],
			[Key(SupportProfileSchemaFile, "/$defs/lua", "required")] = ["module", "sha256"],
			[Key(SupportProfileSchemaFile, "/$defs/celua", "required")] = ["sha256"],
			[Key(SupportProfileSchemaFile, "/$defs/runtime", "required")] =
				["loadProfile", "runtimeconfig", "dotnetRuntimesObserved"],
			[Key(SupportProfileSchemaFile, "/$defs/runtimeconfig", "required")] =
				["sha256", "classification", "observedDate", "tfm", "frameworks"],
			[Key(SupportProfileSchemaFile, "/$defs/runtimeconfig/properties/classification", "enum")] =
				RuntimeconfigClassifications,
			[Key(SupportProfileSchemaFile, "/$defs/framework", "required")] = ["name", "version", "rollForward"],
			[Key(SupportProfileSchemaFile, "/$defs/framework/properties/rollForward", "enum")] = RollForwardPolicies,
			[Key(SupportProfileSchemaFile, "/$defs/dotnetRuntime", "required")] = ["name", "version", "architecture"],
			[Key(SupportProfileSchemaFile, "/$defs/dotnetRuntime/properties/architecture", "enum")] =
				DotnetArchitectures,
			[Key(SupportProfileSchemaFile, "/$defs/registry", "required")] =
				["key", "sharedAcrossCopies", "profileRelevantValues"],
			[Key(SupportProfileSchemaFile, "/$defs/registryName", "required")] = ["kind", "name", "reason"],
			[Key(SupportProfileSchemaFile, "/$defs/registryName/properties/kind", "enum")] = RegistryNameKinds,
			[Key(SupportProfileSchemaFile, "/$defs/authorizedTarget", "required")] = ["kind", "arch", "sha256", "source"],
			[Key(SupportProfileSchemaFile, "/$defs/authorizedTarget/properties/kind", "enum")] = AuthorizedTargetKinds,
			[Key(SupportProfileSchemaFile, "/$defs/authorizedTarget/properties/arch", "enum")] = TargetArchitectures,
			[Key(SupportProfileSchemaFile, "/$defs/decision", "required")] = ["id", "date", "text", "evidenceKind"],
			[Key(SupportProfileSchemaFile, "/$defs/unsupportedRoute", "required")] = ["id", "reason"],
			[Key(SupportProfileSchemaFile, "/$defs/measurement", "required")] =
				["subject", "sha256", "date", "method", "evidenceKind", "declaredIn"],

			// qualification-matrix.v0.schema.json
			[Key(MatrixSchemaFile, "/", "required")] = ["schema", "repository", "audit", "profiles", "rows"],
			[Key(MatrixSchemaFile, "/properties/repository", "enum")] = Repositories,
			[Key(MatrixSchemaFile, "/properties/audit", "required")] = ["manifestSha256"],
			[Key(MatrixSchemaFile, "/$defs/level", "enum")] = Levels,
			[Key(MatrixSchemaFile, "/$defs/status", "enum")] = Statuses,
			[Key(MatrixSchemaFile, "/$defs/passKind", "enum")] = PassKinds,
			[Key(MatrixSchemaFile, "/$defs/evidenceKind", "enum")] = EvidenceKinds,
			[Key(MatrixSchemaFile, "/$defs/row", "required")] =
			[
				"id", "parent", "title", "titleFr", "owner", "requiredLevels", "blocks", "audit", "scenario",
				"levels"
			],
			[Key(MatrixSchemaFile, "/$defs/row/properties/owner", "enum")] = Owners,
			[Key(MatrixSchemaFile, "/$defs/auditReference", "required")] = ["ref", "findings"],
			[Key(MatrixSchemaFile, "/$defs/scenario", "required")] =
				["preconditions", "operation", "expected", "expectedCategory"],
			[Key(MatrixSchemaFile, "/$defs/scenario/properties/expectedCategory", "enum")] = ExpectedCategories,
			[Key(MatrixSchemaFile, "/$defs/fixtureCell", "required")] = ["status", "evidenceKind"],
			[Key(MatrixSchemaFile, "/$defs/hostCell", "required")] = ["status", "evidenceKind", "profileId"],
			[Key(MatrixSchemaFile, "/$defs/hostCell/allOf/1/then", "required")] = ["treeHash", "nupkgSha256"],
			[Key(MatrixSchemaFile, "/$defs/statusRules/allOf/0/then", "required")] = ["passKind", "evidence", "date"],
			[Key(MatrixSchemaFile, "/$defs/statusRules/allOf/1/then", "required")] =
				["evidence", "date", "justification"],
			[Key(MatrixSchemaFile, "/$defs/statusRules/allOf/2/then", "required")] = ["justification"],
			[Key(MatrixSchemaFile, "/$defs/automatedEvidence", "required")] = ["kind", "project", "file", "test", "trait"],
			[Key(MatrixSchemaFile, "/$defs/ciRunEvidence", "required")] = ["kind", "url"],
			[Key(MatrixSchemaFile, "/$defs/receiptEvidence", "required")] = ["kind", "receiptId", "path", "sha256"],

			// qualification-receipt.v0.schema.json
			[Key(ReceiptSchemaFile, "/", "required")] =
			[
				"schema", "receiptId", "qualificationId", "level", "profileId", "operator", "loadRoute", "repository",
				"runner", "package", "host", "bridge", "bundles", "target", "registry", "preconditions", "operation",
				"expected", "observed", "status", "evidenceKind", "justification", "timings", "eventLog",
				"transferJustification", "createdUtc"
			],
			[Key(ReceiptSchemaFile, "/properties/level", "enum")] = HostLevels,
			[Key(ReceiptSchemaFile, "/properties/loadRoute", "enum")] = LoadRoutes,
			[Key(ReceiptSchemaFile, "/properties/status", "enum")] = ReceiptStatuses,
			[Key(ReceiptSchemaFile, "/properties/passKind/anyOf/0", "enum")] = PassKinds,
			[Key(ReceiptSchemaFile, "/allOf/0/then", "required")] = ["passKind"],
			[Key(ReceiptSchemaFile, "/allOf/0/then/properties/passKind", "enum")] = PassKinds,
			[Key(ReceiptSchemaFile, "/$defs/repository", "required")] = ["name", "treeHash", "commit", "pullRequest"],
			[Key(ReceiptSchemaFile, "/$defs/repository/properties/name", "enum")] = Repositories,
			[Key(ReceiptSchemaFile, "/$defs/pullRequest", "required")] = ["number", "headSha"],
			[Key(ReceiptSchemaFile, "/$defs/runner", "required")] =
				["script", "scriptSha256", "sourceRepository", "sourceCommit", "mutex"],
			[Key(ReceiptSchemaFile, "/$defs/package", "required")] =
				["id", "version", "nupkgSha256", "contentHashSha512", "source", "ciRunUrl"],
			[Key(ReceiptSchemaFile, "/$defs/package/properties/source", "enum")] = PackageSources,
			[Key(ReceiptSchemaFile, "/$defs/host", "required")] =
			[
				"ceExeName", "ceExeSha256", "ceFileVersion", "luaDllSha256", "runtimeconfigSha256", "autorunSha256",
				"sandboxCopy", "osVersion", "dotnetRuntimes"
			],
			[Key(ReceiptSchemaFile, "/$defs/bridge", "required")] = ["sha256", "sourceFingerprint"],
			[Key(ReceiptSchemaFile, "/$defs/bundle", "required")] = ["name", "manifestSha256", "files"],
			[Key(ReceiptSchemaFile, "/$defs/bundle/properties/name", "enum")] = BundleNames,
			[Key(ReceiptSchemaFile, "/$defs/bundleFile", "required")] = ["path", "sha256"],
			[Key(ReceiptSchemaFile, "/$defs/target", "required")] = ["kind", "arch", "sha256"],
			[Key(ReceiptSchemaFile, "/$defs/target/properties/kind", "enum")] = TargetKinds,
			[Key(ReceiptSchemaFile, "/$defs/target/properties/arch/anyOf/0", "enum")] = TargetArchitectures,
			[Key(ReceiptSchemaFile, "/$defs/registry", "required")] =
				["key", "exportBeforeSha256", "exportAfterSha256", "restored", "diff"],
			[Key(ReceiptSchemaFile, "/$defs/registryDiff", "required")] = ["added", "removed", "changed", "valueNames"],
			[Key(ReceiptSchemaFile, "/$defs/timings", "required")] = ["startedUtc", "finishedUtc", "durationMs"],
			[Key(ReceiptSchemaFile, "/$defs/eventLog", "required")] = ["path", "sha256", "format", "redactions"],

			// qualification-events.v0.schema.json
			[Key(EventsSchemaFile, "/", "required")] = ["schema", "receiptId", "events"],
			[Key(EventsSchemaFile, "/$defs/event", "required")] = ["tMs", "source", "kind", "message"],
			[Key(EventsSchemaFile, "/$defs/event/properties/source", "enum")] = EventSources,
			[Key(EventsSchemaFile, "/$defs/summary", "required")] = ["keptFirst", "keptLast", "dropped"]
		};

	internal static string Key(string schemaFile, string pointer, string keyword)
	{
		return schemaFile + "|" + pointer + "|" + keyword;
	}
}
