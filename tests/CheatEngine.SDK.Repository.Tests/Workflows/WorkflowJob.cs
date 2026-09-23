using YamlDotNet.RepresentationModel;

namespace CheatEngine.SDK.Repository.Tests.Workflows;

/// <summary>One job of a workflow: either a runner job with steps, or a call to a reusable workflow (<c>uses:</c>).</summary>
internal sealed class WorkflowJob
{
	internal WorkflowJob(WorkflowFile workflow, string id, YamlMappingNode node)
	{
		Workflow = workflow;
		Id = id;
		Node = node;
	}

	/// <summary>The file that declares the job.</summary>
	public WorkflowFile Workflow
	{
		get;
	}

	/// <summary>The job id (its key under <c>jobs</c>).</summary>
	public string Id
	{
		get;
	}

	/// <summary>The job's mapping.</summary>
	public YamlMappingNode Node
	{
		get;
	}

	/// <summary>The display name, or null.</summary>
	public string? Name => WorkflowFile.Scalar(Node, "name");

	/// <summary>The <c>if:</c> condition, or null.</summary>
	public string? Condition => WorkflowFile.Scalar(Node, "if");

	/// <summary>The runner label when it is a scalar, or null.</summary>
	public string? RunsOn => WorkflowFile.Scalar(Node, "runs-on");

	/// <summary>The reusable workflow this job calls, or null for a runner job.</summary>
	public string? Uses => WorkflowFile.Scalar(Node, "uses");

	/// <summary>Whether the job calls a reusable workflow (GitHub rejects <c>runs-on</c> and <c>timeout-minutes</c> there).</summary>
	public bool CallsReusableWorkflow => Uses is not null;

	/// <summary>A readable locator for messages.</summary>
	public string Location => $"{Workflow.FileName} job '{Id}'";

	/// <summary>The steps, in order.</summary>
	public IReadOnlyList<YamlMappingNode> Steps => WorkflowFile.Mappings(WorkflowFile.Sequence(Node, "steps"));

	/// <summary>The job ids this job needs, whether <c>needs</c> is a scalar or a sequence.</summary>
	public IReadOnlyList<string> Needs()
	{
		if (!Node.Children.TryGetValue(new YamlScalarNode("needs"), out YamlNode? needs))
		{
			return [];
		}

		return needs switch
		{
			YamlScalarNode scalar => [scalar.Value!],
			YamlSequenceNode sequence => WorkflowFile.ScalarValues(sequence),
			_ => []
		};
	}

	/// <summary>The step with the given name, which must exist.</summary>
	public YamlMappingNode Step(string name)
	{
		int index = StepIndex(name);
		Assert.True(index >= 0, $"{Location} has no step named '{name}'.");
		return Steps[index];
	}

	/// <summary>The index of the step with the given name, or -1.</summary>
	public int StepIndex(string name)
	{
		IReadOnlyList<YamlMappingNode> steps = Steps;
		for (int index = 0; index < steps.Count; index++)
		{
			if (string.Equals(WorkflowFile.Scalar(steps[index], "name"), name, StringComparison.Ordinal))
			{
				return index;
			}
		}

		return -1;
	}

	/// <summary>The steps whose <c>uses:</c> starts with the given action reference (for example <c>actions/checkout@</c>).</summary>
	public List<YamlMappingNode> StepsUsing(string actionPrefix)
	{
		List<YamlMappingNode> matches = [];
		foreach (YamlMappingNode step in Steps)
		{
			string? uses = WorkflowFile.Scalar(step, "uses");
			if (uses is not null && uses.StartsWith(actionPrefix, StringComparison.Ordinal))
			{
				matches.Add(step);
			}
		}

		return matches;
	}

	/// <summary>Every <c>run:</c> script of the job, joined, for text rules.</summary>
	public string RunText()
	{
		List<string> scripts = [];
		foreach (YamlMappingNode step in Steps)
		{
			if (WorkflowFile.Scalar(step, "run") is { } run)
			{
				scripts.Add(run);
			}
		}

		return string.Join('\n', scripts);
	}

	/// <summary>A value of the step's <c>with:</c> mapping, or null.</summary>
	public static string? With(YamlMappingNode step, string key)
	{
		return WorkflowFile.Mapping(step, "with") is { } with ? WorkflowFile.Scalar(with, key) : null;
	}

	/// <summary>A value of the step's <c>env:</c> mapping, or null.</summary>
	public static string? Env(YamlMappingNode step, string key)
	{
		return WorkflowFile.Mapping(step, "env") is { } env ? WorkflowFile.Scalar(env, key) : null;
	}
}
