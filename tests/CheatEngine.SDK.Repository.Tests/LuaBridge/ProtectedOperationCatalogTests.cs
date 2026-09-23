using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

using CheatEngine.SDK.Repository.Tests.Qualification.Validation;

namespace CheatEngine.SDK.Repository.Tests.LuaBridge;

/// <summary>
///     <c>libs/CheatEngine.SDK.Lua.Interop/Protected/protected-operations.json</c>, the catalogue of the C11 Lua protection bridge: every rule of the
///     retired <c>Test-ProtectedOperationCatalog.ps1</c> (schema, uniqueness, derived bitmap, native enum and switch,
///     direct-call policy) plus the failure evidence of every operation that can raise (audit A20-Q13, CI-SDK-ENG-1).
///     The LuaBridgeContract generator reads the same file: it requires <c>schemaVersion</c> 1 and ignores properties it
///     does not know, such as <c>failureEvidence</c>.
/// </summary>
public sealed partial class ProtectedOperationCatalogTests
{
	private const int BitmapWidth = 64;
	private const string ProbeMarkerKind = "FailureProbeMarker";
	private const string ManagedTestKind = "ManagedTest";
	private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

	[Fact]
	public void Catalog_matches_its_schema()
	{
		JsonSchemaSubset schema = LuaBridgeDocuments.CatalogueSchema;
		JsonElement catalogue = LuaBridgeDocuments.Catalogue;

		IReadOnlyList<string> errors = schema.Validate(catalogue);

		Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
		Assert.Contains(schema.Validate(LuaBridgeDocuments.Mutate(catalogue,
				static root => root["operations"]![0]!["failureEvidence"]![0]!["kind"] = "Screenshot")),
			static error => error.Contains("/operations/0/failureEvidence/0", StringComparison.Ordinal));
		Assert.Contains(schema.Validate(LuaBridgeDocuments.Mutate(catalogue,
				static root => root["operations"]![0]!["opcode"] = -1)),
			static error => error.Contains("/operations/0/opcode", StringComparison.Ordinal));
		Assert.Contains(schema.Validate(LuaBridgeDocuments.Mutate(catalogue,
				static root => root["operations"]![0]!["hostOperation"] = " ")),
			static error => error.Contains("/operations/0/hostOperation", StringComparison.Ordinal));
		Assert.Contains(schema.Validate(LuaBridgeDocuments.Mutate(catalogue,
				static root => root["directApiPolicy"]![0]!["provenance"]![0]!["source"] = "http://example.invalid")),
			static error => error.Contains("/directApiPolicy/0/provenance/0/source", StringComparison.Ordinal));
	}

	[Fact]
	public void Catalog_schema_has_the_repository_id_and_uses_only_the_validator_keywords()
	{
		JsonSchemaSubset schema = LuaBridgeDocuments.CatalogueSchema;

		Assert.Equal(LuaBridgeDocuments.Draft202012, schema.Root.GetProperty("$schema").GetString());
		Assert.Equal(LuaBridgeDocuments.SchemaIdPrefix + LuaBridgeDocuments.CatalogueSchemaPath,
			schema.Root.GetProperty("$id").GetString());
		List<string> problems = LuaBridgeDocuments.SchemaShapeProblems(schema);
		Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
		Assert.Equal(1, LuaBridgeDocuments.Catalogue.GetProperty("schemaVersion").GetInt32());
		Assert.Equal("cheatengine-sdk-lua-protected-operations",
			LuaBridgeDocuments.Catalogue.GetProperty("catalogId").GetString());
	}

