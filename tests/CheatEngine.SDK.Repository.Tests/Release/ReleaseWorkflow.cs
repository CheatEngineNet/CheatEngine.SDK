using System.Text.RegularExpressions;

using CheatEngine.SDK.Repository.Tests.Infrastructure;

using YamlDotNet.RepresentationModel;

namespace CheatEngine.SDK.Repository.Tests.Release;

/// <summary>
///     The committed <c>.github/workflows/release.yml</c> as a YAML tree. Keys are read as plain strings (<c>on</c> is
///     never a boolean), <c>needs</c> may be a scalar or a sequence, and expressions are compared after whitespace
///     normalization, so a folded <c>if:</c> reads the same as a one-line one.
/// </summary>
internal sealed partial class ReleaseWorkflow
{
	/// <summary>The repository-relative path of the release workflow.</summary>
	public const string RelativePath = ".github/workflows/release.yml";

	private ReleaseWorkflow(YamlMappingNode root)
	{
		Root = root;
	}

	/// <summary>The document's root mapping.</summary>
	public YamlMappingNode Root
	{
		get;
	}

	/// <summary>Parses the committed file.</summary>
	public static ReleaseWorkflow Load()
	{
		string path = Path.Combine(RepositoryRoot.Path, RelativePath);
		Assert.True(File.Exists(path), $"{RelativePath} does not exist.");
		YamlStream stream = [];
		using (StreamReader reader = new(path))
		{
			stream.Load(reader);
		}

		return new ReleaseWorkflow((YamlMappingNode) stream.Documents[0].RootNode);
	}

	/// <summary>The job ids, in file order.</summary>
	public List<string> JobIds()
	{
		return Keys(Mapping(Root, "jobs") ?? throw new InvalidOperationException($"{RelativePath} has no jobs."));
	}

	/// <summary>A job that must exist.</summary>
	public YamlMappingNode Job(string id)
	{
		YamlMappingNode? job = Mapping(Mapping(Root, "jobs")!, id);
		Assert.True(job is not null, $"{RelativePath} has no job '{id}'.");
		return job;
	}

	/// <summary>The <c>needs</c> of a job, a scalar or a sequence, in file order; empty when absent.</summary>
	public static List<string> Needs(YamlMappingNode job)
	{
		if (!job.Children.TryGetValue(new YamlScalarNode("needs"), out YamlNode? needs))
		{
			return [];
		}

		return needs switch
		{
			YamlScalarNode scalar => [scalar.Value!],
			YamlSequenceNode sequence => ScalarValues(sequence),
			_ => throw new InvalidOperationException($"{RelativePath}: needs must be a scalar or a sequence.")
		};
	}

	/// <summary>The steps of a job.</summary>
	public static List<YamlMappingNode> Steps(YamlMappingNode job)
	{
		List<YamlMappingNode> steps = [];
		if (Sequence(job, "steps") is { } sequence)
		{
			foreach (YamlNode step in sequence.Children)
			{
				steps.Add((YamlMappingNode) step);
			}
		}

		return steps;
	}

	/// <summary>The steps of a job whose <c>uses</c> starts with <paramref name="actionPrefix" />.</summary>
	public static List<YamlMappingNode> StepsUsing(YamlMappingNode job, string actionPrefix)
	{
		List<YamlMappingNode> steps = [];
		foreach (YamlMappingNode step in Steps(job))
		{
			if (Scalar(step, "uses") is { } uses && uses.StartsWith(actionPrefix, StringComparison.Ordinal))
			{
				steps.Add(step);
			}
		}

		return steps;
	}

	/// <summary>Every <c>run</c> script of a job, joined with newlines.</summary>
	public static string RunText(YamlMappingNode job)
	{
		List<string> scripts = [];
		foreach (YamlMappingNode step in Steps(job))
		{
			if (Scalar(step, "run") is { } run)
			{
				scripts.Add(run);
			}
		}

		return string.Join('\n', scripts);
	}

	/// <summary>A <c>with</c> input of a step or of a reusable-workflow call, or null.</summary>
	public static string? With(YamlMappingNode node, string input)
	{
		return Mapping(node, "with") is { } with ? Scalar(with, input) : null;
	}

	/// <summary>The job's <c>permissions</c> as scope → value; empty when the job inherits the top-level set.</summary>
	public static Dictionary<string, string> Permissions(YamlMappingNode job)
	{
		Dictionary<string, string> permissions = new(StringComparer.Ordinal);
		if (Mapping(job, "permissions") is { } mapping)
		{
			foreach (KeyValuePair<YamlNode, YamlNode> scope in mapping.Children)
			{
				permissions.Add(((YamlScalarNode) scope.Key).Value!, ((YamlScalarNode) scope.Value).Value!);
			}
		}

		return permissions;
	}

	/// <summary>An expression or script with every run of whitespace collapsed to one space and the ends trimmed.</summary>
	public static string Normalize(string? value)
	{
		return value is null ? "" : Whitespace().Replace(value, " ").Trim();
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

	/// <summary>The scalar items of a sequence.</summary>
	public static List<string> ScalarValues(YamlSequenceNode sequence)
	{
		List<string> values = [];
		foreach (YamlNode item in sequence.Children)
		{
			values.Add(((YamlScalarNode) item).Value!);
		}

		return values;
	}

	[GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex Whitespace();
}
