namespace CheatEngine.SDK.Repository.Tests.Abi;

/// <summary>
///     The vocabulary of the committed classic slot registry, as the C# checks in this folder use it. There is no JSON
///     Schema file: the registry is test-owned data (never a top-level <c>docs/</c> folder), so every shape rule these
///     constants describe is enforced directly by <see cref="ClassicSlotRegistryDocumentTests" />.
/// </summary>
internal static class ClassicSlotRegistryContract
{
	internal const string RegistryPath = "tests/CheatEngine.SDK.Repository.Tests/Abi/TestData/classic-slot-registry.json";
	internal const string Kind = "cheatengine-classic-slot-registry/v0";
	internal const string UpstreamCommit = "ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37";
	internal const string QualifiableProfile = "ce-7.7.0.10621-x64-managed-hostfxr";
	internal const string DocumentaryProfile = "ce-public-src-ec45d5f";

	internal static readonly string[] TopLevelRequired =
		["schema", "generator", "sources", "contract", "profileStatus", "slots", "divergences", "callbackCategories"];

	internal static readonly string[] SourceRequired =
		["id", "role", "repository", "commit", "path", "sha256", "lineRanges", "installed"];

	internal static readonly string[] ContractRequired =
	[
		"pluginContractVersion", "hostTableType", "fieldCount", "pointerFieldCount", "x64TableSize", "sdkDirectPrefixSize",
		"sdkPrefixSlots"
	];

	internal static readonly string[] SlotRequired =
	[
		"slot", "x64Offset", "width", "minDeclaredSize", "section", "hostField", "hostAssignment", "hostImplementation",
		"nature", "indirection", "callingConvention", "nullability", "mirrors", "divergenceRefs", "sdkExposure", "sdkField",
		"facadeStatus", "ownership", "luaEquivalent", "catalogSurfaceId", "evidenceKind", "layoutEvidenceKind",
		"hostProfileStatus", "qualification"
	];

	internal static readonly string[] DivergenceRequired =
	[
		"id", "subject", "slots", "records", "callbacks", "headerC", "hostPascal", "decision", "auditRef", "origin",
		"evidenceKind"
	];

	internal static readonly string[] CallbackCategoryRequired =
	[
		"pluginType", "name", "hostForm", "headerForm", "additionalForms", "versionVariants", "versionDependent", "context",
		"synchronousDecision", "sdkRecord", "sdkCallbackTyped", "shapeTest", "note", "evidenceKind"
	];

	internal static readonly string[] SourceIds = ["plugin-pas", "pluginexports-pas", "cepluginsdk-h", "cepluginsdk-pas"];

	internal static readonly string[] SourceRoles = ["Authority", "HostImplementation", "MirrorC", "MirrorPascal"];

	internal static readonly string[] InstalledRelations = ["Identical", "CommentOnlyDifference", "Different"];

	internal static readonly string[] Sections = ["Base", "V2", "V3", "V4", "V5"];

	internal static readonly string[] AssignmentKinds = ["Size", "FunctionAddress", "CellAddress", "VariableAddress", "Nil"];

	internal static readonly string[] Natures =
		["Int32", "Function", "FunctionPointerCell", "DataCell", "ObjectRefCell", "Unknown"];

	internal static readonly string[] CallingConventions =
		["Stdcall", "Fastcall", "Register", "Cdecl", "NotApplicable", "Unknown"];

	internal static readonly string[] Nullabilities = ["NonNull", "NilAssigned", "NotApplicable", "Unknown"];

	internal static readonly string[] SdkExposures = ["PrefixTyped", "PrefixOpaque", "None"];

	internal static readonly string[] FacadeStatuses = ["PrefixOnly", "Deferred"];

	internal static readonly string[] Ownerships = ["Borrowed", "NotApplicable", "Unknown"];

	internal static readonly string[] SlotEvidenceKinds = ["ObservedSource", "ToQualify"];

	internal static readonly string[] ProfileStatuses = ["SourceOnly", "NotObserved"];

	internal static readonly string[] DivergenceOrigins = ["Audit", "LotObservation"];

	internal static readonly string[] CallbackContexts = ["MainThread", "WorkerThread", "Unknown"];

	/// <summary>The evidence vocabulary of shared-contracts section 2.0 (audit ch. 01 plus the repository's terms).</summary>
	internal static readonly string[] EvidenceKinds =
	[
		"ObservedSource", "DeclaredRepo", "Deduced", "ToQualify", "ProposedDecision", "ObservedHost", "ExactBinary",
		"ExactInstalledFile", "PinnedUpstream", "ObservedLive", "Inferred", "Unknown"
	];

	/// <summary>Pinned upstream inputs of the importer (raw bytes as served, CRLF): source id to SHA-256.</summary>
	internal static readonly Dictionary<string, string> PinnedSourceSha256 = new(StringComparer.Ordinal)
	{
		["plugin-pas"] = "358f51a39ad14d00ecba3c9137f440152d4ab85f1d2498068fa81fca906d09db",
		["pluginexports-pas"] = "0192ce02441be2fdf080ba829eae3d8edf6cb24b83308154d5068ca642e2c2ea",
		["cepluginsdk-h"] = "b6500df1e94d7bb011b38e173b2603197b7a1f304496d751ede82e57e36e532f",
		["cepluginsdk-pas"] = "cda5269f441120e5a3bff2f87e289cd71de9158ca2a619c7d0a734eb98ee6052"
	};
}
