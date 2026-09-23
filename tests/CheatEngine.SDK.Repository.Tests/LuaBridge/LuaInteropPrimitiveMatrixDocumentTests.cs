using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using CheatEngine.SDK.Repository.Tests.Infrastructure;
using CheatEngine.SDK.Repository.Tests.Qualification.Validation;

namespace CheatEngine.SDK.Repository.Tests.LuaBridge;

/// <summary>
///     <c>tests/CheatEngine.SDK.Repository.Tests/LuaBridge/TestData/lua-interop-primitives.json</c>: one row per public static <c>LuaApi</c> member with its Lua 5.3
///     error class, side effects and the route SDK production code may take (audit SDK-LUA-2, A05-02/05/08, A18-09).
///     These tests read committed files only; the reflection and XML-documentation checks of the same rows run in
///     <c>LuaInteropPrimitiveMatrixTests</c> of the Lua.Interop tests.
/// </summary>
public sealed class LuaInteropPrimitiveMatrixDocumentTests
{
	private const string Kind = "cheatengine-lua-interop-primitives/v0";
	private const string ManualAnchorPrefix = "https://www.lua.org/manual/5.3/manual.html#";
	private const string RawApiDirectory = "libs/CheatEngine.SDK.Lua.Interop/Api/";

	/// <summary>
	///     The ratchet of <c>productionExceptions</c>: lower it when an exception goes away, never raise it. A new raw
	///     raising call belongs below the bridge instead.
	/// </summary>
	private const int MaximumProductionExceptions = 3;

	private static readonly string[] Decisions =
		["DirectAllowed", "ConditionallyDirect", "BridgeRequired", "CallerProtected", "ForbiddenInSdk", "Lifecycle"];

	private static readonly string[] RaisesClasses = ["Never", "Memory", "Any", "Always", "NotApplicable"];

	private static readonly string[] RowRequired =
	[
		"member", "nativeSymbol", "raises", "mayRunMetamethod", "mayRunFinalizer", "mayYield", "stackEffect",
		"returnsBorrowedView", "decision", "bridgeOperation", "condition", "provenance"
	];

	[Fact]
	public void Primitive_matrix_matches_its_schema()
	{
		JsonSchemaSubset schema = LuaBridgeDocuments.MatrixSchema;
		JsonElement matrix = LuaBridgeDocuments.Matrix;

		IReadOnlyList<string> errors = schema.Validate(matrix);

		Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
		Assert.Equal(Kind, matrix.GetProperty("schema").GetString());
		int direct = IndexOf(matrix, "lua_gettop");
		int lifecycle = IndexOf(matrix, "Initialize");
		Assert.Contains(schema.Validate(Mutate(matrix, row => row["rows"]![direct]!["raises"] = "Memory")),
			error => error.Contains($"/rows/{direct}/raises", StringComparison.Ordinal));
		Assert.Contains(schema.Validate(Mutate(matrix, row => row["rows"]![lifecycle]!["nativeSymbol"] = "lua_close")),
			error => error.Contains($"/rows/{lifecycle}/nativeSymbol", StringComparison.Ordinal));
		Assert.Contains(schema.Validate(Mutate(matrix, row => row["rows"]![direct]!["condition"] = "always")),
			error => error.Contains($"/rows/{direct}/condition", StringComparison.Ordinal));
		Assert.Contains(schema.Validate(Mutate(matrix, row => row["rows"]![direct]!["callable"] = true)),
			static error => error.Contains("unexpected property 'callable'", StringComparison.Ordinal));
	}

	[Fact]
	public void Primitive_matrix_schema_is_closed_with_the_repository_id_and_validator_keywords()
	{
		JsonSchemaSubset schema = LuaBridgeDocuments.MatrixSchema;
		JsonElement defs = schema.Root.GetProperty("$defs");

		Assert.Equal(LuaBridgeDocuments.Draft202012, schema.Root.GetProperty("$schema").GetString());
		Assert.Equal(LuaBridgeDocuments.SchemaIdPrefix + LuaBridgeDocuments.MatrixSchemaPath,
			schema.Root.GetProperty("$id").GetString());
		List<string> problems = LuaBridgeDocuments.SchemaShapeProblems(schema);
		Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
		Assert.Equal(Decisions, Strings(defs.GetProperty("decision").GetProperty("enum")));
		Assert.Equal(RaisesClasses, Strings(defs.GetProperty("raises").GetProperty("enum")));
		Assert.Equal(RowRequired, Strings(defs.GetProperty("row").GetProperty("required")));
	}

