using System.Text.RegularExpressions;

using CheatEngine.SDK.Repository.Tests.Infrastructure;

namespace CheatEngine.SDK.Repository.Tests.Generation;

/// <summary>
///     Repository guards for the Roslyn components (audit A19-18, A19-20, A00-21, ADR-05): the source generator
///     inventory is the reviewed set, every analyzer and generator keeps the extended analyzer rules (RS1035: no file,
///     environment, console or culture access inside the compiler), and no generator embeds a path to a local Cheat
///     Engine installation or reads its <c>celua.txt</c>.
/// </summary>
public sealed partial class GeneratorInventoryTests
{
	// ADR-05: adding a generator (or a shared component) is a review decision, recorded here.
	private static readonly string[] s_reviewedSourceGeneratorProjects =
	[
		"source-generators/CheatEngine.SDK.SourceGenerators.EngineApi/CheatEngine.SDK.SourceGenerators.EngineApi.csproj",
		"source-generators/CheatEngine.SDK.SourceGenerators.EntryPoint/CheatEngine.SDK.SourceGenerators.EntryPoint.csproj",
		"source-generators/CheatEngine.SDK.SourceGenerators.LuaBindings/CheatEngine.SDK.SourceGenerators.LuaBindings.csproj",
		"source-generators/CheatEngine.SDK.SourceGenerators.LuaBridgeContract/CheatEngine.SDK.SourceGenerators.LuaBridgeContract.csproj",
		"source-generators/CheatEngine.SDK.SourceGenerators.Shared/CheatEngine.SDK.SourceGenerators.Shared.csproj"
	];

	// The shared project holds emitters and models only: it declares no [Generator] of its own.
	private static readonly string[] s_generatorDeclaringProjects =
	[
		"source-generators/CheatEngine.SDK.SourceGenerators.EngineApi",
		"source-generators/CheatEngine.SDK.SourceGenerators.EntryPoint",
		"source-generators/CheatEngine.SDK.SourceGenerators.LuaBindings",
		"source-generators/CheatEngine.SDK.SourceGenerators.LuaBridgeContract"
	];

	[Fact]
	public void Source_generator_projects_are_exactly_the_reviewed_set()
	{
		string[] projects =
		[
			.. RepositoryRoot.EnumerateSourceFiles("*.csproj")
				.Where(static path => path.StartsWith("source-generators/", StringComparison.Ordinal))
				.Order(StringComparer.Ordinal)
		];

		Assert.Equal(s_reviewedSourceGeneratorProjects, projects);

		string[] declaring =
		[
			.. ComponentSources()
				.Where(static path => GeneratorAttribute().IsMatch(Read(path)))
				.Select(static path => string.Join('/', path.Split('/').Take(2)))
				.Distinct(StringComparer.Ordinal)
				.Order(StringComparer.Ordinal)
		];
		Assert.Equal(s_generatorDeclaringProjects, declaring);
	}

	[Fact]
	public void Every_roslyn_component_keeps_the_extended_analyzer_rules()
	{
		string props = Read("eng/RoslynComponent.props");
		Assert.Contains("<EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>", props,
			StringComparison.Ordinal);
		Assert.Contains("<IsRoslynComponent>true</IsRoslynComponent>", props, StringComparison.Ordinal);

		// The profile is imported by folder: every csproj under analyzers/ and source-generators/ gets it.
		string buildProps = Read("Directory.Build.props");
		Assert.Matches(
			new Regex(
				@"<Import Project=""\$\(RepoRoot\)eng/RoslynComponent\.props""\s+Condition=""\$\(_CheatEngineSdkFolder\.StartsWith\('analyzers/'\)\) or \$\(_CheatEngineSdkFolder\.StartsWith\('source-generators/'\)\)""",
				RegexOptions.None, TimeSpan.FromSeconds(1)), buildProps);

		string[] components = [.. RoslynComponentProjects()];
		Assert.True(components.Length >= 7, "The Roslyn component scan found only " + components.Length + " projects.");
		foreach (string project in components)
		{
			string text = Read(project);
			Assert.DoesNotContain("EnforceExtendedAnalyzerRules", text, StringComparison.Ordinal);
			Assert.DoesNotContain("IsRoslynComponent", text, StringComparison.Ordinal);
			Assert.DoesNotContain("RS1035", text, StringComparison.Ordinal);
			Assert.DoesNotContain("RoslynComponent.props", text, StringComparison.Ordinal);
		}
	}

	[Fact]
	public void Generators_never_embed_a_local_cheat_engine_path()
	{
		List<string> offences = [];
		foreach (string path in ComponentSources())
		{
			string text = Read(path);
			if (text.Contains("Program Files", StringComparison.OrdinalIgnoreCase) ||
			    text.Contains("celua.txt", StringComparison.OrdinalIgnoreCase) ||
			    DriveRootedPath().IsMatch(text))
			{
				offences.Add(path);
			}
		}

		Assert.True(offences.Count == 0,
			"Generators and analyzers must take Cheat Engine facts from committed specs, never from a local installation: " +
			string.Join(", ", offences));
	}

	private static IEnumerable<string> RoslynComponentProjects()
	{
		return RepositoryRoot.EnumerateSourceFiles("*.csproj").Where(static path =>
			path.StartsWith("analyzers/", StringComparison.Ordinal) ||
			path.StartsWith("source-generators/", StringComparison.Ordinal));
	}

	private static IEnumerable<string> ComponentSources()
	{
		return RepositoryRoot.EnumerateSourceFiles("*.cs").Where(static path =>
			path.StartsWith("analyzers/", StringComparison.Ordinal) ||
			path.StartsWith("source-generators/", StringComparison.Ordinal));
	}

	private static string Read(string relativePath)
	{
		return File.ReadAllText(Path.Combine(RepositoryRoot.Path, relativePath));
	}

	[GeneratedRegex(@"^\s*\[Generator[(\]]", RegexOptions.Multiline, 1000)]
	private static partial Regex GeneratorAttribute();

	// A drive-rooted Windows path in code or text ("C:\..." or "D:/..."), not a URI scheme such as "https://".
	[GeneratedRegex(@"(?<![A-Za-z0-9])[A-Za-z]:[\\/](?![\\/])", RegexOptions.None, 1000)]
	private static partial Regex DriveRootedPath();
}
