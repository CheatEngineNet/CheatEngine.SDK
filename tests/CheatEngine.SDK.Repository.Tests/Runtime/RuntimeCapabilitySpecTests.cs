using System.Text.Json;
using System.Text.RegularExpressions;

using CheatEngine.SDK.Repository.Tests.Infrastructure;

namespace CheatEngine.SDK.Repository.Tests.Runtime;

/// <summary>
///     Text contracts of the runtime facts: the <c>runtime-capabilities</c> EngineApi spec is a wired, read-only
///     <c>contract: ce77</c> spec, and the <c>TargetBackend</c> vocabulary of the Engine equals the backend vocabulary of
///     the qualification support profile (audit A12-03, A17-18).
/// </summary>
public sealed partial class RuntimeCapabilitySpecTests
{
	private const string SpecPath =
		"source-generators/CheatEngine.SDK.SourceGenerators.EngineApi/Specs/runtime-capabilities.cheatengine-sdk-api.txt";

	private const string EngineProjectPath = "libs/CheatEngine.SDK.Engine/CheatEngine.SDK.Engine.csproj";

	private const string TargetBackendPath = "libs/CheatEngine.SDK.Engine/Runtime/TargetBackend.cs";

	/// <summary>
	///     The read-only globals the runtime observations may call (the allowlist of
	///     <c>runtime_probes_never_call_dbk_dbvm_open_process_or_setters</c>), minus <c>getOpenedProcessID</c>, plus the
	///     legacy <c>getCEVersion</c>.
	/// </summary>
	private static readonly SortedSet<string> s_readOnlyGlobals = new(StringComparer.Ordinal)
	{
		"getCEVersion", "isConnectedToCEServer", "targetIs64Bit", "targetIsX86", "targetIsArm", "targetIsAndroid",
		"getABI", "getPointerSize", "getCheatEngineFileVersion", "getSystemArchitecture", "cheatEngineIs64Bit",
		"getOperatingSystem"
	};

	/// <summary>Globals the spec must never bind: they change Cheat Engine or the target, or load a driver.</summary>
	private static readonly string[] s_forbiddenGlobals =
	[
		"setPointerSize", "setAssemblerMode", "openProcess", "openFileAsProcess", "pause", "unpause"
	];

	[Fact]
	public void runtime_capability_spec_uses_the_ce77_contract()
	{
		Dictionary<string, string> header = Header(ReadSpec());

		Assert.Equal("CheatEngine.SDK.Engine.Generated", header["namespace"]);
		Assert.Equal("RuntimeCapabilityProbes", header["type"]);
		Assert.Equal("ce77", header["contract"]);
		Assert.Equal("7.7.0.10621", header["minimum-ce"]);
		Assert.Equal("x64", header["architecture"]);
		Assert.Equal("unknown", header["thread"]);
		Assert.Equal("none", header["ownership"]);
		Assert.StartsWith("ExactInstalledFile: ", header["provenance"], StringComparison.Ordinal);
		Assert.Contains("AA1342B4A5D5D5C65B255FB3A8FD7B6BCBBAC1CD138961669D9F37F43E0B9C00", header["provenance"],
			StringComparison.Ordinal);
		foreach (Dictionary<string, string> entry in Entries(ReadSpec()))
		{
			Assert.Equal("none", entry.GetValueOrDefault("nil"));
			Assert.Equal("throwing", entry.GetValueOrDefault("form"));
		}
	}

	[Fact]
	public void runtime_capability_spec_binds_only_read_only_runtime_globals()
	{
		SortedSet<string> bound = new(StringComparer.Ordinal);
		foreach (Dictionary<string, string> entry in Entries(ReadSpec()))
		{
			bound.Add(entry["global"]);
		}

		Assert.Equal(
			[
				"cheatEngineIs64Bit", "getABI", "getCEVersion", "getOperatingSystem", "getPointerSize",
				"getSystemArchitecture", "isConnectedToCEServer", "targetIs64Bit", "targetIsAndroid", "targetIsArm",
				"targetIsX86"
			],
			bound);
		Assert.Subset(s_readOnlyGlobals, bound);
		foreach (string global in bound)
		{
			Assert.DoesNotContain(global, s_forbiddenGlobals, StringComparer.Ordinal);
			Assert.False(global.StartsWith("dbk_", StringComparison.Ordinal), global);
			Assert.False(global.StartsWith("dbvm_", StringComparison.Ordinal), global);
		}
	}

