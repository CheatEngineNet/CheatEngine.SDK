using CheatEngine.SDK.Repository.Tests.Qualification.Validation;

namespace CheatEngine.SDK.Repository.Tests.Qualification;

/// <summary>
///     The two qualification harnesses are compiled by CI through the solution, never run or packed by it: the CE 7.7
///     live probe is an x64 dynamic-loading plugin, and the qualification target is a Native AOT console program for x64
///     and x86 that references no SDK project. Static checks of the solution and project files; nothing is built here.
/// </summary>
public sealed class QualificationProjectShapeTests
{
	private const string LiveProbe = "tests/CheatEngine.SDK.LiveProbe/CheatEngine.SDK.LiveProbe.csproj";
	private const string Target = "tests/CheatEngine.SDK.QualificationTarget/CheatEngine.SDK.QualificationTarget.csproj";

	[Fact]
	public void LiveProbe_is_in_the_solution_as_an_x64_dynamic_loading_plugin_that_never_packs()
	{
		XElement project = Project(LiveProbe);

		Assert.Contains(LiveProbe, QualificationDocuments.SolutionProjects());
		Assert.Equal("x64", QualificationDocuments.SolutionPlatform(LiveProbe));
		Assert.Equal("x64", Property(project, "Platforms"));
		Assert.Equal("x64", Property(project, "PlatformTarget"));
		Assert.Equal("true", Property(project, "EnableDynamicLoading"));
		Assert.Equal("false", Property(project, "IsPackable"));
		Assert.False(Path.GetFileNameWithoutExtension(LiveProbe).EndsWith(".Tests", StringComparison.Ordinal));
	}

	[Fact]
	public void QualificationTarget_is_in_the_solution_and_publishes_native_aot_for_x64_and_x86()
	{
		XElement project = Project(Target);

		Assert.Contains(Target, QualificationDocuments.SolutionProjects());
		Assert.Equal("Exe", Property(project, "OutputType"));
		Assert.Equal("true", Property(project, "PublishAot"));
		Assert.Equal(["win-x64", "win-x86"],
			(Property(project, "RuntimeIdentifiers") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries));
		Assert.Null(Property(project, "RuntimeIdentifier"));
		Assert.True(QualificationDocuments.Exists("tests/CheatEngine.SDK.QualificationTarget/README.md"));
	}

	[Fact]
	public void Qualification_harnesses_are_not_test_modules_and_never_pack()
	{
		foreach (string path in (string[]) [LiveProbe, Target])
		{
			XElement project = Project(path);

			Assert.False(Path.GetFileNameWithoutExtension(path).EndsWith(".Tests", StringComparison.Ordinal),
				$"{path}: eng/Tests.props turns every *.Tests project into a test module.");
			Assert.Equal("false", Property(project, "IsPackable"));
			Assert.Null(Property(project, "IsTestProject"));
			Assert.DoesNotContain(project.Descendants("PackageReference"),
				static reference => ((string?) reference.Attribute("Include") ?? string.Empty).StartsWith("xunit",
					StringComparison.OrdinalIgnoreCase));
		}
	}

	[Fact]
	public void QualificationTarget_references_no_SDK_project()
	{
		XElement project = Project(Target);

		Assert.Empty(project.Descendants("ProjectReference"));
		Assert.Empty(project.Descendants("PackageReference"));
		foreach (string source in Directory.EnumerateFiles(
					 QualificationDocuments.Absolute("tests/CheatEngine.SDK.QualificationTarget"), "*.cs"))
		{
			Assert.DoesNotContain("CheatEngine.SDK.", File.ReadAllText(source), StringComparison.Ordinal);
		}
	}

	private static XElement Project(string path)
	{
		return XDocument.Load(QualificationDocuments.Absolute(path)).Root
			   ?? throw new InvalidOperationException(path + " has no root element.");
	}

	private static string? Property(XElement project, string name)
	{
		string? value = null;
		foreach (XElement group in project.Elements("PropertyGroup"))
		{
			if (group.Attribute("Condition") is null && group.Element(name) is { } element)
			{
				value = element.Value.Trim();
			}
		}

		return value;
	}
}