	[Fact]
	public void Primitive_matrix_is_canonical_and_its_lists_are_sorted_and_unique()
	{
		string text = QualificationDocuments.ReadNormalizedText(LuaBridgeDocuments.MatrixPath);
		JsonElement matrix = QualificationDocuments.ParseJson(text);

		Assert.Equal(QualificationDocuments.Canonical(matrix), text);
		string[] members = [.. LuaBridgeDocuments.Rows.Select(static row => row.GetProperty("member").GetString()!)];
		Assert.Equal(members.Order(StringComparer.Ordinal), members, StringComparer.Ordinal);
		Assert.Equal(members.Length, members.Distinct(StringComparer.Ordinal).Count());
		string[] exceptions =
		[
			.. matrix.GetProperty("productionExceptions").EnumerateArray()
				.Select(static item => item.GetProperty("member").GetString() + "|" + item.GetProperty("file").GetString())
		];
		Assert.Equal(exceptions.Order(StringComparer.Ordinal), exceptions, StringComparer.Ordinal);
		Assert.Equal(exceptions.Length, exceptions.Distinct(StringComparer.Ordinal).Count());
	}

	[Fact]
	public void Rows_are_consistent_with_their_decision_bridge_operation_and_manual_marker()
	{
		HashSet<string> operations = new(LuaBridgeDocuments.Operations.Select(static operation =>
			operation.GetProperty("id").GetString()!), StringComparer.Ordinal);
		Dictionary<string, JsonElement> divergences = LuaBridgeDocuments.Matrix.GetProperty("manualDivergences")
			.EnumerateArray().ToDictionary(static item => item.GetProperty("member").GetString()!, StringComparer.Ordinal);
		List<string> problems = [];
		foreach (JsonElement row in LuaBridgeDocuments.Rows)
		{
			CheckRoute(row, operations, problems);
			CheckProvenance(row, divergences, problems);
		}

		Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
		HashSet<string> members = new(LuaBridgeDocuments.Rows.Select(static row => row.GetProperty("member").GetString()!),
			StringComparer.Ordinal);
		Assert.All(divergences.Keys, member => Assert.Contains(member, members));
	}

	[Fact]
	public void Audit_page_table_equals_the_primitive_matrix()
	{
		string page = QualificationDocuments.ReadNormalizedText(LuaBridgeDocuments.AuditPagePath);
		JsonElement matrix = LuaBridgeDocuments.Matrix;

		AssertBlock(page, "primitive-table", PrimitiveTable(matrix));
		AssertBlock(page, "divergence-table", Table(["Member", "Manual", "SDK remark", "Reason"],
			matrix.GetProperty("manualDivergences").EnumerateArray().Select(static item => (string[])
			[
				Code(item.GetProperty("member").GetString()), item.GetProperty("manualClass").GetString()!,
				item.GetProperty("sdkClass").GetString()!, Cell(item.GetProperty("reason").GetString()!)
			])));
		AssertBlock(page, "exception-table", Table(["Member", "File", "Why the call cannot raise"],
			matrix.GetProperty("productionExceptions").EnumerateArray().Select(static item => (string[])
			[
				Code(item.GetProperty("member").GetString()), Code(item.GetProperty("file").GetString()),
				Cell(item.GetProperty("justification").GetString()!)
			])));
		AssertBlock(page, "evidence-table", EvidenceTable());
	}

