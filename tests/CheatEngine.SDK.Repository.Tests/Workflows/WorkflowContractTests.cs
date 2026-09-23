using System.Text.RegularExpressions;

using CheatEngine.SDK.Repository.Tests.Infrastructure;

using YamlDotNet.RepresentationModel;

namespace CheatEngine.SDK.Repository.Tests.Workflows;

/// <summary>
///     Freezes the CI interface of shared contract section 1 over the workflows and composite actions that exist, so a
///     workflow added later is checked on arrival. Each <c>WorkflowContractTests.&lt;Concern&gt;.cs</c> file covers one
///     concern of the contract (hygiene, restores, jobs, lint, gate); this file holds the shared readers.
/// </summary>
public sealed partial class WorkflowContractTests
{
	private static WorkflowFile Pipeline()
	{
		return WorkflowFile.LoadWorkflow(WorkflowContract.Pipeline);
	}

	private static WorkflowFile SetupAction()
	{
		foreach (WorkflowFile action in WorkflowFile.LoadActions())
		{
			if (string.Equals(action.RelativePath, WorkflowContract.SetupActionFile, StringComparison.Ordinal))
			{
				return action;
			}
		}

		Assert.Fail($"The composite action {WorkflowContract.SetupActionFile} is missing.");
		return null!;
	}

	private static string ReadRepositoryText(string relativePath)
	{
		string path = Path.Combine(RepositoryRoot.Path, relativePath);
		Assert.True(File.Exists(path), $"{relativePath} is missing.");
		return File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
	}

	/// <summary>Every workflow and composite action.</summary>
	private static List<WorkflowFile> AllActionFiles()
	{
		return [.. WorkflowFile.LoadWorkflows(), .. WorkflowFile.LoadActions()];
	}

	private static List<YamlMappingNode> AllSteps(WorkflowFile file)
	{
		List<YamlMappingNode> steps = [];
		foreach ((string _, YamlMappingNode step) in AllStepsWithSubject(file))
		{
			steps.Add(step);
		}

		return steps;
	}

	/// <summary>Every step with its job id (workflows) or <c>composite</c> (actions).</summary>
	private static List<(string Subject, YamlMappingNode Step)> AllStepsWithSubject(WorkflowFile file)
	{
		List<(string, YamlMappingNode)> steps = [];
		foreach (WorkflowJob job in file.Jobs())
		{
			foreach (YamlMappingNode step in job.Steps)
			{
				steps.Add((job.Id, step));
			}
		}

		foreach (YamlMappingNode step in file.ActionSteps())
		{
			steps.Add(("composite", step));
		}

		return steps;
	}

	/// <summary>A PowerShell script without its comment lines and comment blocks.</summary>
	private static string StripComments(string script)
	{
		string withoutBlocks = CommentBlock().Replace(script, "");
		List<string> lines = [];
		foreach (string line in withoutBlocks.Split('\n'))
		{
			if (!line.TrimStart().StartsWith('#'))
			{
				lines.Add(line);
			}
		}

		return string.Join('\n', lines);
	}

	[GeneratedRegex(@"<#.*?#>", RegexOptions.Singleline | RegexOptions.CultureInvariant, 1000)]
	private static partial Regex CommentBlock();
}
