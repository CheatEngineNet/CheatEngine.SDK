using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using CheatEngine.SDK.Lua.Interop.Api;

namespace CheatEngine.SDK.Lua.Interop.Tests.Protected;

/// <summary>
///     The primitive matrix <c>tests/CheatEngine.SDK.Repository.Tests/LuaBridge/TestData/lua-interop-primitives.json</c> (embedded) against the real
///     <see cref="LuaApi" />: one row per public static member, the error class and stack effect its XML documentation
///     states, the exports the function-pointer table binds, and the bridge catalogue's direct-call policy. DLL-free.
/// </summary>
public sealed partial class LuaInteropPrimitiveMatrixTests
{
	private const string MatrixResource = "CheatEngine.SDK.Lua.Interop.Tests.LuaInteropPrimitives.json";
	private const string CatalogueResource = "CheatEngine.SDK.Lua.Interop.Tests.ProtectedOperations.json";
	private const string DocumentationFile = "CheatEngine.SDK.Lua.Interop.xml";
	private const string MemberPrefix = "CheatEngine.SDK.Lua.Interop.Api.LuaApi.";

	private static readonly Dictionary<string, string> RaisesFromRemark = new(StringComparer.Ordinal)
	{
		["never"] = "Never",
		["memory"] = "Memory",
		["any"] = "Any",
		["always"] = "Always"
	};

	[Fact]
	public void Every_public_LuaApi_member_has_exactly_one_matrix_row()
	{
		string[] rows = [.. Rows().Select(static row => row.GetProperty("member").GetString()!)];

		Assert.Equal(rows.Length, rows.Distinct(StringComparer.Ordinal).Count());
		string[] missing = [.. PublicMembers().Where(member => !rows.Contains(member, StringComparer.Ordinal))];
		Assert.True(missing.Length == 0, "No matrix row for: " + string.Join(", ", missing));
	}

	[Fact]
	public void Every_matrix_row_names_an_existing_LuaApi_member()
	{
		HashSet<string> members = PublicMembers();

		string[] unknown = [.. Rows().Select(static row => row.GetProperty("member").GetString()!)
			.Where(member => !members.Contains(member))];

		Assert.True(unknown.Length == 0, "Rows without a public static LuaApi member: " + string.Join(", ", unknown));
	}

	[Fact]
	public void Matrix_raises_class_equals_the_documented_raises_remark()
	{
		string path = Path.Combine(AppContext.BaseDirectory, DocumentationFile);
		Assert.True(File.Exists(path), $"The XML documentation of CheatEngine.SDK.Lua.Interop was not copied to '{path}'.");
		Dictionary<string, string> remarks = ReadRemarks(XDocument.Load(path));
		List<string> problems = [];
		foreach (JsonElement row in Rows())
		{
			string member = row.GetProperty("member").GetString()!;
			Match remark = StackAndRaises().Match(remarks.GetValueOrDefault(member, string.Empty));
			string expectedRaises = remark.Success ? RaisesFromRemark[remark.Groups["raises"].Value] : "NotApplicable";
			string? expectedStack = remark.Success ? remark.Groups["stack"].Value : null;
			string? stack = row.GetProperty("stackEffect").ValueKind == JsonValueKind.Null
				? null
				: row.GetProperty("stackEffect").GetString();
			if (!string.Equals(expectedRaises, row.GetProperty("raises").GetString(), StringComparison.Ordinal) ||
				!string.Equals(expectedStack, stack, StringComparison.Ordinal))
			{
				problems.Add($"{member}: documented {expectedStack ?? "no stack"} / {expectedRaises}, matrix {stack ?? "no stack"} / {row.GetProperty("raises").GetString()}.");
			}
		}

		Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
		Assert.True(remarks.Count > 100, "The XML documentation lost the LuaApi remarks.");
	}

	[Fact]
	public void Native_symbols_are_exactly_the_exports_the_table_binds()
	{
		// The main program module exports none of the table's names, so every requested export is reported missing.
		IReadOnlyList<string> exports = LuaApi.GetMissingExports(NativeLibrary.GetMainProgramHandle());

		string[] nativeSymbols =
		[
			.. Rows().Where(static row => row.GetProperty("nativeSymbol").ValueKind == JsonValueKind.String)
				.Select(static row => row.GetProperty("nativeSymbol").GetString()!)
		];

		Assert.Equal(exports.Order(StringComparer.Ordinal), nativeSymbols.Order(StringComparer.Ordinal));
	}

