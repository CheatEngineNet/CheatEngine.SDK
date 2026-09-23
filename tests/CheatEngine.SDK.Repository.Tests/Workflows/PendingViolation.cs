namespace CheatEngine.SDK.Repository.Tests.Workflows;

/// <summary>A known violation in a file another work item owns, removed from the list together with its fix.</summary>
internal sealed record PendingViolation(string File, string Rule, string Subject, string Reason);
