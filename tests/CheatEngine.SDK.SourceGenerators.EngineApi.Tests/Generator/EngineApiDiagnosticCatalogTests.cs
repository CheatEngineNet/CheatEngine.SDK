using System.Globalization;
using System.Reflection;

using Microsoft.CodeAnalysis;

namespace CheatEngine.SDK.SourceGenerators.EngineApi.Tests.Generator;

/// <summary>
///     The EngineApi generator's own diagnostic catalog (binding DoD F): every descriptor has the identifier range,
///     category and help link of the SDK rules, a documentation page, a release-tracking row and a row in the rule index.
///     The Analyzers catalog test covers only the analyzer assembly, so the generator's CESDK3xxx rules are checked here.
/// </summary>
public sealed class EngineApiDiagnosticCatalogTests
{
	private const string HelpLinkBase = "https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/analyzers/docs/";

	[Fact]
	public void Every_engine_api_descriptor_has_a_page_a_help_link_and_a_release_row()
	{
		DiagnosticDescriptor[] descriptors = Descriptors();
		string root = RepositoryRoot();
		string releases = File.ReadAllText(Path.Combine(root,
			"source-generators/CheatEngine.SDK.SourceGenerators.EngineApi/AnalyzerReleases.Unshipped.md"));
		string index = File.ReadAllText(Path.Combine(root, "analyzers/docs/README.md"));

		Assert.Equal(["CESDK3001", "CESDK3002", "CESDK3003", "CESDK3004", "CESDK3005"],
			descriptors.Select(static d => d.Id).Order(StringComparer.Ordinal), StringComparer.Ordinal);
		foreach (DiagnosticDescriptor descriptor in descriptors)
		{
			string title = descriptor.Title.ToString(CultureInfo.InvariantCulture);
			Assert.Equal("CheatEngine.SDK.EngineApi", descriptor.Category);
			Assert.Equal(DiagnosticSeverity.Error, descriptor.DefaultSeverity);
			Assert.Equal(HelpLinkBase + descriptor.Id + ".md", descriptor.HelpLinkUri);
			Assert.DoesNotMatch(@"\.$", title);
			Assert.EndsWith(".", descriptor.Description.ToString(CultureInfo.InvariantCulture),
				StringComparison.Ordinal);

			string page = Path.Combine(root, "analyzers", "docs", descriptor.Id + ".md");
			Assert.True(File.Exists(page), "Missing documentation page " + page + ".");
			Assert.StartsWith("# " + descriptor.Id + ": " + title, File.ReadAllText(page), StringComparison.Ordinal);
			Assert.Contains(" " + descriptor.Id + " | CheatEngine.SDK.EngineApi | Error    |", releases,
				StringComparison.Ordinal);
			Assert.Contains("| [" + descriptor.Id + "](" + descriptor.Id + ".md) | " + title, index,
				StringComparison.Ordinal);
		}
	}

	private static DiagnosticDescriptor[] Descriptors()
	{
		return
		[
			.. typeof(EngineApiGenerator).Assembly.GetType(
					"CheatEngine.SDK.SourceGenerators.EngineApi.EngineApiDiagnostics",
					true)!
				.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
				.Where(static field => field.FieldType == typeof(DiagnosticDescriptor))
				.Select(static field => (DiagnosticDescriptor) field.GetValue(null)!)
		];
	}

	// The directory that holds CheatEngine.SDK.slnx, found from the test binaries upwards.
	private static string RepositoryRoot()
	{
		for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
			 directory is not null;
			 directory = directory.Parent)
		{
			if (File.Exists(Path.Combine(directory.FullName, "CheatEngine.SDK.slnx")))
			{
				return directory.FullName;
			}
		}

		throw new InvalidOperationException(
			"CheatEngine.SDK.slnx was not found above " + AppContext.BaseDirectory + ".");
	}
}
