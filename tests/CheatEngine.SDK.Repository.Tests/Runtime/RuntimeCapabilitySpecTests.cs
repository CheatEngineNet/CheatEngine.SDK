using System.Text.RegularExpressions;

using CheatEngine.SDK.Repository.Tests.Infrastructure;

namespace CheatEngine.SDK.Repository.Tests.Runtime;

/// <summary>
///     Text contracts of the runtime facts: the <c>runtime-capabilities</c> EngineApi spec is a wired, read-only
///     <c>contract: ce77</c> spec, and the <c>TargetBackend</c> members are exactly the rows of the Engine README backend
///     table, where <c>LocalProcess</c> is the only qualified backend (audit A12-03, A17-18).
/// </summary>
public sealed partial class RuntimeCapabilitySpecTests
{
	private const string SpecPath =
		"source-generators/CheatEngine.SDK.SourceGenerators.EngineApi/Specs/runtime-capabilities.cheatengine-sdk-api.txt";

	private const string EngineProjectPath = "libs/CheatEngine.SDK.Engine/CheatEngine.SDK.Engine.csproj";

	private const string TargetBackendPath = "libs/CheatEngine.SDK.Engine/Runtime/TargetBackend.cs";

	private const string EngineReadmePath = "libs/CheatEngine.SDK.Engine/README.md";

	/// <summary>
	///     The read-only globals the runtime observations may call (the allowlist of
	///     <c>runtime_probes_never_call_dbk_dbvm_open_process_or_setters</c>), minus <c>getOpenedProcessID</c>, plus the
	///     legacy <c>getCEVersion</c>.
	/// </summary>
	private static readonly SortedSet<string> s_readOnlyGlobals = new(StringComparer.Ordinal)
	{
		"getCEVersion",
		"isConnectedToCEServer",
		"targetIs64Bit",
		"targetIsX86",
		"targetIsArm",
		"targetIsAndroid",
		"getABI",
		"getPointerSize",
		"getCheatEngineFileVersion",
		"getSystemArchitecture",
		"cheatEngineIs64Bit",
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
	public void target_backend_members_are_the_readme_backend_rows_and_only_local_process_is_qualified()
	{
		string source = File.ReadAllText(Path.Combine(RepositoryRoot.Path, TargetBackendPath));
		SortedSet<string> members = new(StringComparer.Ordinal);
		foreach (Match member in EnumMemberPattern().Matches(source))
		{
			members.Add(member.Groups["name"].Value);
		}

		SortedSet<string> rows = new(StringComparer.Ordinal);
		List<string> qualified = [];
		foreach (string[] cells in BackendTableRows(
			         File.ReadAllLines(Path.Combine(RepositoryRoot.Path, EngineReadmePath))))
		{
			string backend = cells[0].Trim('`');
			Assert.True(rows.Add(backend), $"Duplicate backend row {backend}.");
			string verdict = cells[^1];
			Assert.True(
				string.Equals(verdict, "no", StringComparison.Ordinal) ||
				verdict.StartsWith("yes, ", StringComparison.Ordinal),
				$"The qualified cell of {backend} is neither 'no' nor 'yes, <profile>': {verdict}");
			if (verdict.StartsWith("yes", StringComparison.Ordinal))
			{
				qualified.Add(backend);
			}
		}

		Assert.Equal(["CEServer", "FileAsProcess", "LocalProcess", "Unknown"], members);
		Assert.Equal(members, rows);
		Assert.Equal(["LocalProcess"], qualified);
	}

	// The rows of the README table whose first header cell is "Backend" and last header cell is "Qualified backend", as
	// trimmed cells without the outer pipes; the separator row is skipped and the table ends at the first non-table line.
	private static List<string[]> BackendTableRows(string[] lines)
	{
		List<string[]> rows = [];
		int header = -1;
		for (int index = 0; index < lines.Length; index++)
		{
			if (!lines[index].StartsWith('|'))
			{
				continue;
			}

			string[] cells = Cells(lines[index]);
			if (string.Equals(cells[0], "Backend", StringComparison.Ordinal)
			    && string.Equals(cells[^1], "Qualified backend", StringComparison.Ordinal))
			{
				Assert.Equal(-1, header);
				header = index;
			}
		}

		Assert.True(header >= 0, $"{EngineReadmePath} has no backend table with a 'Qualified backend' column.");
		for (int index = header + 2; index < lines.Length && lines[index].StartsWith('|'); index++)
		{
			rows.Add(Cells(lines[index]));
		}

		Assert.NotEmpty(rows);
		return rows;
	}

	private static string[] Cells(string line)
	{
		string[] cells = line.Trim().Trim('|').Split('|');
		for (int cell = 0; cell < cells.Length; cell++)
		{
			cells[cell] = cells[cell].Trim();
		}

		return cells;
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