	[Fact]
	public void Production_raw_LuaApi_uses_are_classified_or_explicitly_excepted()
	{
		Dictionary<string, string> decisions = LuaBridgeDocuments.Rows.ToDictionary(
			static row => row.GetProperty("member").GetString()!,
			static row => row.GetProperty("decision").GetString()!, StringComparer.Ordinal);
		HashSet<string> excepted = new(LuaBridgeDocuments.Matrix.GetProperty("productionExceptions").EnumerateArray()
			.Select(static item => item.GetProperty("member").GetString() + "|" + item.GetProperty("file").GetString()),
			StringComparer.Ordinal);
		HashSet<string> members = new(decisions.Keys, StringComparer.Ordinal);
		HashSet<string> used = new(StringComparer.Ordinal);
		List<string> problems = [];
		foreach (string file in ProductionSources())
		{
			string source = File.ReadAllText(QualificationDocuments.Absolute(file), Encoding.UTF8);
			foreach (LuaApiUse use in LuaApiUseScanner.Scan(source, members))
			{
				string key = use.Member + "|" + file;
				used.Add(key);
				if (decisions[use.Member] is not ("DirectAllowed" or "Lifecycle") && !excepted.Contains(key))
				{
					problems.Add($"{file}:{use.Line}: {use.Member} is {decisions[use.Member]}; route it through the bridge or add a production exception.");
				}
			}
		}

		problems.AddRange(excepted.Where(key => !used.Contains(key))
			.Select(static key => $"Production exception {key} matches no raw use: remove it."));
		problems.AddRange(excepted.Where(key => !string.Equals(decisions.GetValueOrDefault(key.Split('|')[0]),
				"ConditionallyDirect", StringComparison.Ordinal))
			.Select(static key => $"Production exception {key} must name a ConditionallyDirect member."));
		Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
		Assert.InRange(excepted.Count, 0, MaximumProductionExceptions);
		Assert.Contains("lua_settop", used.Select(static key => key.Split('|')[0]), StringComparer.Ordinal);
	}

