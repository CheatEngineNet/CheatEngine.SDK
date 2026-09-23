using System.Text.Json;
using System.Text.Json.Nodes;

using CheatEngine.SDK.Repository.Tests.Qualification.Validation;

namespace CheatEngine.SDK.Repository.Tests.LuaBridge;

/// <summary>
///     Paths and readers shared by the tests of the protected-operation catalogue and of the Lua interop primitive
///     matrix. Both documents are read as committed (UTF-8, CRLF normalized to LF) and validated with the repository's
///     <see cref="JsonSchemaSubset" />, never with a keyword it would silently ignore.
/// </summary>
internal static class LuaBridgeDocuments
{
	internal const string CataloguePath = "libs/CheatEngine.SDK.Lua.Interop/Protected/protected-operations.json";
	internal const string CatalogueSchemaPath = "eng/lua-bridge/protected-operations.schema.json";
	internal const string MatrixPath = "tests/CheatEngine.SDK.Repository.Tests/LuaBridge/TestData/lua-interop-primitives.json";
	internal const string MatrixSchemaPath = "eng/lua-bridge/lua-interop-primitives.v0.schema.json";
	internal const string CatalogueReadmePath = "eng/lua-bridge/README.md";
	internal const string AuditPagePath = "libs/CheatEngine.SDK.Lua.Interop/README.md";
	internal const string BridgeSourcePath = "native/cheatengine-sdk-lua-bridge/cheatengine_sdk_lua_bridge.c";
	internal const string XmakePath = "native/cheatengine-sdk-lua-bridge/xmake.lua";
	internal const string LuaFixturePath = "native/cheat-engine/lua53-64.dll";
	internal const string FailureProbePath = "tests/CheatEngine.SDK.Lua.FailureProbe/Program.cs";
	internal const string SchemaIdPrefix = "https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/";
	internal const string Draft202012 = "https://json-schema.org/draft/2020-12/schema";

	internal static JsonElement Catalogue => QualificationDocuments.LoadJson(CataloguePath);

	internal static JsonElement Matrix => QualificationDocuments.LoadJson(MatrixPath);

	internal static JsonSchemaSubset CatalogueSchema => Schema(CatalogueSchemaPath);

	internal static JsonSchemaSubset MatrixSchema => Schema(MatrixSchemaPath);

	internal static JsonElement[] Operations => [.. Catalogue.GetProperty("operations").EnumerateArray()];

	internal static JsonElement[] Rows => [.. Matrix.GetProperty("rows").EnumerateArray()];

	internal static JsonSchemaSubset Schema(string repositoryRelativePath)
	{
		return JsonSchemaSubset.Parse(QualificationDocuments.ReadNormalizedText(repositoryRelativePath),
			Path.GetFileName(repositoryRelativePath));
	}

	/// <summary>A copy of <paramref name="document" /> after <paramref name="change" />, for refusal checks.</summary>
	internal static JsonElement Mutate(JsonElement document, Action<JsonNode> change)
	{
		JsonNode root = JsonNode.Parse(document.GetRawText())!;
		change(root);
		return QualificationDocuments.ParseJson(root.ToJsonString());
	}

	/// <summary>The string value of <paramref name="property" />, or <see langword="null" /> when it is absent or null.</summary>
	internal static string? OptionalString(JsonElement element, string property)
	{
		return element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;
	}

	/// <summary>
	///     One message per keyword outside <see cref="JsonSchemaSubset.SupportedKeywords" /> and per object schema that
	///     does not set <c>"additionalProperties": false</c>.
	/// </summary>
	internal static List<string> SchemaShapeProblems(JsonSchemaSubset schema)
	{
		List<string> problems = [];
		foreach ((string pointer, JsonElement node) in schema.EnumerateSchemaObjects())
		{
			foreach (JsonProperty keyword in node.EnumerateObject())
			{
				if (!JsonSchemaSubset.SupportedKeywords.Contains(keyword.Name))
				{
					problems.Add($"{pointer}: '{keyword.Name}' is not implemented by JsonSchemaSubset.");
				}
			}

			bool isObject = node.TryGetProperty("type", out JsonElement type) &&
							type.GetRawText().Contains("\"object\"", StringComparison.Ordinal);
			if (isObject && (!node.TryGetProperty("additionalProperties", out JsonElement additional) ||
							 additional.ValueKind != JsonValueKind.False))
			{
				problems.Add($"{pointer}: an object schema must set \"additionalProperties\": false.");
			}
		}

		return problems;
	}

	/// <summary>The lines between <c>&lt;!-- BEGIN GENERATED: name --&gt;</c> and its END marker, LF-joined.</summary>
	internal static string GeneratedBlock(string page, string name)
	{
		string begin = $"<!-- BEGIN GENERATED: {name} -->\n";
		string end = $"\n<!-- END GENERATED: {name} -->";
		int from = page.IndexOf(begin, StringComparison.Ordinal);
		int to = page.IndexOf(end, StringComparison.Ordinal);
		Assert.True(from >= 0 && to > from, $"The page lacks the {name} markers.");
		return page[(from + begin.Length)..to];
	}
}
