using System.Text.Json;

using CheatEngine.SDK.Repository.Tests.Infrastructure;

using YamlDotNet.RepresentationModel;

namespace CheatEngine.SDK.Repository.Tests.Workflows;

/// <summary>
///     The coverage ratchet of the Debug leg: eng/coverage-baseline.json holds a line-coverage floor for exactly the
///     assemblies the CheatEngine.SDK package ships, eng/ci/Test-CoverageBaseline.ps1 enforces it on the merged report of
///     every test module, and the merge tool is pinned in the local tool manifest.
/// </summary>
public sealed class CoverageBaselineTests
{
	private const string BaselinePath = "eng/coverage-baseline.json";
	private const string ScriptPath = "eng/ci/Test-CoverageBaseline.ps1";
	private const string ToolManifestPath = ".config/dotnet-tools.json";
	private const string PackageProjectPath = "src/CheatEngine.SDK/CheatEngine.SDK.csproj";
	private const string BaselineSchema = "cheatengine-coverage-baseline/v0";

	[Fact]
	public void Coverage_baseline_lists_exactly_the_shipping_assemblies()
	{
		// Shipping = every project the package embeds under lib/ or packs under analyzers/, read from the package project.
		SortedSet<string> shipping = new(StringComparer.Ordinal);
		foreach (XElement reference in XDocument.Load(RepositoryFile(PackageProjectPath)).Descendants("ProjectReference"))
		{
			string include = ((string?) reference.Attribute("Include") ?? "").Replace('\\', '/');
			shipping.Add(Path.GetFileNameWithoutExtension(include));
		}

		Assert.NotEmpty(shipping);
		using JsonDocument baseline = ReadBaseline();
		SortedSet<string> listed = new(StringComparer.Ordinal);
		foreach (JsonProperty assembly in baseline.RootElement.GetProperty("assemblies").EnumerateObject())
		{
			listed.Add(assembly.Name);
		}

		Assert.Equal(shipping, listed);
	}

	[Fact]
	public void Coverage_floors_are_percentages_and_the_tolerance_is_explicit()
	{
		using JsonDocument baseline = ReadBaseline();
		JsonElement root = baseline.RootElement;

		List<string> keys = [];
		foreach (JsonProperty property in root.EnumerateObject())
		{
			keys.Add(property.Name);
		}

		Assert.Equal(["schema", "tolerance", "assemblies"], keys);
		Assert.Equal(BaselineSchema, root.GetProperty("schema").GetString());
		double tolerance = root.GetProperty("tolerance").GetDouble();
		Assert.True(tolerance is > 0 and <= 5, $"The tolerance must be a small positive number of percentage points, found {tolerance}.");

		foreach (JsonProperty assembly in root.GetProperty("assemblies").EnumerateObject())
		{
			// Line coverage only: the merged report keeps no block or branch data (see Test-CoverageBaseline.ps1).
			List<string> metrics = [];
			foreach (JsonProperty metric in assembly.Value.EnumerateObject())
			{
				metrics.Add(metric.Name);
			}

			Assert.Equal(["line"], metrics);
			double floor = assembly.Value.GetProperty("line").GetDouble();
			Assert.True(floor is >= 0 and <= 100, $"{assembly.Name} has the floor {floor}, which is not a percentage.");
			// Floors are floored to 0.1 when copied from the CI suggestion, so they never claim more than was measured.
			Assert.Equal(Math.Round(floor, 1), floor);
		}
	}

	[Fact]
	public void Coverage_tool_is_pinned_in_the_local_tool_manifest()
	{
		using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(RepositoryFile(ToolManifestPath)));
		JsonElement root = manifest.RootElement;
		Assert.True(root.GetProperty("isRoot").GetBoolean(), $"{ToolManifestPath} must be a root manifest.");

		JsonProperty tool = Assert.Single(root.GetProperty("tools").EnumerateObject());
		Assert.Equal("dotnet-coverage", tool.Name);
		// dotnet-coverage targets .NET 8; runners that expose only the pinned .NET 10 runtime need the Major roll-forward.
		// https://learn.microsoft.com/dotnet/core/tools/dotnet-tool-run
		Assert.True(tool.Value.GetProperty("rollForward").GetBoolean());
		string version = tool.Value.GetProperty("version").GetString() ?? "";
		Assert.Matches(@"^\d+\.\d+\.\d+$", version);

		// The merge tool and the collector the tests use ship together: keep them on the same version.
		XElement collector = Assert.Single(XDocument.Load(RepositoryFile("Directory.Packages.props")).Descendants("PackageVersion"),
			static element => string.Equals((string?) element.Attribute("Include"), "Microsoft.Testing.Extensions.CodeCoverage",
				StringComparison.Ordinal));
		Assert.Equal((string?) collector.Attribute("Version"), version);
	}

	[Fact]
	public void Debug_leg_checks_the_coverage_floors_and_never_writes_the_baseline()
	{
		WorkflowJob job = WorkflowFile.LoadWorkflow(WorkflowContract.Pipeline).Job("build-test");
		YamlMappingNode check = job.Step("Check coverage floors");
		Assert.Equal("matrix.configuration == 'Debug'", WorkflowFile.Scalar(check, "if"));
		Assert.Contains($"./{ScriptPath} -ResultsDirectory $env:RESULTS -Baseline {BaselinePath}",
			WorkflowFile.Scalar(check, "run"), StringComparison.Ordinal);
		Assert.True(job.StepIndex("Check test module inventory") < job.StepIndex("Check coverage floors"));
		YamlMappingNode upload = Assert.Single(job.StepsUsing("actions/upload-artifact@"),
			static step => string.Equals(WorkflowJob.With(step, "name"), "coverage-report", StringComparison.Ordinal));
		Assert.Equal("artifacts/coverage-report/", WorkflowJob.With(upload, "path"));

		// The script merges with the pinned tool and only suggests a new baseline; raising a floor is a reviewed commit.
		string script = File.ReadAllText(RepositoryFile(ScriptPath));
		Assert.Contains("dotnet tool run dotnet-coverage merge", script, StringComparison.Ordinal);
		Assert.Contains("suggested-coverage-baseline.json", script, StringComparison.Ordinal);
		foreach (string line in script.Split('\n'))
		{
			bool writes = line.Contains("Set-Content", StringComparison.Ordinal) || line.Contains("WriteAll", StringComparison.Ordinal) ||
				line.Contains("Out-File", StringComparison.Ordinal);
			Assert.False(writes && line.Contains("$baselinePath", StringComparison.Ordinal),
				$"{ScriptPath} must never write the baseline: '{line.Trim()}'.");
		}
	}

	private static JsonDocument ReadBaseline()
	{
		return JsonDocument.Parse(File.ReadAllText(RepositoryFile(BaselinePath)));
	}

	private static string RepositoryFile(string relativePath)
	{
		string path = Path.Combine(RepositoryRoot.Path, relativePath);
		Assert.True(File.Exists(path), $"{relativePath} is missing.");
		return path;
	}
}
