using CheatEngine.SDK.Repository.Tests.Infrastructure;

using YamlDotNet.RepresentationModel;

namespace CheatEngine.SDK.Repository.Tests.Workflows;

/// <summary>
///     One GitHub Actions YAML file (a workflow or a composite action) with its raw text, for rules that need comments,
///     and its YAML tree. Keys are read as plain strings (<c>on</c> is never a boolean), and a missing key is null.
/// </summary>
internal sealed class WorkflowFile
{
	private const string WorkflowDirectory = ".github/workflows";
	private const string ActionDirectory = ".github/actions";

	private WorkflowFile(string relativePath, string text, YamlMappingNode root)
	{
		RelativePath = relativePath;
		Text = text;
		Root = root;
	}

	/// <summary>The repository-relative path with forward slashes.</summary>
	public string RelativePath
	{
		get;
	}

	/// <summary>The file name, for example <c>ci.yml</c>.</summary>
	public string FileName => Path.GetFileName(RelativePath);

	/// <summary>The raw text with LF line endings.</summary>
	public string Text
	{
		get;
	}

	/// <summary>The document's root mapping.</summary>
	public YamlMappingNode Root
	{
		get;
	}

	/// <summary>Every workflow under <c>.github/workflows</c> that exists now, so new workflows are checked on arrival.</summary>
	public static IReadOnlyList<WorkflowFile> LoadWorkflows()
	{
		return LoadDirectory(WorkflowDirectory, "*.y*ml");
	}

	/// <summary>Every composite action under <c>.github/actions</c>.</summary>
	public static IReadOnlyList<WorkflowFile> LoadActions()
	{
		return LoadDirectory(ActionDirectory, "action.y*ml");
	}

	/// <summary>A workflow by file name, or null when it does not exist.</summary>
	public static WorkflowFile? TryLoadWorkflow(string fileName)
	{
		string path = Path.Combine(RepositoryRoot.Path, WorkflowDirectory, fileName);
		return File.Exists(path) ? Load(path) : null;
	}

	/// <summary>A workflow that must exist.</summary>
	public static WorkflowFile LoadWorkflow(string fileName)
	{
		WorkflowFile? workflow = TryLoadWorkflow(fileName);
		Assert.True(workflow is not null, $"{WorkflowDirectory}/{fileName} does not exist.");
		return workflow;
	}

	/// <summary>The jobs of a workflow, in file order.</summary>
	public IReadOnlyList<WorkflowJob> Jobs()
	{
		List<WorkflowJob> jobs = [];
		YamlMappingNode? node = Mapping(Root, "jobs");
		if (node is null)
		{
			return jobs;
		}

		foreach (KeyValuePair<YamlNode, YamlNode> entry in node.Children)
		{
			jobs.Add(new WorkflowJob(this, ((YamlScalarNode) entry.Key).Value!, (YamlMappingNode) entry.Value));
		}

		return jobs;
	}

	/// <summary>A job that must exist.</summary>
	public WorkflowJob Job(string id)
	{
		foreach (WorkflowJob job in Jobs())
		{
			if (string.Equals(job.Id, id, StringComparison.Ordinal))
			{
				return job;
			}
		}

		Assert.Fail($"{RelativePath} has no job '{id}'.");
		return null!;
	}

	/// <summary>The event names of the <c>on</c> key, whether it is a scalar, a sequence or a mapping.</summary>
	public IReadOnlyList<string> Triggers()
	{
		if (!Root.Children.TryGetValue(new YamlScalarNode("on"), out YamlNode? on))
		{
			return [];
		}

		return on switch
		{
			YamlScalarNode scalar => [scalar.Value!],
			YamlSequenceNode sequence => ScalarValues(sequence),
			YamlMappingNode mapping => Keys(mapping),
			_ => []
		};
	}

	/// <summary>The configuration of one trigger, or null when it has none.</summary>
	public YamlMappingNode? Trigger(string eventName)
	{
		return Mapping(Root, "on") is { } on ? Mapping(on, eventName) : null;
	}

	/// <summary>The steps of a composite action.</summary>
	public IReadOnlyList<YamlMappingNode> ActionSteps()
	{
		YamlMappingNode? runs = Mapping(Root, "runs");
		return runs is null ? [] : Mappings(Sequence(runs, "steps"));
	}

	/// <summary>A child scalar's value, or null.</summary>
	public static string? Scalar(YamlMappingNode node, string key)
	{
		return node.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? value) && value is YamlScalarNode scalar
			? scalar.Value
			: null;
	}

	/// <summary>A child mapping, or null.</summary>
	public static YamlMappingNode? Mapping(YamlMappingNode node, string key)
	{
		return node.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? value)
			? value as YamlMappingNode
			: null;
	}

	/// <summary>A child sequence, or null.</summary>
	public static YamlSequenceNode? Sequence(YamlMappingNode node, string key)
	{
		return node.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? value)
			? value as YamlSequenceNode
			: null;
	}

	/// <summary>Whether the mapping has the key, whatever its value.</summary>
	public static bool Has(YamlMappingNode node, string key)
	{
		return node.Children.ContainsKey(new YamlScalarNode(key));
	}

	/// <summary>The keys of a mapping, in order.</summary>
	public static List<string> Keys(YamlMappingNode node)
	{
		List<string> keys = [];
		foreach (YamlNode key in node.Children.Keys)
		{
			keys.Add(((YamlScalarNode) key).Value!);
		}

		return keys;
	}

	/// <summary>The mapping items of a sequence (a null sequence is empty).</summary>
	public static List<YamlMappingNode> Mappings(YamlSequenceNode? sequence)
	{
		List<YamlMappingNode> items = [];
		if (sequence is null)
		{
			return items;
		}

		foreach (YamlNode item in sequence.Children)
		{
			if (item is YamlMappingNode mapping)
			{
				items.Add(mapping);
			}
		}

		return items;
	}

	/// <summary>The scalar items of a sequence.</summary>
	public static List<string> ScalarValues(YamlSequenceNode sequence)
	{
		List<string> values = [];
		foreach (YamlNode item in sequence.Children)
		{
			if (item is YamlScalarNode scalar)
			{
				values.Add(scalar.Value!);
			}
		}

		return values;
	}

	/// <summary>Collapses every run of whitespace to one space and trims, for comparing folded expressions.</summary>
	public static string NormalizeWhitespace(string value)
	{
		return string.Join(' ', value.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries));
	}

	private static List<WorkflowFile> LoadDirectory(string relativeDirectory, string pattern)
	{
		List<WorkflowFile> files = [];
		string directory = Path.Combine(RepositoryRoot.Path, relativeDirectory);
		if (!Directory.Exists(directory))
		{
			return files;
		}

		foreach (string path in Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories))
		{
			files.Add(Load(path));
		}

		files.Sort(static (left, right) => string.CompareOrdinal(left.RelativePath, right.RelativePath));
		return files;
	}

	private static WorkflowFile Load(string path)
	{
		string text = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
		YamlStream stream = [];
		stream.Load(new StringReader(text));
		Assert.Single(stream.Documents);
		YamlMappingNode root = Assert.IsType<YamlMappingNode>(stream.Documents[0].RootNode);
		return new WorkflowFile(RepositoryRoot.ToRelative(path), text, root);
	}
}