	[Fact]
	public void Operation_ids_opcodes_native_enums_and_constants_are_unique_and_derive_the_bitmap()
	{
		JsonElement[] operations = LuaBridgeDocuments.Operations;
		JsonElement contract = LuaBridgeDocuments.Catalogue.GetProperty("bridgeContract");
		ulong bitmap = 0;
		int previousOpcode = -1;
		foreach (JsonElement operation in operations)
		{
			string id = operation.GetProperty("id").GetString()!;
			int opcode = operation.GetProperty("opcode").GetInt32();
			Assert.InRange(opcode, 0, BitmapWidth - 1);
			Assert.True(opcode > previousOpcode, $"{id}: operations must be ordered by opcode.");
			Assert.Equal(id + "Operation", operation.GetProperty("managed").GetProperty("constant").GetString());
			Assert.True(operation.GetProperty("protected").GetBoolean(), $"{id} must stay protected.");
			Assert.True(operation.GetProperty("requiresNativeProtection").GetBoolean(),
				$"{id} must stay below the C11 lua_pcallk boundary.");
			Assert.NotEqual(0, operation.GetProperty("provenance").GetArrayLength());
			previousOpcode = opcode;
			bitmap |= 1UL << opcode;
		}

		AssertUnique(operations, static operation => operation.GetProperty("id").GetString()!);
		AssertUnique(operations, static operation => operation.GetProperty("nativeEnum").GetString()!);
		AssertUnique(operations, static operation => operation.GetProperty("managed").GetProperty("constant").GetString()!);
		AssertUnique(operations, static operation => operation.GetProperty("managed").GetProperty("wrapper").GetString()!);
		Assert.Equal(BitmapWidth, contract.GetProperty("operationBitmapWidth").GetInt32());
		Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"0x{bitmap:X16}"),
			contract.GetProperty("operationBitmap").GetString());
	}

	[Fact]
	public void Every_operation_has_its_native_enum_value_and_switch_case_in_the_lf_pinned_bridge_source()
	{
		string source = File.ReadAllText(QualificationDocuments.Absolute(LuaBridgeDocuments.BridgeSourcePath));
		Assert.DoesNotContain('\r', source);
		Match nativeEnum = NativeOperationEnum().Match(source);
		Assert.True(nativeEnum.Success, "The C11 protected-operation enum was not found.");
		string values = nativeEnum.Groups["values"].Value;
		Match count = OperationCount().Match(values);
		Assert.True(count.Success, "The C11 protected-operation enum has no count sentinel.");
		Match mask = OperationMask().Match(source);
		Assert.True(mask.Success, "The C11 protected-operation mask was not found.");

		JsonElement[] operations = LuaBridgeDocuments.Operations;
		Assert.Equal(operations.Length, int.Parse(count.Groups["count"].Value, CultureInfo.InvariantCulture));
		foreach (JsonElement operation in operations)
		{
			string name = Regex.Escape(operation.GetProperty("nativeEnum").GetString()!);
			int opcode = operation.GetProperty("opcode").GetInt32();
			Assert.True(Regex.IsMatch(values, $@"\b{name}\s*=\s*{opcode}\b", RegexOptions.CultureInvariant, RegexTimeout),
				$"The native enum does not define {name} = {opcode}.");
			Assert.True(Regex.IsMatch(source, $@"\bcase\s+{name}\s*:", RegexOptions.CultureInvariant, RegexTimeout),
				$"The native bridge has no case for {name}.");
			Assert.True(Regex.IsMatch(mask.Groups["mask"].Value, $@"\b{name}\b", RegexOptions.CultureInvariant, RegexTimeout),
				$"The native operation mask does not name {name}.");
		}
	}

	[Fact]
	public void Direct_api_policy_routes_are_exclusive_explicit_and_never_direct_for_raising_apis()
	{
		HashSet<string> operations = new(LuaBridgeDocuments.Operations.Select(static operation =>
			operation.GetProperty("id").GetString()!), StringComparer.Ordinal);
		JsonElement[] policies = [.. LuaBridgeDocuments.Catalogue.GetProperty("directApiPolicy").EnumerateArray()];
		AssertUnique(policies, static policy => policy.GetProperty("managedSymbol").GetString()!);
		List<string> problems = [];
		foreach (JsonElement policy in policies)
		{
			string symbol = policy.GetProperty("managedSymbol").GetString()!;
			bool direct = policy.GetProperty("allowedDirectly").GetBoolean();
			bool bridge = policy.GetProperty("requiresBridge").GetBoolean();
			string? operation = LuaBridgeDocuments.OptionalString(policy, "bridgeOperation");
			if (direct == bridge)
			{
				problems.Add($"{symbol}: choose exactly one route, direct or bridge.");
			}

			if (direct && !string.Equals(policy.GetProperty("raises").GetString(), "never", StringComparison.Ordinal))
			{
				problems.Add($"{symbol}: a directly allowed API must be classified never.");
			}

			if (operation is not null && !operations.Contains(operation))
			{
				problems.Add($"{symbol}: bridge operation {operation} is not in the catalogue.");
			}

			if (policy.TryGetProperty("conditionalDirectUse", out JsonElement conditional) &&
				(direct || !bridge || !conditional.GetProperty("allowed").GetBoolean()))
			{
				problems.Add($"{symbol}: a conditional direct use keeps the bridge as its default route and opts in explicitly.");
			}
		}

		Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
	}

	[Fact]
	[Trait("Qualification", "Q13")]
	public void Every_raising_operation_names_a_failure_test()
	{
		List<string> problems = [];
		foreach (JsonElement operation in LuaBridgeDocuments.Operations)
		{
			string id = operation.GetProperty("id").GetString()!;
			bool raises = !string.Equals(operation.GetProperty("raises").GetString(), "never", StringComparison.Ordinal);
			if (raises && (!operation.TryGetProperty("failureEvidence", out JsonElement evidence) ||
						   evidence.GetArrayLength() == 0))
			{
				problems.Add($"{id} can raise but names no failure evidence (a failure-probe marker or a Q13 test).");
			}
		}

		Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
		Assert.Contains(LuaBridgeDocuments.Operations, static operation =>
			!string.Equals(operation.GetProperty("raises").GetString(), "never", StringComparison.Ordinal));
	}

	[Fact]
	public void Managed_failure_evidence_resolves_to_a_Q13_traited_method()
	{
		JsonElement[] tests = [.. Evidence(ManagedTestKind)];
		Assert.NotEmpty(tests);
		foreach (JsonElement test in tests)
		{
			string project = test.GetProperty("project").GetString()!;
			string file = test.GetProperty("file").GetString()!;
			string[] name = test.GetProperty("test").GetString()!.Split('.');
			Assert.True(QualificationDocuments.Exists(project), $"The evidence project {project} does not exist.");
			Assert.StartsWith(project[..(project.LastIndexOf('/') + 1)], file, StringComparison.Ordinal);
			IReadOnlyList<string>? traits = TestSourceIndex.TraitsOfMethod(file, name[0], name[1]);
			Assert.True(traits is not null, $"{file} declares no {name[0]}.{name[1]}.");
			Assert.Contains("Q13", traits, StringComparer.Ordinal);
		}
	}

	[Fact]
	public void Probe_marker_evidence_is_emitted_by_the_failure_probe_source()
	{
		string probe = QualificationDocuments.ReadNormalizedText(LuaBridgeDocuments.FailureProbePath);
		string[] markers = [.. Evidence(ProbeMarkerKind).Select(static item => item.GetProperty("marker").GetString()!)];

		Assert.NotEmpty(markers);
		Assert.Equal(markers.Length, markers.Distinct(StringComparer.Ordinal).Count());
		Assert.All(markers, marker => Assert.Contains($"WriteMarker(\"{marker}\");", probe, StringComparison.Ordinal));
	}

	[Fact]
	public void Retired_live_ids_are_absent()
	{
		// Built from parts so this file never contains the retired strings it looks for.
		string[] retired = ["P0-" + "LIVE-", "Test-Protected" + "OperationCatalog"];
		string[] documents =
		[
			LuaBridgeDocuments.CataloguePath, LuaBridgeDocuments.CatalogueSchemaPath, LuaBridgeDocuments.MatrixPath,
			LuaBridgeDocuments.MatrixSchemaPath, LuaBridgeDocuments.CatalogueReadmePath, LuaBridgeDocuments.AuditPagePath,
			"native/cheatengine-sdk-lua-bridge/README.md", "libs/CheatEngine.SDK.Lua.Interop/README.md",
			"analyzers/docs/internal-lua-direct-api-boundary.md"
		];

		Assert.False(QualificationDocuments.Exists("eng/lua-bridge/" + retired[1] + ".ps1"),
			"The PowerShell catalogue check was replaced by these tests.");
		foreach (string document in documents)
		{
			string text = QualificationDocuments.ReadNormalizedText(document);
			Assert.All(retired, id => Assert.DoesNotContain(id, text, StringComparison.Ordinal));
		}

		JsonElement pusher = Assert.Single(LuaBridgeDocuments.Operations,
			static operation => string.Equals(operation.GetProperty("id").GetString(), "PushHostObject", StringComparison.Ordinal));
		Assert.Contains("Q23", pusher.GetProperty("provenance")[0].GetProperty("verification").GetString(), StringComparison.Ordinal);
		Assert.Contains("Q23", QualificationDocuments.ReadNormalizedText(LuaBridgeDocuments.CatalogueReadmePath),
			StringComparison.Ordinal);
		Assert.NotNull(QualificationMatrix.Read(QualificationDocuments.LoadJson(QualificationDocuments.MatrixPath)).Find("Q23"));
	}

	private static IEnumerable<JsonElement> Evidence(string kind)
	{
		foreach (JsonElement operation in LuaBridgeDocuments.Operations)
		{
			if (!operation.TryGetProperty("failureEvidence", out JsonElement evidence))
			{
				continue;
			}

			foreach (JsonElement item in evidence.EnumerateArray())
			{
				if (string.Equals(item.GetProperty("kind").GetString(), kind, StringComparison.Ordinal))
				{
					yield return item;
				}
			}
		}
	}

	private static void AssertUnique(JsonElement[] items, Func<JsonElement, string> key)
	{
		string[] duplicates = [.. items.GroupBy(key, StringComparer.Ordinal).Where(static group => group.Count() > 1)
			.Select(static group => group.Key)];
		Assert.True(duplicates.Length == 0, "Duplicated: " + string.Join(", ", duplicates));
	}

	[GeneratedRegex(@"enum\s*\{\s*(?<values>OP_PUSH_BYTES\s*=\s*0,[\s\S]*?CHEATENGINE_SDK_LUA_BRIDGE_OPERATION_COUNT\s*=\s*\d+)\s*\};",
		RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex NativeOperationEnum();

	[GeneratedRegex(@"CHEATENGINE_SDK_LUA_BRIDGE_OPERATION_COUNT\s*=\s*(?<count>\d+)", RegexOptions.CultureInvariant,
		matchTimeoutMilliseconds: 1000)]
	private static partial Regex OperationCount();

	[GeneratedRegex(@"#define\s+CHEATENGINE_SDK_LUA_BRIDGE_OPERATION_MASK\s*\\(?<mask>[\s\S]*?)\n\s*\n",
		RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex OperationMask();
}