	[Fact]
	public void engine_project_generates_the_runtime_capability_spec()
	{
		XDocument project = XDocument.Load(Path.Combine(RepositoryRoot.Path, EngineProjectPath));
		List<string> specs = [];
		foreach (XElement item in project.Descendants("AdditionalFiles"))
		{
			specs.Add((string?) item.Attribute("Include") ?? "");
		}

		Assert.Contains("../../" + SpecPath, specs, StringComparer.Ordinal);
		Assert.Contains(
			"../../source-generators/CheatEngine.SDK.SourceGenerators.EngineApi/Specs/memory-scalars.cheatengine-sdk-api.txt",
			specs, StringComparer.Ordinal);
	}

	[Fact]
	public void support_profile_qualified_backends_name_target_backend_members()
	{
		string source = File.ReadAllText(Path.Combine(RepositoryRoot.Path, TargetBackendPath));
		SortedSet<string> members = new(StringComparer.Ordinal);
		foreach (Match member in EnumMemberPattern().Matches(source))
		{
			members.Add(member.Groups["name"].Value);
		}

		using JsonDocument profile = JsonDocument.Parse(File.ReadAllText(
			Path.Combine(RepositoryRoot.Path, "docs", "qualification", "support-profile.json")));
		using JsonDocument schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot.Path, "docs",
			"qualification", "schemas", "support-profile.v0.schema.json")));
		SortedSet<string> vocabulary = new(StringComparer.Ordinal);
		foreach (JsonElement backend in schema.RootElement.GetProperty("$defs").GetProperty("profile")
					 .GetProperty("properties").GetProperty("qualifiedBackends").GetProperty("items").GetProperty("enum")
					 .EnumerateArray())
		{
			vocabulary.Add(backend.GetString()!);
		}

		List<string> qualified = [];
		foreach (JsonElement entry in profile.RootElement.GetProperty("profiles").EnumerateArray())
		{
			if (entry.TryGetProperty("qualifiedBackends", out JsonElement backends))
			{
				foreach (JsonElement backend in backends.EnumerateArray())
				{
					qualified.Add(backend.GetString()!);
				}
			}
		}

		Assert.Equal(["CEServer", "FileAsProcess", "LocalProcess", "Unknown"], members);
		SortedSet<string> known = new(members, StringComparer.Ordinal);
		known.Remove("Unknown");
		Assert.Equal(known, vocabulary);
		Assert.Equal(["LocalProcess"], qualified);
		Assert.Subset(members, new HashSet<string>(qualified, StringComparer.Ordinal));
	}

	[GeneratedRegex(@"^\t(?<name>[A-Z][A-Za-z]*) = \d+,?\r?$", RegexOptions.Multiline | RegexOptions.CultureInvariant,
		1000)]
	private static partial Regex EnumMemberPattern();

	private static string[] ReadSpec()
	{
		return File.ReadAllLines(Path.Combine(RepositoryRoot.Path, SpecPath));
	}

	// The EngineApi spec format: blocks separated by blank lines, '#' comment lines ignored, 'key: value' lines.
	private static List<Dictionary<string, string>> Blocks(string[] lines)
	{
		List<Dictionary<string, string>> blocks = [];
		Dictionary<string, string>? current = null;
		foreach (string raw in lines)
		{
			string line = raw.Trim();
			if (line.StartsWith('#'))
			{
				continue;
			}

			if (line.Length == 0)
			{
				current = null;
				continue;
			}

			if (current is null)
			{
				current = new Dictionary<string, string>(StringComparer.Ordinal);
				blocks.Add(current);
			}

			int colon = line.IndexOf(':', StringComparison.Ordinal);
			Assert.True(colon > 0, $"Not a 'key: value' line: {line}");
			current[line[..colon]] = line[(colon + 1)..].Trim();
		}

		return blocks;
	}

	private static Dictionary<string, string> Header(string[] lines)
	{
		return Blocks(lines)[0];
	}

	private static List<Dictionary<string, string>> Entries(string[] lines)
	{
		List<Dictionary<string, string>> blocks = Blocks(lines);
		blocks.RemoveAt(0);
		Assert.NotEmpty(blocks);
		return blocks;
	}
}
