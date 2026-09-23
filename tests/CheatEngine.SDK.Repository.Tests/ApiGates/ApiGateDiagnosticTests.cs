using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using CheatEngine.SDK.Repository.Tests.Infrastructure;

namespace CheatEngine.SDK.Repository.Tests.ApiGates;

/// <summary>
///     Every <c>[Experimental("CESDKxxxx")]</c> and <c>[Obsolete(DiagnosticId = "CESDKxxxx")]</c> declared by shipping
///     source (<c>libs/</c>, <c>src/</c>) is a public diagnostic without an analyzer descriptor: the compiler reports it.
///     Like an analyzer rule it needs a help link and a page (shared contracts section 3.2): the attribute uses the
///     repository's <c>UrlFormat</c>, <c>analyzers/docs/&lt;id&gt;.md</c> exists and starts with the id, and the
///     diagnostics index lists it. Experimental gates use the <c>CESDK5xxx</c> range.
/// </summary>
/// <remarks>
///     Shared-contracts section 3.2 requires this catalog to be built "by reflection over the packed assemblies", the
///     same technique <c>CheatEngine.SDK.Engine.Tests.Scanning.ScanExperimentalApiTests</c> already uses, scoped there
///     to one namespace it has a <c>ProjectReference</c> to. This project has none, by design (its own top-of-file
///     comment: it only reads committed files and never builds, packs or restores), so it cannot reflect over a
///     referenced assembly. Instead it loads the shipping assemblies' own build output with
///     <see cref="Assembly.LoadFrom(string)"/> from <c>artifacts/bin/CheatEngine.SDK/&lt;configuration&gt;</c> — the one
///     folder <c>src/CheatEngine.SDK</c> copies every <c>libs/</c> assembly into (its csproj comment "Libraries embedded
///     under lib/net10.0") alongside its own — which the solution build that runs before this test module (shared
///     contracts section 1.8: build, then pack, then test) has already populated. This is still a from-disk load, never
///     a compile-time reference, so the "no ProjectReference" design holds; unlike a source-text scan it cannot be
///     fooled by a documentation example or a comment, and it sees exactly what the compiler bound.
/// </remarks>
public sealed class ApiGateDiagnosticTests
{
	private const string UrlFormat = "https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/analyzers/docs/{0}.md";

	[Fact]
	public void Every_api_gate_attribute_uses_a_CESDK_id_and_the_documentation_url_format()
	{
		List<ApiGate> gates = ApiGates();
		List<string> offenders = [];
		foreach (ApiGate gate in gates)
		{
			if (!HasExpectedShape(gate.Id, gate.Kind))
			{
				string expectedShape = gate.Kind == ApiGateKind.Experimental ? "CESDK5 + 3 digits" : "CESDK + 4 digits";
				offenders.Add($"{gate.Location}: {gate.Kind} id '{gate.Id}' does not match '{expectedShape}'.");
			}

			if (!string.Equals(gate.UrlFormat, UrlFormat, StringComparison.Ordinal))
			{
				offenders.Add($"{gate.Location}: {gate.Kind}('{gate.Id}') has UrlFormat '{gate.UrlFormat}', expected '{UrlFormat}'.");
			}
		}

		Assert.Contains(gates, static gate => string.Equals(gate.Id, "CESDK5010", StringComparison.Ordinal));
		Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
	}

