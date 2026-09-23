using System.Text.Json;
using System.Text.RegularExpressions;

using CheatEngine.SDK.Repository.Tests.Infrastructure;

using YamlDotNet.RepresentationModel;

namespace CheatEngine.SDK.Repository.Tests.Workflows;

/// <summary>
///     build-info.json v0 (shared contract 1.10): the schema requires exactly the contract fields and rejects anything
///     else, eng/ci/New-BuildInfo.ps1 writes every one of them, and the Release leg writes and uploads the document.
/// </summary>
public sealed partial class BuildInfoSchemaTests
{
	private const string SchemaPath = "eng/ci/build-info.v0.schema.json";
	private const string WriterPath = "eng/ci/New-BuildInfo.ps1";

	/// <summary>The required property lists of contract 1.10, by JSON pointer of the object schema that declares them.</summary>
	private static readonly Dictionary<string, string[]> s_requiredByObject = new(StringComparer.Ordinal)
	{
		[""] =
		[
			"schema", "repository", "commit", "treeHash", "ref", "event", "runId", "runAttempt", "runUrl", "pullRequest",
			"dotnetSdk", "globalJsonSha256", "runner", "toolchain", "nativeBridge", "packages", "createdUtc"
		],
		["/properties/pullRequest/oneOf/1"] = ["number", "headSha"],
		["/properties/runner"] = ["label", "imageOs", "imageVersion"],
		["/properties/toolchain"] = ["xmake", "msvcToolset", "msvcVersion", "windowsSdk"],
		["/properties/nativeBridge"] = ["sha256", "checkedInSha256", "sourceFingerprint", "driftFromCheckedIn"],
		["/properties/packages/items"] = ["id", "version", "file", "sha256", "sha512", "sbomEntry"]
	};

	[Fact]
	public void Build_info_schema_requires_exactly_the_contract_fields()
	{
		using JsonDocument schema = ReadSchema();
		JsonElement root = schema.RootElement;
		Assert.Equal("https://json-schema.org/draft/2020-12/schema", root.GetProperty("$schema").GetString());
		Assert.Equal($"https://github.com/CheatEngineNet/CheatEngine.SDK/blob/main/{SchemaPath}", root.GetProperty("$id").GetString());
		Assert.Equal("cheatengine-build-info/v0", root.GetProperty("properties").GetProperty("schema").GetProperty("const").GetString());

		foreach ((string pointer, string[] required) in s_requiredByObject)
		{
			JsonElement node = Resolve(root, pointer);
			Assert.Equal(required, Strings(node.GetProperty("required")), StringComparer.Ordinal);
			// Every declared property is required: v0 has no optional field, so a missing value is always an error.
			Assert.Equal(required, PropertyNames(node.GetProperty("properties")), StringComparer.Ordinal);
		}

		Assert.Equal(["pull_request", "push", "workflow_dispatch"],
			Strings(root.GetProperty("properties").GetProperty("event").GetProperty("enum")));
		Assert.Equal(["windows-2025", "ubuntu-24.04"],
			Strings(Resolve(root, "/properties/runner/properties/label").GetProperty("enum")));
	}

	[Fact]
	public void Build_info_schema_rejects_additional_properties()
	{
		using JsonDocument schema = ReadSchema();
		int objects = 0;
		foreach (JsonElement node in ObjectSchemas(schema.RootElement))
		{
			objects++;
			Assert.True(node.TryGetProperty("additionalProperties", out JsonElement additional) &&
				additional.ValueKind == JsonValueKind.False,
				$"Every object of {SchemaPath} must declare additionalProperties: false.");
		}

		Assert.Equal(s_requiredByObject.Count, objects);
	}

	[Fact]
	public void Build_info_writer_emits_every_required_field()
	{
		string writer = File.ReadAllText(RepositoryFile(WriterPath)).Replace("\r\n", "\n", StringComparison.Ordinal);
		foreach (string[] required in s_requiredByObject.Values)
		{
			foreach (string property in required)
			{
				Assert.True(Regex.IsMatch(writer, $@"(?m)^\s*{Regex.Escape(property)} = ", RegexOptions.None, TimeSpan.FromSeconds(1)),
					$"{WriterPath} does not write '{property}'.");
			}
		}

		// Encoding rules of shared contract 2.0.
		Assert.Contains("[Text.UTF8Encoding]::new($false)", writer, StringComparison.Ordinal);
		Assert.Contains(".ToLowerInvariant()", writer, StringComparison.Ordinal);
		Assert.Contains("'build-info.json would contain an absolute local path.'", writer, StringComparison.Ordinal);
	}