	[Fact]
	public void Direct_api_policy_entries_agree_with_the_matrix()
	{
		Dictionary<string, JsonElement> rows = Rows().ToDictionary(static row => row.GetProperty("member").GetString()!,
			StringComparer.Ordinal);
		using JsonDocument catalogue = Load(CatalogueResource);
		Dictionary<string, JsonElement> operations = catalogue.RootElement.GetProperty("operations").EnumerateArray()
			.ToDictionary(static operation => operation.GetProperty("id").GetString()!, StringComparer.Ordinal);
		List<string> problems = [];
		foreach (JsonElement policy in catalogue.RootElement.GetProperty("directApiPolicy").EnumerateArray())
		{
			string member = policy.GetProperty("managedSymbol").GetString()![MemberPrefix.Length..];
			if (!rows.TryGetValue(member, out JsonElement row))
			{
				problems.Add($"{member}: the policy names a member without a matrix row.");
				continue;
			}

			CheckPolicy(member, policy, row, operations, problems);
		}

		Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
	}

	private static void CheckPolicy(string member, JsonElement policy, JsonElement row,
		Dictionary<string, JsonElement> operations, List<string> problems)
	{
		string decision = row.GetProperty("decision").GetString()!;
		string? bridge = Optional(row, "bridgeOperation");
		string expectedDecision = policy.GetProperty("allowedDirectly").GetBoolean()
			? "DirectAllowed"
			: policy.TryGetProperty("conditionalDirectUse", out _) ? "ConditionallyDirect" : "BridgeRequired";
		if (!string.Equals(expectedDecision, decision, StringComparison.Ordinal) ||
			!string.Equals(Optional(policy, "bridgeOperation"), bridge, StringComparison.Ordinal) ||
			!string.Equals(policy.GetProperty("nativeSymbol").GetString(), Optional(row, "nativeSymbol"), StringComparison.Ordinal))
		{
			problems.Add($"{member}: the policy route ({expectedDecision}, {Optional(policy, "bridgeOperation")}) differs from the row ({decision}, {bridge}).");
		}

		int policyRank = Rank(policy.GetProperty("raises").GetString()!);
		int rowRank = Rank(row.GetProperty("raises").GetString()!);
		// The policy may only be more conservative than the documented class, and only where the bridge operation
		// records the source conflict that justifies it (luaL_unref: documented never, implemented with lua_rawseti).
		bool conflictRecorded = bridge is not null && operations.TryGetValue(bridge, out JsonElement operation) &&
								operation.TryGetProperty("provenanceConflict", out _);
		if (policyRank != rowRank && !(policyRank > rowRank && conflictRecorded))
		{
			problems.Add($"{member}: the policy classifies it {policy.GetProperty("raises").GetString()}, the matrix {row.GetProperty("raises").GetString()}.");
		}
	}

	private static int Rank(string raises)
	{
		return raises switch
		{
			"never" or "Never" or "NotApplicable" => 0,
			"memory" or "Memory" => 1,
			"any" or "Any" or "Always" => 2,
			_ => throw new InvalidOperationException($"Unknown raises class {raises}.")
		};
	}

	private static string? Optional(JsonElement element, string property)
	{
		return element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;
	}

	private static HashSet<string> PublicMembers()
	{
		const BindingFlags Flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
		HashSet<string> members = new(StringComparer.Ordinal);
		foreach (MethodInfo method in typeof(LuaApi).GetMethods(Flags).Where(static method => !method.IsSpecialName))
		{
			members.Add(method.Name);
		}

		// The LUA_* properties are header constants (library names, the signature), not callable primitives.
		foreach (PropertyInfo property in typeof(LuaApi).GetProperties(Flags)
					 .Where(static property => !property.Name.StartsWith("LUA_", StringComparison.Ordinal)))
		{
			members.Add(property.Name);
		}

		return members;
	}

	private static Dictionary<string, string> ReadRemarks(XDocument documentation)
	{
		Dictionary<string, string> remarks = new(StringComparer.Ordinal);
		foreach (XElement member in documentation.Descendants("member"))
		{
			string name = (string?) member.Attribute("name") ?? string.Empty;
			if (name.Length < 2 || !name[2..].StartsWith(MemberPrefix, StringComparison.Ordinal))
			{
				continue;
			}

			string shortName = name[(2 + MemberPrefix.Length)..].Split('(')[0];
			string text = string.Join(' ', (member.Element("remarks")?.Value ?? string.Empty)
				.Split((char[]) [' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
			remarks[shortName] = text;
		}

		return remarks;
	}

	private static JsonElement[] Rows()
	{
		using JsonDocument matrix = Load(MatrixResource);
		return [.. matrix.RootElement.GetProperty("rows").EnumerateArray().Select(static row => row.Clone())];
	}

	private static JsonDocument Load(string resource)
	{
		using Stream stream = typeof(LuaInteropPrimitiveMatrixTests).Assembly.GetManifestResourceStream(resource)
							  ?? throw new InvalidOperationException($"The embedded resource {resource} is missing.");
		return JsonDocument.Parse(stream);
	}

	[GeneratedRegex(@"Stack: (?<stack>.*?)\. Raises: (?<raises>\w+)", RegexOptions.CultureInvariant,
		matchTimeoutMilliseconds: 1000)]
	private static partial Regex StackAndRaises();
}
