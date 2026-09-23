using System.Text;
using System.Text.Json;

using CheatEngine.SDK.Repository.Tests.Abi;
using CheatEngine.SDK.Repository.Tests.Infrastructure;
using CheatEngine.SDK.Repository.Tests.SourceScanning;

namespace CheatEngine.SDK.Repository.Tests.LuaBridge;

/// <summary>
///     The committed Lua interop primitive matrix (test-owned data, never a top-level <c>docs/</c> folder or a bespoke
///     <c>eng/</c> tool): one row per public static <c>LuaApi</c> member with its Lua 5.3 error class, side effects and
///     the route SDK production code may take (audit SDK-LUA-2, A05-02/05/08, A18-09). These tests read committed
///     files only; the reflection and XML-documentation checks of the same rows run in
///     <c>LuaInteropPrimitiveMatrixTests</c> of the Lua.Interop tests.
/// </summary>
public sealed class LuaInteropPrimitiveMatrixDocumentTests
{
	/// <summary>
	///     The ratchet of <c>productionExceptions</c>: lower it when an exception goes away, never raise it. A new raw
	///     raising call belongs below the bridge instead.
	/// </summary>
	private const int MaximumProductionExceptions = 3;

	private const string ManualAnchorPrefix = "https://www.lua.org/manual/5.3/manual.html#";
	private const string RawApiDirectory = "libs/CheatEngine.SDK.Lua.Interop/Api/";

	[Fact]
	public void Primitive_matrix_has_its_schema_identity()
	{
		Assert.Equal("cheatengine-lua-interop-primitives/v0",
			LuaBridgeDocuments.Matrix.GetProperty("schema").GetString());
	}

	[Fact]
	public void Primitive_matrix_is_canonical_and_its_lists_are_sorted_and_unique()
	{
		string text = RepositoryDocument.ReadNormalizedText(LuaBridgeDocuments.MatrixPath);
		JsonElement matrix = RepositoryDocument.ParseJson(text);

		Assert.Equal(RepositoryDocument.Canonical(matrix), text);
		string[] members = [.. LuaBridgeDocuments.Rows.Select(static row => row.GetProperty("member").GetString()!)];
		Assert.Equal(members.Order(StringComparer.Ordinal), members, StringComparer.Ordinal);
		Assert.Equal(members.Length, members.Distinct(StringComparer.Ordinal).Count());
		string[] exceptions =
		[
			.. matrix.GetProperty("productionExceptions").EnumerateArray()
				.Select(static item =>
					item.GetProperty("member").GetString() + "|" + item.GetProperty("file").GetString())
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
			.EnumerateArray().ToDictionary(static item => item.GetProperty("member").GetString()!,
				StringComparer.Ordinal);
		List<string> problems = [];
		foreach (JsonElement row in LuaBridgeDocuments.Rows)
		{
			CheckRoute(row, operations, problems);
			CheckProvenance(row, divergences, problems);
		}

		Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
		HashSet<string> members = new(
			LuaBridgeDocuments.Rows.Select(static row => row.GetProperty("member").GetString()!),
			StringComparer.Ordinal);
		Assert.All(divergences.Keys, member => Assert.Contains(member, members));
	}

	[Fact]
	public void Production_raw_LuaApi_uses_are_classified_or_explicitly_excepted()
	{
		Dictionary<string, string> decisions = LuaBridgeDocuments.Rows.ToDictionary(
			static row => row.GetProperty("member").GetString()!,
			static row => row.GetProperty("decision").GetString()!, StringComparer.Ordinal);
		HashSet<string> excepted = new(LuaBridgeDocuments.Matrix.GetProperty("productionExceptions").EnumerateArray()
				.Select(static item =>
					item.GetProperty("member").GetString() + "|" + item.GetProperty("file").GetString()),
			StringComparer.Ordinal);
		HashSet<string> members = new(decisions.Keys, StringComparer.Ordinal);
		HashSet<string> used = new(StringComparer.Ordinal);
		List<string> problems = [];
		foreach (string file in ProductionSources())
		{
			string source = File.ReadAllText(RepositoryDocument.Absolute(file), Encoding.UTF8);
			foreach (LuaApiUse use in LuaApiUseScanner.Scan(source, members))
			{
				string key = use.Member + "|" + file;
				used.Add(key);
				if (decisions[use.Member] is not ("DirectAllowed" or "Lifecycle") && !excepted.Contains(key))
				{
					problems.Add(
						$"{file}:{use.Line}: {use.Member} is {decisions[use.Member]}; route it through the bridge or add a production exception.");
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

		Assert.Equal(RepositoryDocument.RawSha256(LuaBridgeDocuments.LuaFixturePath),
			proof.GetProperty("luaFixtureSha256").GetString());
		Assert.Equal(RepositoryDocument.RawSha256(LuaBridgeDocuments.BridgeSourcePath),
			proof.GetProperty("bridgeSourceSha256").GetString());
		Assert.Equal(RepositoryDocument.RawSha256(LuaBridgeDocuments.XmakePath),
			proof.GetProperty("xmakeSha256").GetString());
		Assert.DoesNotContain((byte) '\r',
			File.ReadAllBytes(RepositoryDocument.Absolute(LuaBridgeDocuments.BridgeSourcePath)));

		string[] test = proof.GetProperty("checkstackEvidence").GetString()!.Split('.');
		string file = Assert.Single(RepositoryRoot.EnumerateSourceFiles(test[0] + ".cs"),
			static path => path.StartsWith("tests/", StringComparison.Ordinal));
		Assert.NotNull(TestMethodTraits.Of(file, test[1]));
	}

	[Fact]
	public void Bundled_lua_fixture_hash_equals_the_catalogue_and_primitive_matrix_hashes()
	{
		string fixture = RepositoryDocument.RawSha256(LuaBridgeDocuments.LuaFixturePath);

		Assert.Equal(fixture,
			LuaBridgeDocuments.Matrix.GetProperty("bridgeProof").GetProperty("luaFixtureSha256").GetString());
		Assert.Equal(fixture, LuaBridgeDocuments.Catalogue.GetProperty("host").GetProperty("lua")
			.GetProperty("fixtureSha256").GetString(), true);
	}

	private static void CheckRoute(JsonElement row, HashSet<string> operations, List<string> problems)
	{
		string member = row.GetProperty("member").GetString()!;
		string decision = row.GetProperty("decision").GetString()!;
		string raises = row.GetProperty("raises").GetString()!;
		string? bridge = LuaBridgeDocuments.OptionalString(row, "bridgeOperation");
		string? native = LuaBridgeDocuments.OptionalString(row, "nativeSymbol");
		if (raises is "Memory" or "Any" or "Always" && decision is "DirectAllowed" or "Lifecycle")
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
			problems.Add(
				$"{member}: BridgeRequired names no bridge operation (only lua_error, raised by the bridge's C code, may).");
		}

		if (native is not null && !string.Equals(native, member, StringComparison.Ordinal))
		{
			problems.Add($"{member}: the native symbol {native} is not the member name.");
		}
	}

	private static void CheckProvenance(JsonElement row, Dictionary<string, JsonElement> divergences,
		List<string> problems)
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
						string.Equals(divergence.GetProperty("manualClass").GetString(), manualClass,
							StringComparison.Ordinal) &&
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
			'–' => "Never",
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
}