	[Fact]
	public void Scanner_sees_qualified_and_static_uses_and_ignores_comments_and_literals()
	{
		HashSet<string> members = new(["lua_tolstring", "lua_settop", "lua_gettop"], StringComparer.Ordinal);
		const string Source = """"
							  using static CheatEngine.SDK.Lua.Interop.Api.LuaApi;
							  // lua_gettop(L) in a comment
							  /* LuaApi.lua_gettop */
							  class C
							  {
							      string s = "lua_gettop(L)";
							      string v = @"LuaApi.lua_gettop ""quoted""";
							      void M(void* L) { lua_settop(L, 0); _ = LuaApi.lua_tolstring(L, -1, null); other.lua_gettop(); }
							  }
							  """";

		IReadOnlyList<LuaApiUse> uses = LuaApiUseScanner.Scan(Source, members);

		Assert.Equal([new LuaApiUse("lua_settop", 8), new LuaApiUse("lua_tolstring", 8)], uses);
		Assert.Empty(LuaApiUseScanner.Scan("class C { void M(void* L) { lua_settop(L, 0); } }", members));
	}

	[Fact]
	public void Checkstack_proof_record_matches_the_bundled_lua_and_the_lf_pinned_bridge_sources()
	{
		JsonElement proof = LuaBridgeDocuments.Matrix.GetProperty("bridgeProof");

		Assert.Equal(QualificationDocuments.RawSha256(LuaBridgeDocuments.LuaFixturePath),
			proof.GetProperty("luaFixtureSha256").GetString());
		Assert.Equal(QualificationDocuments.RawSha256(LuaBridgeDocuments.BridgeSourcePath),
			proof.GetProperty("bridgeSourceSha256").GetString());
		Assert.Equal(QualificationDocuments.RawSha256(LuaBridgeDocuments.XmakePath),
			proof.GetProperty("xmakeSha256").GetString());
		Assert.DoesNotContain((byte) '\r', File.ReadAllBytes(QualificationDocuments.Absolute(LuaBridgeDocuments.BridgeSourcePath)));

		string[] test = proof.GetProperty("checkstackEvidence").GetString()!.Split('.');
		string file = Assert.Single(RepositoryRoot.EnumerateSourceFiles(test[0] + ".cs"),
			static path => path.StartsWith("tests/", StringComparison.Ordinal));
		Assert.NotNull(TestSourceIndex.TraitsOfMethod(file, test[0], test[1]));
	}

	[Fact]
	public void Bundled_lua_fixture_hash_equals_the_catalogue_and_support_profile_hashes()
	{
		// SupportProfileTests.Profile_lua_hash_equals_the_committed_fixture_dll already ties the support profile to the
		// fixture; this test adds the two lua-bridge documents to the same identity.
		string fixture = QualificationDocuments.RawSha256(LuaBridgeDocuments.LuaFixturePath);
		JsonElement profile = SupportProfileRules.Find(
								  QualificationDocuments.LoadJson(QualificationDocuments.SupportProfilePath),
								  QualificationContract.QualifiableProfileId)
							  ?? throw new InvalidOperationException("The qualifiable profile is missing.");

		Assert.Equal(fixture, LuaBridgeDocuments.Matrix.GetProperty("bridgeProof").GetProperty("luaFixtureSha256").GetString());
		Assert.Equal(fixture, LuaBridgeDocuments.Catalogue.GetProperty("host").GetProperty("lua")
			.GetProperty("fixtureSha256").GetString(), ignoreCase: true);
		Assert.Equal(fixture, profile.GetProperty("lua").GetProperty("sha256").GetString());
	}

	private static void CheckRoute(JsonElement row, HashSet<string> operations, List<string> problems)
	{
		string member = row.GetProperty("member").GetString()!;
		string decision = row.GetProperty("decision").GetString()!;
		string raises = row.GetProperty("raises").GetString()!;
		string? bridge = LuaBridgeDocuments.OptionalString(row, "bridgeOperation");
		string? native = LuaBridgeDocuments.OptionalString(row, "nativeSymbol");
		if (raises is "Memory" or "Any" or "Always" && (decision is "DirectAllowed" or "Lifecycle"))
		{
			problems.Add($"{member}: a raising member cannot be {decision}.");
		}

		if (bridge is not null && !operations.Contains(bridge))
		{
			problems.Add($"{member}: bridge operation {bridge} is not in the catalogue.");
		}

		if (string.Equals(decision, "BridgeRequired", StringComparison.Ordinal) && bridge is null &&
			!string.Equals(member, "lua_error", StringComparison.Ordinal))
		{
			problems.Add($"{member}: BridgeRequired names no bridge operation (only lua_error, raised by the bridge's C code, may).");
		}

		if (native is not null && !string.Equals(native, member, StringComparison.Ordinal))
		{
			problems.Add($"{member}: the native symbol {native} is not the member name.");
		}
	}

	private static void CheckProvenance(JsonElement row, Dictionary<string, JsonElement> divergences, List<string> problems)
	{
		string member = row.GetProperty("member").GetString()!;
		JsonElement provenance = row.GetProperty("provenance");
		if (provenance.ValueKind == JsonValueKind.Null)
		{
			return;
		}

		string marker = provenance.GetProperty("marker").GetString()!;
		string source = provenance.GetProperty("source").GetString()!;
		if (marker.StartsWith("none:", StringComparison.Ordinal))
		{
			if (!member.StartsWith("luaopen_", StringComparison.Ordinal))
			{
				problems.Add($"{member}: only a luaopen_* value may lack a manual marker.");
			}

			return;
		}

		if (!string.Equals(source, ManualAnchorPrefix + member, StringComparison.Ordinal))
		{
			problems.Add($"{member}: the provenance must link the manual entry of {member}, found {source}.");
		}

		string manualClass = ClassOfMarker(marker);
		string raises = row.GetProperty("raises").GetString()!;
		bool agrees = string.Equals(manualClass, raises, StringComparison.Ordinal) ||
					  (string.Equals(manualClass, "Never", StringComparison.Ordinal) &&
					   string.Equals(raises, "NotApplicable", StringComparison.Ordinal));
		bool recorded = divergences.TryGetValue(member, out JsonElement divergence) &&
						string.Equals(divergence.GetProperty("manualClass").GetString(), manualClass, StringComparison.Ordinal) &&
						string.Equals(divergence.GetProperty("sdkClass").GetString(), raises, StringComparison.Ordinal);
		if (agrees == recorded)
		{
			problems.Add(agrees
				? $"{member}: a divergence is recorded, but the manual marker {marker} agrees with {raises}."
				: $"{member}: the manual marker {marker} says {manualClass} and the row says {raises}; record the divergence.");
		}
	}

	private static string ClassOfMarker(string marker)
	{
		return marker[^2] switch
		{
			'\u2013' => "Never",
			'm' => "Memory",
			'e' => "Any",
			'v' => "Always",
			_ => throw new InvalidOperationException($"Unknown manual marker {marker}.")
		};
	}

	private static IEnumerable<string> ProductionSources()
	{
		return RepositoryRoot.EnumerateSourceFiles("*.cs")
			.Where(static file => file.StartsWith("libs/", StringComparison.Ordinal) &&
								  !file.StartsWith(RawApiDirectory, StringComparison.Ordinal))
			.Order(StringComparer.Ordinal);
	}

	private static string PrimitiveTable(JsonElement matrix)
	{
		return Table(
			["Member", "Export", "Raises", "Manual marker", "Metamethod", "Finalizer", "Yield", "Borrowed", "Decision",
				"Bridge operation"],
			matrix.GetProperty("rows").EnumerateArray().Select(static row => (string[])
			[
				Code(row.GetProperty("member").GetString()), Code(LuaBridgeDocuments.OptionalString(row, "nativeSymbol")),
				row.GetProperty("raises").GetString()!, Marker(row), Yes(row, "mayRunMetamethod"),
				Yes(row, "mayRunFinalizer"), Yes(row, "mayYield"), Yes(row, "returnsBorrowedView"),
				row.GetProperty("decision").GetString()!, Code(LuaBridgeDocuments.OptionalString(row, "bridgeOperation"))
			]));
	}

	private static string EvidenceTable()
	{
		return Table(["Operation", "Raises", "Evidence"], LuaBridgeDocuments.Operations.Select(static operation =>
		{
			IEnumerable<string> evidence = operation.TryGetProperty("failureEvidence", out JsonElement items)
				? items.EnumerateArray().Select(static item => Code(
					LuaBridgeDocuments.OptionalString(item, "marker") ?? LuaBridgeDocuments.OptionalString(item, "test")))
				: [];
			return (string[])
			[
				Code(operation.GetProperty("id").GetString()), operation.GetProperty("raises").GetString()!,
				string.Join(", ", evidence)
			];
		}));
	}

	private static string Marker(JsonElement row)
	{
		JsonElement provenance = row.GetProperty("provenance");
		if (provenance.ValueKind == JsonValueKind.Null)
		{
			return string.Empty;
		}

		string marker = provenance.GetProperty("marker").GetString()!;
		return marker.StartsWith("none:", StringComparison.Ordinal) ? "none" : Code(Cell(marker));
	}

	private static string Yes(JsonElement row, string property)
	{
		return row.GetProperty(property).GetBoolean() ? "yes" : "no";
	}

	private static string Code(string? value)
	{
		return value is null ? string.Empty : "`" + value + "`";
	}

	private static string Cell(string value)
	{
		return value.Replace("|", "\\|", StringComparison.Ordinal);
	}

	private static string Table(string[] header, IEnumerable<string[]> rows)
	{
		StringBuilder table = new();
		table.Append("| ").AppendJoin(" | ", header).Append(" |\n|");
		table.Append(string.Concat(Enumerable.Repeat("---|", header.Length)));
		foreach (string[] row in rows)
		{
			table.Append("\n| ").AppendJoin(" | ", row).Append(" |");
		}

		return table.ToString();
	}

	private static void AssertBlock(string page, string name, string expected)
	{
		string actual = LuaBridgeDocuments.GeneratedBlock(page, name);
		Assert.True(string.Equals(expected, actual, StringComparison.Ordinal),
			$"The {name} block of {LuaBridgeDocuments.AuditPagePath} differs from the JSON. Expected block:\n{expected}");
	}

	private static JsonElement Mutate(JsonElement matrix, Action<JsonNode> change)
	{
		return LuaBridgeDocuments.Mutate(matrix, change);
	}

	private static int IndexOf(JsonElement matrix, string member)
	{
		int index = 0;
		foreach (JsonElement row in matrix.GetProperty("rows").EnumerateArray())
		{
			if (string.Equals(row.GetProperty("member").GetString(), member, StringComparison.Ordinal))
			{
				return index;
			}

			index++;
		}

		throw new InvalidOperationException($"No row for {member}.");
	}

	private static string[] Strings(JsonElement array)
	{
		return [.. array.EnumerateArray().Select(static item => item.GetString()!)];
	}
}
