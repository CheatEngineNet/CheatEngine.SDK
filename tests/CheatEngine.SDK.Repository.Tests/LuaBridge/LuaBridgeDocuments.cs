using System.Text.Json;
using System.Text.Json.Nodes;

using CheatEngine.SDK.Repository.Tests.Abi;

namespace CheatEngine.SDK.Repository.Tests.LuaBridge;

/// <summary>
///     Paths and readers shared by the tests of the protected-operation catalogue and of the Lua interop primitive
///     matrix. Both documents are read as committed (UTF-8, CRLF normalized to LF); neither carries a JSON Schema file
///     any more (the maintainer's no-custom-scripting pivot removed that infrastructure), so every shape rule is a
///     plain C# assertion in the tests that use these readers.
/// </summary>
internal static class LuaBridgeDocuments
{
	internal const string CataloguePath = "libs/CheatEngine.SDK.Lua.Interop/Protected/protected-operations.json";
	internal const string MatrixPath = "tests/CheatEngine.SDK.Repository.Tests/LuaBridge/TestData/lua-interop-primitives.json";
	internal const string BridgeSourcePath = "native/cheatengine-sdk-lua-bridge/cheatengine_sdk_lua_bridge.c";
	internal const string XmakePath = "native/cheatengine-sdk-lua-bridge/xmake.lua";
	internal const string LuaFixturePath = "native/cheat-engine/lua53-64.dll";
	internal const string FailureProbePath = "tests/CheatEngine.SDK.Lua.FailureProbe/Program.cs";

	internal static JsonElement Catalogue => RepositoryDocument.LoadJson(CataloguePath);

	internal static JsonElement Matrix => RepositoryDocument.LoadJson(MatrixPath);

	internal static JsonElement[] Operations => [.. Catalogue.GetProperty("operations").EnumerateArray()];

	internal static JsonElement[] Rows => [.. Matrix.GetProperty("rows").EnumerateArray()];

	/// <summary>A copy of <paramref name="document" /> after <paramref name="change" />, for refusal checks.</summary>
	internal static JsonElement Mutate(JsonElement document, Action<JsonNode> change)
	{
		JsonNode root = JsonNode.Parse(document.GetRawText())!;
		change(root);
		return RepositoryDocument.ParseJson(root.ToJsonString());
	}

	/// <summary>The string value of <paramref name="property" />, or <see langword="null" /> when it is absent or null.</summary>
	internal static string? OptionalString(JsonElement element, string property)
	{
		return element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;
	}
}
