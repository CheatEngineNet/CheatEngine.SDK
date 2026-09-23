namespace CheatEngine.SDK.Repository.Tests.Governance;

/// <summary>One rule result reported by <c>Test-PullRequestPolicy</c>.</summary>
internal sealed record PolicyRuleResult(string Rule, bool Passed, string Message);
