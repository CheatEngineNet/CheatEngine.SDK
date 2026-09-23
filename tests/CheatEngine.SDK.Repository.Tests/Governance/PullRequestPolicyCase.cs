namespace CheatEngine.SDK.Repository.Tests.Governance;

/// <summary>One pull request evaluated by <c>eng/ci/PullRequestPolicy.psm1</c> and the rules expected to fail.</summary>
internal sealed record PullRequestPolicyCase(
	string Name,
	string Title,
	string[] ChangedFiles,
	string[] ExpectedFailures,
	string Body = "",
	string Author = "contributor");