	[Fact]
	public void Release_leg_writes_and_uploads_build_info_from_the_native_job_outputs()
	{
		WorkflowFile pipeline = WorkflowFile.LoadWorkflow(WorkflowContract.Pipeline);
		WorkflowJob job = pipeline.Job("build-test");
		YamlMappingNode step = job.Step("Write build info");
		Assert.Equal("matrix.configuration == 'Release'", WorkflowFile.Scalar(step, "if"));
		Assert.True(job.StepIndex("Pack") < job.StepIndex("Write build info"));
		Assert.Contains($"./{WriterPath} -PackagePath $env:PACKAGE", WorkflowFile.Scalar(step, "run"), StringComparison.Ordinal);

		// The step passes exactly the variables the writer reads, and every native-job output it names exists.
		string writer = File.ReadAllText(RepositoryFile(WriterPath));
		SortedSet<string> read = new(StringComparer.Ordinal);
		foreach (Match match in BuildInfoVariable().Matches(writer))
		{
			read.Add(match.Groups["name"].Value);
		}

		SortedSet<string> passed = new(StringComparer.Ordinal);
		YamlMappingNode env = Assert.IsType<YamlMappingNode>(WorkflowFile.Mapping(step, "env"));
		YamlMappingNode outputs = Assert.IsType<YamlMappingNode>(WorkflowFile.Mapping(pipeline.Job("native").Node, "outputs"));
		foreach (KeyValuePair<YamlNode, YamlNode> variable in env.Children)
		{
			string name = ((YamlScalarNode) variable.Key).Value ?? "";
			if (!name.StartsWith("BUILD_INFO_", StringComparison.Ordinal))
			{
				continue;
			}

			passed.Add(name);
			Match output = NativeOutputReference().Match(((YamlScalarNode) variable.Value).Value ?? "");
			Assert.True(!output.Success || WorkflowFile.Has(outputs, output.Groups["name"].Value),
				$"{name} reads the native output '{output.Groups["name"].Value}', which the native job does not declare.");
		}

		Assert.Equal(read, passed);
		Assert.Equal("windows-2025", WorkflowJob.Env(step, "BUILD_INFO_RUNNER_LABEL"));
		Assert.Equal(job.RunsOn, WorkflowJob.Env(step, "BUILD_INFO_RUNNER_LABEL"));

		YamlMappingNode upload = Assert.Single(job.StepsUsing("actions/upload-artifact@"),
			static candidate => string.Equals(WorkflowJob.With(candidate, "name"), "build-info", StringComparison.Ordinal));
		Assert.Equal("matrix.configuration == 'Release'", WorkflowFile.Scalar(upload, "if"));
		Assert.Equal("artifacts/build-info/build-info.json", WorkflowJob.With(upload, "path"));
	}

	private static JsonDocument ReadSchema()
	{
		return JsonDocument.Parse(File.ReadAllText(RepositoryFile(SchemaPath)));
	}

	private static string RepositoryFile(string relativePath)
	{
		string path = Path.Combine(RepositoryRoot.Path, relativePath);
		Assert.True(File.Exists(path), $"{relativePath} is missing.");
		return path;
	}

	private static JsonElement Resolve(JsonElement root, string pointer)
	{
		JsonElement node = root;
		foreach (string segment in pointer.Split('/', StringSplitOptions.RemoveEmptyEntries))
		{
			node = node.ValueKind == JsonValueKind.Array
				? node[int.Parse(segment, System.Globalization.CultureInfo.InvariantCulture)]
				: node.GetProperty(segment);
		}

		return node;
	}

	private static List<string> Strings(JsonElement array)
	{
		List<string> values = [];
		foreach (JsonElement item in array.EnumerateArray())
		{
			values.Add(item.GetString() ?? "");
		}

		return values;
	}

	private static List<string> PropertyNames(JsonElement properties)
	{
		List<string> names = [];
		foreach (JsonProperty property in properties.EnumerateObject())
		{
			names.Add(property.Name);
		}

		return names;
	}

	/// <summary>Every schema node that declares <c>"type": "object"</c>, at any depth.</summary>
	private static List<JsonElement> ObjectSchemas(JsonElement node)
	{
		List<JsonElement> found = [];
		if (node.ValueKind == JsonValueKind.Object)
		{
			if (node.TryGetProperty("type", out JsonElement type) && type.ValueKind == JsonValueKind.String &&
				string.Equals(type.GetString(), "object", StringComparison.Ordinal))
			{
				found.Add(node);
			}

			foreach (JsonProperty property in node.EnumerateObject())
			{
				found.AddRange(ObjectSchemas(property.Value));
			}
		}
		else if (node.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement item in node.EnumerateArray())
			{
				found.AddRange(ObjectSchemas(item));
			}
		}

		return found;
	}

	[GeneratedRegex(@"-Name (?<name>BUILD_INFO_[A-Z0-9_]+)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex BuildInfoVariable();

	[GeneratedRegex(@"^\$\{\{ needs\.native\.outputs\.(?<name>[a-z0-9-]+) \}\}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex NativeOutputReference();
}