	[Fact]
	public void Every_api_gate_id_has_a_documentation_page_and_an_index_row()
	{
		string index = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "analyzers", "docs", "README.md"));
		List<string> offenders = [];
		foreach (string id in ApiGates().Select(static gate => gate.Id).Distinct(StringComparer.Ordinal))
		{
			string page = Path.Combine(RepositoryRoot.Path, "analyzers", "docs", id + ".md");
			if (!File.Exists(page))
			{
				offenders.Add($"analyzers/docs/{id}.md is missing.");
			}
			else if (!File.ReadAllText(page).StartsWith("# " + id + ":", StringComparison.Ordinal))
			{
				offenders.Add($"analyzers/docs/{id}.md does not start with '# {id}:'.");
			}

			if (!index.Contains($"| [{id}]({id}.md) |", StringComparison.Ordinal))
			{
				offenders.Add($"analyzers/docs/README.md has no table row for {id}.");
			}
		}

		Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
	}

	/// <summary>Experimental ids are exactly <c>CESDK5</c> + 3 digits; Obsolete ids are exactly <c>CESDK</c> + 4 digits.</summary>
	private static bool HasExpectedShape(string id, ApiGateKind kind)
	{
		string prefix = kind == ApiGateKind.Experimental ? "CESDK5" : "CESDK";
		int digitCount = kind == ApiGateKind.Experimental ? 3 : 4;
		if (!id.StartsWith(prefix, StringComparison.Ordinal) || id.Length != prefix.Length + digitCount)
		{
			return false;
		}

		for (int index = prefix.Length; index < id.Length; index++)
		{
			if (id[index] is < '0' or > '9')
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>
	///     The shipping assemblies (<c>libs/</c> + <c>src/CheatEngine.SDK</c> itself), loaded once per test process from
	///     the current configuration's build output. The configuration is read from this test module's own output
	///     directory name (<c>artifacts/bin/CheatEngine.SDK.Repository.Tests/&lt;configuration&gt;</c>), never
	///     hard-coded, so the same test scans whichever leg (Debug or Release) built it.
	/// </summary>
	private static readonly Lazy<List<Assembly>> s_shippingAssemblies = new(LoadShippingAssemblies);

	private static List<ApiGate> ApiGates()
	{
		List<ApiGate> gates = [];
		foreach (Assembly assembly in s_shippingAssemblies.Value)
		{
			string assemblyName = assembly.GetName().Name ?? assembly.FullName ?? "<unknown assembly>";
			foreach (Type type in assembly.GetExportedTypes())
			{
				AddGates(gates, assemblyName, type.FullName ?? type.Name, type);
				const BindingFlags memberFlags =
					BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
				foreach (MemberInfo member in type.GetMembers(memberFlags))
				{
					AddGates(gates, assemblyName, (type.FullName ?? type.Name) + "." + member.Name, member);
				}
			}
		}

		return gates;
	}

	private static void AddGates(List<ApiGate> gates, string assemblyName, string location, MemberInfo member)
	{
		if (member.GetCustomAttribute<ExperimentalAttribute>() is { } experimental)
		{
			gates.Add(new ApiGate($"{assemblyName}: {location}", ApiGateKind.Experimental, experimental.DiagnosticId,
				experimental.UrlFormat ?? string.Empty));
		}

		if (member.GetCustomAttribute<ObsoleteAttribute>() is { DiagnosticId.Length: > 0 } obsolete)
		{
			gates.Add(new ApiGate($"{assemblyName}: {location}", ApiGateKind.Obsolete, obsolete.DiagnosticId!,
				obsolete.UrlFormat ?? string.Empty));
		}
	}

	private static List<Assembly> LoadShippingAssemblies()
	{
		string configuration = new DirectoryInfo(AppContext.BaseDirectory).Name;
		string directory = Path.Combine(RepositoryRoot.Path, "artifacts", "bin", "CheatEngine.SDK", configuration);
		if (!Directory.Exists(directory))
		{
			throw new InvalidOperationException(
				$"'{RepositoryRoot.ToRelative(directory)}' does not exist. Build 'CheatEngine.SDK.slnx' in the " +
				$"'{configuration}' configuration before running this test: shared-contracts section 3.2 reads the " +
				"built shipping assemblies, never source text.");
		}

		List<Assembly> assemblies = [];
		foreach (string dll in Directory.EnumerateFiles(directory, "CheatEngine.SDK*.dll", SearchOption.TopDirectoryOnly))
		{
			assemblies.Add(Assembly.LoadFrom(dll));
		}

		return assemblies;
	}

	private enum ApiGateKind
	{
		Unknown = 0,
		Experimental,
		Obsolete
	}

	private sealed record ApiGate(string Location, ApiGateKind Kind, string Id, string UrlFormat);
}
