namespace CheatEngine.SDK.Repository.Tests.Workflows;

/// <summary>A rule violation: the file, the subject (usually a job id) and a message naming the fix.</summary>
internal sealed record Violation(string File, string Subject, string Message);
